using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mini
{
    public sealed class Req
    {
        public string Method = "GET";
        public string Path = "/";
        public string Query = "";
        public string Body = "";
        public string Origin = "";
    }

    public sealed class Resp
    {
        public int Status = 200;
        public string Body = "";
        public byte[] Raw = null;                      // when set, served verbatim (bridge assets: bytes must match disk)
        public string ContentType = "application/json; charset=utf-8";
        public Resp(int s, string b) { Status = s; Body = b ?? ""; }
    }

    // Shared request routing, used by both the HttpListener and the raw TCP backend.
    public sealed class Core
    {
        public Store Store = new Store();
        public Config Cfg = Config.Load();
        public CdpClient Cdp;
        public HttpServer Http;
        public string Backend = "-";
        public int Port = 0;
        public long StartedUtc = DateTime.UtcNow.Ticks;
        public int PostCount = 0;
        public long LastPostTicks = 0;
        public long LastGetTicks = 0;
        public Func<string, bool> ClickHook;          // logical icon click
        public Func<string, string> DebugProbe;        // "dbg-pointer" | "dbg-layout" -> one-line JSON
        public Func<string> HotspotsJson;             // current hotspot dump
        public Action<string> UiAction;               // "hide" "show" "toggle" "top" "theme" "queue" "taskbar:on|off" "quit"
        public string LastCommandWire = "";
        public bool LastClickThrough = false;
        public string CoverState = "sleeve";
        public string BridgeBuild = null;
        public string LastBridgeProbe = "";
        public string PortHandoff = "pending";
        public string BridgeInject = "never";           // self-distribution: last inject attempt outcome
        public int BridgeInjectCount = 0;

        // Bridge assets are read from <netease-mini>\bridge\ and served verbatim, so the overlay can
        // hand the page its own bootstrap (no external temporary HTTP server in the loop any more).
        public static byte[] BridgeBytes(string name)
        {
            try
            {
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Paths.Root, "bridge"));
                string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, name));
                if (!full.StartsWith(dir + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
                if (!System.IO.File.Exists(full)) return null;
                return System.IO.File.ReadAllBytes(full);
            }
            catch (Exception e) { Log.W("bridge asset read fail " + name + " " + e.Message); return null; }
        }

        public bool HttpIdleMs(int ms)
        {
            long t = Interlocked.Read(ref LastPostTicks);
            if (t == 0) return true;
            return (DateTime.UtcNow.Ticks - t) > TimeSpan.TicksPerMillisecond * ms;
        }

        // ---- L1 (latency brief) command delivery: CDP first, HTTP response as the fallback --------------
        // Old path: IssueCommand only enqueued, and the queue was drained by whichever POST /state response
        // happened to come next (250ms cadence playing, 1500ms paused). The paused-state worst case was one
        // full POST cycle = the 1633ms the parent measured on "prev", while the CDP socket sat connected and
        // unused (/info cdp=1 source=cdp) because CdpIdlePreferred() demanded 1200ms of HTTP silence that a
        // live bridge never gives.
        // New path: the command is FIRED over CDP the moment it is issued, and a short read burst then makes
        // its effect observable on GET /state without waiting for the next scheduled frame. Exactly one
        // carrier per command id: CDP success never enqueues, CDP failure enqueues for the HTTP response.
        public int CdpCmdSent = 0;                  // commands fired over CDP (verdict 'sent')
        public int CdpCmdFail = 0;                  // commands that fell back to the HTTP response channel
        public long LastCmdCdpMs = -1;              // round-trip cost of the last CDP fire
        public string LastCmdCarrier = "-";         // "cdp" | "cdp-retry" | "http-fallback"
        public string LastCmdCdpErr = "";           // why the last fire did not take
        public int FastReads = 0, FastReadApplied = 0;
        public long LastFastReadMs = -1;
        public string LastCmdAck = "";              // the bridge's own settle verdict for the last command

        public void IssueCommand(string name, JValue arg)
        {
            Command c = new Command(name, arg);
            LastCommandWire = c.ToWire();
            Log.W("cmd enqueue " + LastCommandWire);
            long t0 = DateTime.UtcNow.Ticks;
            string verdict = CdpFire(c, 300);
            long ms = (DateTime.UtcNow.Ticks - t0) / TimeSpan.TicksPerMillisecond;
            if (verdict == "sent")
            {
                CdpCmdSent++; LastCmdCdpMs = ms; LastCmdCarrier = "cdp"; LastCmdCdpErr = "";
                Log.W("cmd cdp " + c.Name + " id=" + c.Id + " -> sent ms=" + ms + " carrier=cdp (http response not used)");
                BeginFastRead(c);
                return;
            }
            CdpCmdFail++; LastCmdCdpMs = ms; LastCmdCarrier = "http-fallback"; LastCmdCdpErr = verdict;
            Log.W("cmd cdp " + c.Name + " id=" + c.Id + " -> " + verdict + " ms=" + ms + " carrier=http-fallback");
            Store.Enqueue(c);
        }

        // One CDP round trip, budget ms. Returns "sent" when the page took the command.
        public string CdpFire(Command c, int budgetMs)
        {
            CdpClient cdp = Cdp;
            if (cdp == null) return "no-cdp-client";
            if (!cdp.Connected) return "cdp-disconnected";
            try
            {
                string r = cdp.EvaluateFast(c.ToCdpFireExpression(), budgetMs);
                if (r == null) return "eval-timeout-" + budgetMs + "ms";
                // 'dup' means this very id was already fired over CDP: the page has it, so it counts as
                // delivered and must NOT be re-queued for the HTTP response (that would be a double send).
                if (r == "sent" || r == "dup") return "sent";
                return "verdict:" + Trunc(r, 60);
            }
            catch (Exception e) { return "err:" + e.GetType().Name + ":" + Trunc(e.Message, 60); }
        }

        private long fastReadUntilTicks;
        private int fastReadRunning;

        // The command is in the page; this makes its EFFECT observable. GET /state answers from the store,
        // and the store only moves when a frame is applied - so without a read burst the observer still
        // waits for the next scheduled frame (CDP poll 500/1500ms, bridge POST 250/1500ms) even though the
        // page changed within ~100ms. One burst at a time; a second command only extends the deadline.
        // Runs on a pool thread: a UI-thread click must never wait for it.
        public void BeginFastRead(Command c)
        {
            CdpClient cdp = Cdp;
            if (cdp == null || !cdp.Connected) return;
            Interlocked.Exchange(ref fastReadUntilTicks, DateTime.UtcNow.Ticks + TimeSpan.TicksPerMillisecond * 900);
            if (Interlocked.CompareExchange(ref fastReadRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate (object o)
            {
                long t0 = DateTime.UtcNow.Ticks;
                int reads = 0, applied = 0;
                try
                {
                    while (reads < 18 && DateTime.UtcNow.Ticks < Interlocked.Read(ref fastReadUntilTicks))
                    {
                        reads++;
                        string json = cdp.EvaluateFast(CdpClient.ReadExpr, 250);
                        if (json != null && json.Length > 2 && json[0] == '{')
                        {
                            FastReads++;
                            // authoritative: this frame is the direct consequence of a command WE just
                            // delivered over CDP, so the channel-preference gate must not drop it.
                            if (Store.Apply(json, "cdp", true)) applied++;
                        }
                        Thread.Sleep(45);
                    }
                    string ack = cdp.EvaluateFast("(function(){try{var k=window.__NCM_MINI_CDPACK__;return k?JSON.stringify(k):null}catch(e){return null}})()", 200);
                    if (ack != null) { LastCmdAck = Trunc(ack, 160); Log.W("cmd ack after " + c.Name + " id=" + c.Id + " :: " + LastCmdAck); }
                }
                catch (Exception e) { Log.W("fast-read fail " + e.GetType().Name + " " + e.Message); }
                finally
                {
                    LastFastReadMs = (DateTime.UtcNow.Ticks - t0) / TimeSpan.TicksPerMillisecond;
                    Log.W("fast-read done cmd=" + c.Name + " id=" + c.Id + " reads=" + reads + " applied=" + applied + " ms=" + LastFastReadMs);
                    Interlocked.Exchange(ref fastReadRunning, 0);
                }
            });
        }

        // Retry half of the fallback: the queue only ever holds commands whose immediate CDP fire failed
        // (or that were issued while CDP was down). A success here removes the command from the queue, so
        // the POST /state response can never carry the same id afterwards; a failure puts it back at the
        // head with its original id and the HTTP response stays its carrier.
        public int TryFlushCdp()
        {
            if (Cdp == null || !Cdp.Connected) return 0;
            int sent = 0;
            for (int i = 0; i < 8; i++)
            {
                Command c = Store.Take();
                if (c == null) break;
                string verdict = CdpFire(c, 300);
                if (verdict == "sent")
                {
                    sent++; CdpCmdSent++; LastCmdCarrier = "cdp-retry"; LastCmdCdpErr = "";
                    Log.W("cmd cdp-retry " + c.Name + " id=" + c.Id + " -> sent");
                    BeginFastRead(c);
                }
                else
                {
                    Store.Requeue(c);
                    Log.W("cmd cdp-retry " + c.Name + " id=" + c.Id + " -> " + verdict + " (kept queued for the http response)");
                    break;
                }
            }
            return sent;
        }

        public static string Trunc(string s, int n)
        {
            if (s == null) return "null";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= n ? s : s.Substring(0, n) + "...";
        }

        public string InfoJson()
        {
            State s = Store.Snapshot();
            return new JsonWriter().Begin()
                .B("ok", true)
                .Str("app", "MiniPlayer")
                .Str("protocol", "v1")
                .Str("backend", Backend)
                .Num("port", Port)
                .Num("pid", ProcessId())
                .Num("posts", PostCount)
                .Num("cdpConnected", Cdp != null && Cdp.Connected ? 1 : 0)
                .Num("cdpEvals", Cdp == null ? 0 : Cdp.EvalCount)
                .Num("frames", Store.FrameCount)
                .Num("applied", Store.AppliedCount)
                .Str("applyReason", Store.LastApplyReason)
                .Str("source", s == null ? "" : s.Source)
                .Str("lastCommand", LastCommandWire)
                // L1: which carrier actually took the last command, and what the immediate CDP fire cost.
                .Num("cdpCmdSent", CdpCmdSent).Num("cdpCmdFail", CdpCmdFail)
                .Num("lastCmdCdpMs", LastCmdCdpMs).Str("lastCmdCarrier", LastCmdCarrier)
                .Str("lastCmdCdpErr", LastCmdCdpErr)
                .Num("fastReads", FastReads).Num("fastReadApplied", FastReadApplied)
                .Num("lastFastReadMs", LastFastReadMs).Str("lastCmdAck", LastCmdAck)
                .Num("pending", Store.PendingCount)
                .Num("ageMs", Store.AgeSinceLastIngestMs())
                .Num("changeAgeMs", Store.AgeSinceLastApplyMs())
                .Num("postAgeMs", LastPostTicks == 0 ? long.MaxValue : (DateTime.UtcNow.Ticks - Interlocked.Read(ref LastPostTicks)) / TimeSpan.TicksPerMillisecond)
                .Str("bridgeBuild", BridgeBuild)
                .Str("portHandoff", PortHandoff)
                .Str("pageBuildProbe", LastBridgeProbe)
                .Str("bridgeInject", BridgeInject)
                .Num("bridgeInjectCount", BridgeInjectCount)
                .Str("coverState", CoverState)
                .End().ToString();
        }

        public static double ProcessId()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; } catch { return 0; }
        }

        public Resp Handle(Req r)
        {
            string p = r.Path;
            if (r.Method == "OPTIONS") return new Resp(204, "");
            if (p == "/state")
            {
                if (r.Method == "POST")
                {
                    Interlocked.Increment(ref PostCount);
                    Interlocked.Exchange(ref LastPostTicks, DateTime.UtcNow.Ticks);
                    bool changed = Store.Apply(r.Body, "http");
                    Command c = Store.Take();
                    string body = c == null ? "{}" : c.ToWire();
                    Log.W("POST /state accepted=true changed=" + (changed ? "true" : "false") + " reason=" + Store.LastApplyReason + " frames=" + Store.FrameCount + " bytes=" + (r.Body == null ? 0 : r.Body.Length) + " reply=" + Core.Trunc(body, 90));
                    return new Resp(200, body);
                }
                if (r.Method == "GET") return new Resp(200, Store.RawJson());
                return new Resp(405, "{}");
            }
            if (p == "/health" || p == "/info") return new Resp(200, InfoJson());
            // Self-distribution: the page loads its bridge from us, we read the file from disk.
            if (p == "/mini.js" || p == "/mini.css")
            {
                string name = p == "/mini.js" ? "mini.js" : "mini.css";
                byte[] raw = BridgeBytes(name);
                if (raw == null) { Log.W("bridge asset 404 " + name + " root=" + Paths.Root); return new Resp(404, "{\"ok\":false,\"err\":\"no " + name + "\"}"); }
                Resp file = new Resp(200, "");
                file.Raw = raw;
                file.ContentType = name == "mini.js" ? "application/javascript; charset=utf-8" : "text/css; charset=utf-8";
                Log.W("bridge asset served " + name + " bytes=" + raw.Length);
                return file;
            }
            if (p == "/hotspots") return new Resp(200, HotspotsJson == null ? "{}" : HotspotsJson());
            if (p == "/cmd" && (r.Method == "POST" || r.Method == "GET"))
            {
                if (r.Method == "POST")
                {
                    JValue v;
                    if (!JsonParser.TryParse(r.Body, out v) || v == null) return new Resp(400, "{\"ok\":false,\"err\":\"bad json\"}");
                    string name = v.Str("cmd");
                    if (string.IsNullOrEmpty(name)) name = v.Str("name");
                    if (string.IsNullOrEmpty(name)) return new Resp(400, "{\"ok\":false,\"err\":\"no cmd\"}");
                    IssueCommand(name, v.Get("arg"));
                    return new Resp(200, "{\"ok\":true,\"delivered\":\"queued\"}");
                }
                string n2 = QueryVal(r.Query, "cmd");
                if (string.IsNullOrEmpty(n2)) return new Resp(400, "{\"ok\":false}");
                IssueCommand(n2, null);
                return new Resp(200, "{\"ok\":true}");
            }
            if (p == "/click" && r.Method == "POST")
            {
                string which = null;
                JValue v;
                if (JsonParser.TryParse(r.Body, out v) && v != null) which = v.Str("which");
                if (string.IsNullOrEmpty(which)) which = QueryVal(r.Query, "which");
                if (string.IsNullOrEmpty(which)) return new Resp(400, "{\"ok\":false,\"err\":\"no which\"}");
                bool ok = ClickHook != null && ClickHook(which);
                return new Resp(ok ? 200 : 404, "{\"ok\":" + (ok ? "true" : "false") + ",\"which\":" + JsonWriter.Encode(which) + "}");
            }
            if (p.StartsWith("/ctl/"))
            {
                string verb = p.Substring(5);
                if (verb == "state") { Interlocked.Exchange(ref LastGetTicks, DateTime.UtcNow.Ticks); return new Resp(200, Store.RawJson()); }
                // R1/R2: read-only self-report endpoints. No input, no foreground, safe while the user is busy.
                // R1/R2 + T1c: read-only / diagnostic self-report endpoints. This branch must answer before
                // the generic UiAction handler, which would otherwise return {"ok":true,"verb":...} with no payload.
                if ((verb == "dbg-pointer" || verb == "dbg-layout" || verb == "dbg-mask" || verb.StartsWith("dbg-probe-reset")) && DebugProbe != null)
                {
                    string budget = QueryVal(r.Query, "budget");
                    string v2 = verb.StartsWith("dbg-probe-reset") && !string.IsNullOrEmpty(budget) ? verb + ":" + budget : verb;
                    return new Resp(200, DebugProbe(v2));
                }
                if (UiAction != null) UiAction(verb);
                return new Resp(200, "{\"ok\":true,\"verb\":" + JsonWriter.Encode(verb) + "}");
            }
            if (p == "/" || p == "/index")
                return new Resp(200, "{\"app\":\"MiniPlayer\",\"endpoints\":[\"POST /state\",\"GET /state\",\"GET /health\",\"POST /cmd\",\"POST /click\",\"GET|POST /ctl/hide|show|toggle|top|theme|queue|taskbar|quit\"]}");
            return new Resp(404, "{\"ok\":false,\"err\":\"not found\"}");
        }

        public static string QueryVal(string q, string key)
        {
            if (string.IsNullOrEmpty(q)) return null;
            foreach (string kv in q.Split('&'))
            {
                int eq = kv.IndexOf('=');
                string k = eq < 0 ? kv : kv.Substring(0, eq);
                if (k == key) return WebUtility.UrlDecode(eq < 0 ? "" : kv.Substring(eq + 1));
            }
            return null;
        }
    }

    public sealed class HttpServer
    {
        private readonly Core core;
        private HttpListener listener;
        private TcpListener tcp;
        private Thread worker;
        private volatile bool running;
        public string Backend { get { return core.Backend; } }
        public int Port { get { return core.Port; } }
        public string FailReason = "";

        public HttpServer(Core c) { core = c; }

        public bool Start(int basePort)
        {
            running = true;
            if (TryHttpListener(basePort)) { worker = new Thread(HttpListenerLoop); }
            else if (TryTcp(basePort)) { worker = new Thread(TcpLoop); }
            else { running = false; return false; }
            worker.IsBackground = true;
            worker.Start();
            Paths.WriteText(Paths.Runtime("port.json"),
                new JsonWriter().Begin()
                    .Num("port", core.Port).Num("pid", Core.ProcessId())
                    .Str("backend", core.Backend)
                    .Str("prefix", "http://127.0.0.1:" + core.Port + "/")
                    .B("urlaclNeeded", !string.IsNullOrEmpty(FailReason))
                    .Str("reason", FailReason)
                    .Str("at", DateTime.Now.ToString("s", CultureInfo.InvariantCulture)).End().ToString());
            Log.W("server backend=" + core.Backend + " port=" + core.Port + " reason=" + FailReason);
            return true;
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch { }
            try { if (tcp != null) tcp.Stop(); } catch { }
        }

        private bool TryHttpListener(int basePort)
        {
            for (int port = basePort; port <= basePort + 9; port++)
            {
                HttpListener l = new HttpListener();
                l.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
                l.Prefixes.Add("http://127.0.0.1:" + port + "/");
                try
                {
                    l.Start();
                    listener = l;
                    core.Backend = "HttpListener";
                    core.Port = port;
                    return true;
                }
                catch (HttpListenerException e)
                {
                    FailReason = "HttpListenerException code=" + e.ErrorCode + " " + e.Message;
                    Log.W("HttpListener port " + port + " failed: " + FailReason);
                    // 5 = ERROR_ACCESS_DENIED (urlacl) -> no point trying more ports this way
                    if (e.ErrorCode == 5) { try { l.Close(); } catch { } return false; }
                    try { l.Close(); } catch { }
                }
                catch (Exception e)
                {
                    FailReason = e.GetType().Name + ": " + e.Message;
                    Log.W("HttpListener port " + port + " failed: " + FailReason);
                    if (e is UnauthorizedAccessException) return false;
                    try { l.Close(); } catch { }
                }
            }
            return false;
        }

        private bool TryTcp(int basePort)
        {
            for (int port = basePort; port <= basePort + 9; port++)
            {
                try
                {
                    TcpListener t = new TcpListener(IPAddress.Loopback, port);
                    t.Start();
                    tcp = t;
                    core.Backend = "TcpListener";
                    core.Port = port;
                    return true;
                }
                catch (SocketException e)
                {
                    FailReason = "SocketException " + e.SocketErrorCode;
                    Log.W("TcpListener port " + port + " failed: " + FailReason);
                }
            }
            return false;
        }

        private void HttpListenerLoop()
        {
            IAsyncResult ar = null;
            while (running)
            {
                try
                {
                    if (ar == null) ar = listener.BeginGetContext(null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(500)) continue;
                    HttpListenerContext ctx = listener.EndGetContext(ar);
                    ar = null;
                    ThreadPool.QueueUserWorkItem(HttpOne, ctx);
                }
                catch (HttpListenerException) { if (!running) break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { Log.W("hl loop " + e.Message); if (!running) break; }
            }
        }

        private void HttpOne(object o)
        {
            HttpListenerContext ctx = (HttpListenerContext)o;
            try
            {
                Req r = new Req();
                r.Method = ctx.Request.HttpMethod;
                r.Path = ctx.Request.Url.AbsolutePath;
                r.Query = ctx.Request.Url.Query.TrimStart('?');
                r.Origin = ctx.Request.Headers["Origin"] ?? "";
                if (r.Method == "POST" && ctx.Request.HasEntityBody)
                {
                    using (Stream s = ctx.Request.InputStream)
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        r.Body = Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
                Resp resp = core.Handle(r);
                byte[] data = resp.Raw != null ? resp.Raw : Encoding.UTF8.GetBytes(resp.Body);
                ctx.Response.StatusCode = resp.Status;
                ctx.Response.ContentType = resp.ContentType;
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                ctx.Response.AddHeader("Access-Control-Max-Age", "86400");
                ctx.Response.AddHeader("Cache-Control", "no-store");
                if (resp.Status == 204) ctx.Response.ContentLength64 = 0;
                else
                {
                    ctx.Response.ContentLength64 = data.Length;
                    ctx.Response.OutputStream.Write(data, 0, data.Length);
                }
            }
            catch (Exception e) { Log.W("http one: " + e.Message); }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        private void TcpLoop()
        {
            while (running)
            {
                TcpClient c = null;
                try { c = tcp.AcceptTcpClient(); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                ThreadPool.QueueUserWorkItem(TcpOne, c);
            }
        }

        private void TcpOne(object o)
        {
            TcpClient c = (TcpClient)o;
            try
            {
                c.ReceiveTimeout = 4000;
                c.SendTimeout = 4000;
                NetworkStream ns = c.GetStream();
                Req r = ParseHttp(ns);
                if (r == null) { try { c.Close(); } catch { } return; }
                Resp resp = core.Handle(r);
                byte[] data = resp.Raw != null ? resp.Raw : Encoding.UTF8.GetBytes(resp.Body);
                StringBuilder b = new StringBuilder();
                b.Append("HTTP/1.1 ").Append(resp.Status).Append(' ').Append(Reason(resp.Status)).Append("\r\n");
                b.Append("Server: MiniPlayer/1.0\r\n");
                b.Append("Access-Control-Allow-Origin: *\r\n");
                b.Append("Access-Control-Allow-Methods: POST, GET, OPTIONS\r\n");
                b.Append("Access-Control-Allow-Headers: Content-Type\r\n");
                b.Append("Access-Control-Max-Age: 86400\r\n");
                b.Append("Cache-Control: no-store\r\n");
                b.Append("Content-Type: ").Append(resp.ContentType).Append("\r\n");
                b.Append("Content-Length: ").Append(resp.Status == 204 ? 0 : data.Length).Append("\r\n");
                b.Append("Connection: close\r\n\r\n");
                byte[] head = Encoding.ASCII.GetBytes(b.ToString());
                ns.Write(head, 0, head.Length);
                if (resp.Status != 204 && data.Length > 0) ns.Write(data, 0, data.Length);
                ns.Flush();
            }
            catch (Exception e) { Log.W("tcp one: " + e.GetType().Name + " " + e.Message); }
            finally { try { c.Close(); } catch { } }
        }

        private static string Reason(int code)
        {
            switch (code)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                default: return "OK";
            }
        }

        private static Req ParseHttp(NetworkStream ns)
        {
            MemoryStream buf = new MemoryStream();
            byte[] one = new byte[1];
            int headerEnd = -1;
            int guard = 0;
            while (guard++ < 200000)
            {
                int n = ns.Read(one, 0, 1);
                if (n <= 0) break;
                buf.WriteByte(one[0]);
                if (buf.Length >= 4)
                {
                    byte[] a = buf.ToArray();
                    if (a[a.Length - 4] == '\r' && a[a.Length - 3] == '\n' && a[a.Length - 2] == '\r' && a[a.Length - 1] == '\n') { headerEnd = a.Length; break; }
                }
            }
            if (headerEnd < 0 && buf.Length == 0) return null;
            byte[] all = buf.ToArray();
            string headText = Encoding.ASCII.GetString(all, 0, all.Length);
            string[] lines = headText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || lines[0].Length == 0) return null;
            Req r = new Req();
            string[] parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;
            r.Method = parts[0].ToUpperInvariant();
            string url = parts[1];
            int q = url.IndexOf('?');
            r.Path = q < 0 ? url : url.Substring(0, q);
            r.Query = q < 0 ? "" : url.Substring(q + 1);
            int clen = 0;
            foreach (string ln in lines)
            {
                int ci = ln.IndexOf(':');
                if (ci <= 0) continue;
                string k = ln.Substring(0, ci).Trim().ToUpperInvariant();
                string v = ln.Substring(ci + 1).Trim();
                if (k == "CONTENT-LENGTH") int.TryParse(v, out clen);
                if (k == "ORIGIN") r.Origin = v;
            }
            if (headerEnd < 0) return null;
            int have = all.Length - headerEnd;
            MemoryStream body = new MemoryStream();
            if (have > 0) body.Write(all, headerEnd, have);
            while (body.Length < clen)
            {
                byte[] chunk = new byte[Math.Max(1, Math.Min(8192, clen - (int)body.Length))];
                int n = ns.Read(chunk, 0, chunk.Length);
                if (n <= 0) break;
                body.Write(chunk, 0, n);
            }
            if (clen > 0) r.Body = Encoding.UTF8.GetString(body.ToArray());
            return r;
        }
    }

    public sealed class CdpClient
    {
        private readonly Core core;
        private Thread loop;
        private volatile bool running;
        private readonly object gate = new object();
        private ClientWebSocket ws;
        private int nextId = 1;
        private CancellationTokenSource cts;
        public volatile bool Connected;
        public string LastError = "";
        public int EvalCount = 0;
        public int ReadOkCount = 0;
        public string PageUrl = "";
        public int Port = 9222;

        public CdpClient(Core c) { core = c; }

        public void Start()
        {
            running = true;
            loop = new Thread(Loop);
            loop.IsBackground = true;
            loop.Start();
        }

        public void Stop()
        {
            running = false;
            try { if (cts != null) cts.Cancel(); } catch { }
            CloseWs();
        }

        private void Loop()
        {
            while (running)
            {
                try
                {
                    if (!Connected)
                    {
                        string url = Discover();
                        if (url == null) { Thread.Sleep(2000); continue; }
                        Connect(url);
                        if (Connected)
                        {
                            // Startup order matters: attach -> make sure the bridge is on the page (self-distributed
                            // over our own HTTP /mini.js) -> only then hand the port over to it.
                            EnsureBridge("cold");
                            HandoffPort(); nextHandoff = DateTime.UtcNow.Ticks + TimeSpan.TicksPerSecond * 10;
                        }
                    }
                    if (Connected)
                    {
                        string json = Evaluate(ReadExpr, 2500);
                        EvalCount++;
                        if (json != null && json.Length > 2 && json[0] == '{')
                            { lastReadOkTicks = DateTime.UtcNow.Ticks; if (core.Store.Apply(json, "cdp")) ReadOkCount++; }
                        // Self-heal: a page reload wipes window.__NCM_MINI__ and every injected tag.
                        // Re-check at most every 3 s and re-distribute the bridge when it is gone.
                        long checkNow = DateTime.UtcNow.Ticks;
                        if (lastBridgeCheckTicks == 0 || (checkNow - lastBridgeCheckTicks) / TimeSpan.TicksPerMillisecond > 3000)
                        {
                            lastBridgeCheckTicks = checkNow;
                            if (!BridgeUp()) EnsureBridge(json == null ? "read-null" : "poll-check");
                        }
                        core.TryFlushCdp();
                        ProbeBridgeBuild();
                        long nowTicks = DateTime.UtcNow.Ticks;
                        if (nextHandoff != 0 && nowTicks >= nextHandoff) { HandoffPort(); nextHandoff = nowTicks + TimeSpan.TicksPerSecond * 10; }
                        int playingMs = 500, pausedMs = 1500;
                        State s = core.Store.Snapshot();
                        Thread.Sleep(s != null && s.Playing ? playingMs : pausedMs);
                    }
                }
                catch (ThreadAbortException) { break; }
                catch (Exception e) { LastError = e.GetType().Name + ": " + e.Message; Thread.Sleep(1500); }
            }
            CloseWs();
        }

        public const string ReadExpr = "window.__NCM_MINI__ ? JSON.stringify(window.__NCM_MINI__.state()) : null";
        public const string BuildExpr = "(function(){try{var a=window.__NCM_MINI__;return (a&&a.build)?String(a.build):null}catch(e){return null}})()";
        private long nextBuildProbe = 0;
        private long nextHandoff = 0;
        private long lastReadOkTicks = 0;
        private long lastInjectTicks = 0;
        private long lastBridgeCheckTicks = 0;

        // --- self-distribution of the bridge (task: closed-loop auto-start) ---------------------
        // The page's CSP script-src whitelist already contains http://localhost, so a single script
        // tag is enough to bring the bridge up from our own port. Nothing external is required.
        public bool BridgeUp()
        {
            string v = Evaluate("(function(){try{var a=window.__NCM_MINI__;return (a&&typeof a.state==='function')?'up':'down'}catch(e){return 'err'}})()", 1200);
            return v == "up";
        }

        public string EnsureBridge(string why)
        {
            try
            {
                long now = DateTime.UtcNow.Ticks;
                if (lastInjectTicks != 0 && (now - lastInjectTicks) / TimeSpan.TicksPerMillisecond < 2500) return "throttled";
                lastInjectTicks = now;
                if (!Connected) { core.BridgeInject = "no cdp"; return core.BridgeInject; }
                if (PageUrl.IndexOf("orpheus", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    core.BridgeInject = "skip non-orpheus page=" + Core.Trunc(PageUrl, 60);
                    Log.W("inject " + core.BridgeInject);
                    return core.BridgeInject;
                }
                if (BridgeUp())
                {
                    core.BridgeInject = "already up (" + why + ")";
                    return core.BridgeInject;
                }
                int port = core.Port;
                if (port <= 0) { core.BridgeInject = "no http port yet"; Log.W("inject " + core.BridgeInject); return core.BridgeInject; }
                string url = "http://localhost:" + port + "/mini.js";
                // Two delivery paths in one page-side promise:
                //   1) <script src=...>: only honoured when the page CSP lists our host:port.
                //      Measured on app.html: script-src has 'http://localhost' WITHOUT a port and Chrome/CEF
                //      REFUSES 'http://localhost:46210/mini.js' ("Refused to load the script ... violates CSP"),
                //      so this path alone cannot be the closed loop.
                //   2) fetch + indirect eval: the same CSP carries 'unsafe-eval' and connect-src allows http:,
                //      and the page can already XHR our port (measured 200 / 56485 chars), so this always works.
                // The page never sees a hand-written copy of the bridge: both paths read bridge\mini.js
                // through our own HTTP handler, which streams the file bytes verbatim.
                string expr =
                    "(function(){try{"
                  + "var U=" + JsonWriter.Encode(url) + ";var d=document;"
                  + "if(!d||!d.documentElement)return Promise.resolve('no-dom');"
                  + "window.__NCM_MINI_INJECT_ERR__=null;window.__NCM_MINI_INJECT_VIA__='none';window.__NCM_MINI_INJECT_MS__=Date.now();"
                  + "var up=function(){var a=window.__NCM_MINI__;return !!(a&&typeof a.state==='function')};"
                  + "if(up())return Promise.resolve('already');"
                  + "var s=null;try{s=d.createElement('script');s.async=true;s.src=U;s.setAttribute('data-ncmmini','overlay-self-inject');"
                  + "s.onerror=function(){try{window.__NCM_MINI_INJECT_ERR__='script-refused'}catch(e9){}};(d.head||d.documentElement).appendChild(s);}catch(e1){}"
                  + "return new Promise(function(resolve){var settled=false;"
                  + "function fin(how){if(settled)return;settled=true;window.__NCM_MINI_INJECT_VIA__=how;"
                  + "resolve(how+' bridge='+(up()?'up':'down')+' ms='+(Date.now()-window.__NCM_MINI_INJECT_MS__));}"
                  + "function dropTag(){try{if(s&&s.parentNode)s.parentNode.removeChild(s);}catch(e2){}}"
                  + "function evalSrc(tag,code){try{(0,eval)(code);setTimeout(function(){fin(tag+' bytes='+code.length)},250);"
                  + "}catch(e3){fin(tag+' eval-err '+String(e3&&e3.message))}}"
                  + "function viaXhr(){try{var x=new XMLHttpRequest();x.open('GET',U,true);x.timeout=4000;"
                  + "x.onload=function(){dropTag();evalSrc('xhr-eval status='+x.status,x.responseText)};"
                  + "x.onerror=function(){fin('all-failed xhr-error')};x.ontimeout=function(){fin('all-failed xhr-timeout')};x.send();"
                  + "}catch(e4){fin('all-failed xhr '+String(e4&&e4.message))}}"
                  + "function viaFetch(){try{fetch(U,{cache:'no-store'}).then(function(r){return r.text().then(function(t){return [r.status,t]})})"
                  + ".then(function(st){dropTag();evalSrc('fetch-eval status='+st[0],st[1])}).catch(function(e5){viaXhr()})}"
                  + "catch(e6){fin('all-failed fetch '+String(e6&&e6.message))}}"
                  + "setTimeout(function(){if(up()){fin('tag');return}viaFetch()},500);"
                  + "setTimeout(function(){if(!settled&&up())fin('tag-late');else if(!settled)fin('timeout')},4500);});"
                  + "}catch(e){return 'err '+String((e&&e.message)||e)}})()";
                string tag = Evaluate(expr, 7000);
                long waited = 0; string st = "down";
                for (int i = 0; i < 30; i++)
                {
                    if (BridgeUp()) { st = "up"; break; }
                    Thread.Sleep(130); waited += 130;
                }
                core.BridgeInjectCount++;
                core.BridgeInject = why + " :: " + (tag == null ? "eval-null" : tag) + " -> " + st + " (+wait " + waited + "ms) via " + url;
                Log.W("inject " + core.BridgeInject);
                return core.BridgeInject;
            }
            catch (Exception e) { core.BridgeInject = "inject err " + e.Message; Log.W(core.BridgeInject); return core.BridgeInject; }
        }

        // v1.3 #2 port handoff: the page cannot read local files, so the overlay pushes
        // port.json through CDP and then calls connect(). Skipped when the page already
        // points at our own port (avoids fighting with a concurrent mock).
        public void HandoffPort()
        {
            try
            {
                if (!Connected) { core.PortHandoff = "no cdp"; return; }
                string infoTxt = Evaluate("(function(){try{var a=window.__NCM_MINI__;if(!a||typeof a.info!=='function')return null;return JSON.stringify(a.info());}catch(e){return null}})()", 2000);
                int pagePort = 0;
                JValue iv;
                if (infoTxt != null && JsonParser.TryParse(infoTxt, out iv) && iv != null) pagePort = (int)iv.Num("port", 0);
                if (core.Port > 0 && pagePort == core.Port)
                {
                    core.PortHandoff = "skip(page already " + pagePort + ")";
                    Log.W("handoff skip page port already " + pagePort);
                    return;
                }
                string pj = Paths.ReadText(Paths.Runtime("port.json"));
                if (string.IsNullOrEmpty(pj))
                    pj = "{\"port\":" + core.Port + ",\"pid\":" + Core.ProcessId() + ",\"backend\":\"" + core.Backend + "\"}";
                Evaluate("window.__NCM_MINI_PORT_FILE__ = " + JsonWriter.Encode(pj), 2000);
                // connect() returns a Promise: the expression must hand the promise back to CDP
                // (awaitPromise:true resolves it). Wrapping it in JSON.stringify() here would
                // return a plain string and lose the resolution.
                string r = Evaluate("(function(){try{var a=window.__NCM_MINI__;if(!a||typeof a.connect!=='function')return JSON.stringify({ok:false,err:'no connect()'});var v=a.connect();if(v&&typeof v.then==='function')return v.then(function(x){return JSON.stringify(x);},function(e){return JSON.stringify({ok:false,err:'rej '+String((e&&e.message)||e)});});return JSON.stringify(v);}catch(e){return JSON.stringify({ok:false,err:String((e&&e.message)||e)})}})()", 5000);
                core.PortHandoff = "page " + pagePort + " -> " + core.Port + " :: " + Core.Trunc(r == null ? "null" : r, 140);
                Log.W("handoff " + core.PortHandoff + " (file=" + Core.Trunc(pj, 120) + ")");
            }
            catch (Exception e) { core.PortHandoff = "err " + e.Message; Log.W("handoff err " + e.Message); }
        }
        // D7: read window.__NCM_MINI__.build over CDP (throttled to every 5s), else stay null.
        private void ProbeBridgeBuild()
        {
            long now = DateTime.UtcNow.Ticks;
            if (nextBuildProbe != 0 && now < nextBuildProbe) return;
            nextBuildProbe = now + TimeSpan.TicksPerSecond * 5;
            try
            {
                string v = Evaluate(BuildExpr, 1500);
                core.LastBridgeProbe = v == null ? "null" : Core.Trunc(v, 80);
                if (!string.IsNullOrEmpty(v) && v != "null") core.BridgeBuild = v;
            }
            catch (Exception e) { core.LastBridgeProbe = "err " + e.Message; }
        }

        private string Discover()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/json");
                req.Timeout = 2000;
                req.ReadWriteTimeout = 2000;
                req.UserAgent = "MiniPlayer/1.0";
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    string txt = sr.ReadToEnd();
                    JValue v;
                    if (!JsonParser.TryParse(txt, out v)) { LastError = "bad /json"; return null; }
                    foreach (JValue t in v.AsArr())
                    {
                        if (t == null) continue;
                        if (t.Str("type") != "page") continue;
                        string wsu = t.Str("webSocketDebuggerUrl");
                        if (string.IsNullOrEmpty(wsu)) continue;
                        PageUrl = t.Str("url") ?? "";
                        return wsu;
                    }
                    LastError = "no page target";
                }
            }
            catch (Exception e) { LastError = "discover " + e.GetType().Name + ": " + e.Message; }
            return null;
        }

        private void Connect(string wsUrl)
        {
            try
            {
                CloseWs();
                cts = new CancellationTokenSource();
                ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                if (!ws.ConnectAsync(new Uri(wsUrl), cts.Token).Wait(4000)) { LastError = "connect timeout"; CloseWs(); return; }
                Connected = true;
                LastError = "";
                Log.W("cdp connected " + wsUrl + " page=" + PageUrl);
                int en = nextId++;
                lock (gate)
                {
                    SendText("{\"id\":" + en + ",\"method\":\"Runtime.enable\"}");
                    DrainUntil(en, 1500);
                }
            }
            catch (Exception e) { LastError = "connect " + e.Message; Connected = false; CloseWs(); }
        }

        private void CloseWs()
        {
            Connected = false;
            try { if (ws != null) ws.Dispose(); } catch { }
            ws = null;
            try { if (cts != null) cts.Dispose(); } catch { }
            cts = null;
        }

        // Synchronous evaluate; returns the JSON-ish value text or null.
        public string Evaluate(string expr, int timeoutMs)
        {
            // The poll loop keeps the original semantics: a drain that never answers means the socket is
            // suspect, so it is dropped and the loop reconnects (that is the existing self-heal path).
            return EvaluateCore(expr, timeoutMs, timeoutMs + 3000, true);
        }

        // L1 (latency brief): the command path's evaluate. Two differences, both deliberate:
        //   * the drain budget is the caller's budget (+120ms slack) instead of timeout+3000ms, so one slow
        //     command can never park an HTTP worker - or the UI thread behind a real click - for 3.3s;
        //   * a drain TIMEOUT does not tear the socket down. Missing one 300ms command window must not cost
        //     the whole CDP channel: re-discover + reconnect + bridge re-inject is seconds of outage, and
        //     noticing a genuinely dead socket stays the poll loop's job.
        public string EvaluateFast(string expr, int budgetMs)
        {
            return EvaluateCore(expr, budgetMs, budgetMs + 120, false);
        }

        private string EvaluateCore(string expr, int timeoutMs, int drainMs, bool dropOnTimeout)
        {
            if (!Connected) return null;
            lock (gate)
            {
                if (!Connected || ws == null) return null;
                try
                {
                    int id = nextId++;
                    string msg = "{\"id\":" + id + ",\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":" +
                        JsonWriter.Encode(expr) + ",\"returnByValue\":true,\"awaitPromise\":true,\"timeout\":" + timeoutMs + ",\"userGesture\":true}}";
                    SendText(msg);
                    JValue res = DrainUntil(id, drainMs, dropOnTimeout);
                    if (res == null) return null;
                    JValue result = res.Get("result");
                    if (result == null) return null;
                    if (result.Get("exceptionDetails") != null)
                    {
                        JValue ed = result.Get("exceptionDetails");
                        LastError = "page exception: " + Core.Trunc(ed == null ? "" : (ed.Str("text") ?? ""), 120);
                        return null;
                    }
                    JValue rv = result.Get("result");
                    if (rv == null) return null;
                    string type = rv.Str("type") ?? "";
                    if (type == "undefined") return null;
                    // the payload lives in rv.value; rv itself is an object node, so rv.S is always null
                    JValue v = rv.Get("value");
                    if (v == null || v.Kind == JKind.Null) return null;
                    if (v.Kind == JKind.Str) return v.S;
                    if (type == "string") return null;
                    return Json.Serialize(v);
                }
                catch (Exception e)
                {
                    LastError = "eval " + e.GetType().Name + ": " + e.Message;
                    Connected = false;
                    CloseWs();
                    return null;
                }
            }
        }

        public string Send(Command c)
        {
            string expr = c.ToCdpExpression();
            return Evaluate(expr, 2500);
        }

        private void SendText(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ArraySegment<byte> seg = new ArraySegment<byte>(bytes);
            TaskProxy.Send(ws, seg, cts == null ? CancellationToken.None : cts.Token, 3000);
        }

        // A receive TIMEOUT is not the same fact as a dead socket, and the two callers need different
        // verdicts: the poll loop drops the connection (reconnect + re-inject is its self-heal), the fast
        // command path just reports the miss and keeps the channel.
        private bool recvTimedOut;

        private JValue DrainUntil(int wantId, int ms) { return DrainUntil(wantId, ms, true); }

        private JValue DrainUntil(int wantId, int ms, bool dropOnTimeout)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline)
            {
                recvTimedOut = false;
                string txt = ReceiveOne((int)Math.Max(50, (deadline - DateTime.UtcNow).TotalMilliseconds));
                if (txt == null)
                {
                    if (recvTimedOut && !dropOnTimeout) return null;
                    Connected = false;
                    return null;
                }
                JValue v;
                if (!JsonParser.TryParse(txt, out v)) continue;
                if (v.Num("id", -1) == wantId) return v;
            }
            return null;
        }

        private string ReceiveOne(int ms)
        {
            byte[] buf = new byte[262144];
            StringBuilder sb = new StringBuilder();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) { if (sb.Length == 0) recvTimedOut = true; return sb.Length > 0 ? sb.ToString() : null; }
                ArraySegment<byte> seg = new ArraySegment<byte>(buf);
                WebSocketReceiveResult res;
                try
                {
                    Task<WebSocketReceiveResult> t = ws.ReceiveAsync(seg, cts == null ? CancellationToken.None : cts.Token);
                    if (!t.Wait(left)) { if (sb.Length == 0) recvTimedOut = true; return sb.Length > 0 ? sb.ToString() : null; }
                    res = t.Result;
                }
                catch (Exception) { return null; }
                if (res.MessageType == WebSocketMessageType.Close) return null;
                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                if (res.EndOfMessage) return sb.ToString();
            }
        }
    }

    internal static class TaskProxy
    {
        public static void Send(ClientWebSocket ws, ArraySegment<byte> seg, CancellationToken ct, int ms)
        {
            Task t = ws.SendAsync(seg, WebSocketMessageType.Text, true, ct);
            if (!t.Wait(ms)) throw new TimeoutException("ws send");
            if (t.IsFaulted && t.Exception != null) throw t.Exception.GetBaseException();
        }
    }
}
