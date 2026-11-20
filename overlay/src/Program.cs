using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace Mini
{
    public static class Program
    {
        public static readonly string NcmPath = System.IO.Path.Combine("D:", "CloudMusic", "cloudmusic.exe");
        public static readonly string LockName = "Local" + (char)92 + "ncmmini_miniplayer_lock";
        public static string LockFile { get { return Paths.Runtime("MiniPlayer.lock"); } }

        [STAThread]
        public static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception e) { return Crash(e); }
        }

        private static int Crash(Exception e)
        {
            try { Log.W("FATAL " + e.GetType().Name + ": " + e.Message + Environment.NewLine + e.StackTrace); } catch { }
            Console.WriteLine("FATAL " + e.Message);
            return 2;
        }

        private static int Run(string[] args)
        {
            try { Win.SetProcessDPIAware(); } catch { }
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)4080 | (SecurityProtocolType)3072 | (SecurityProtocolType)192;
                ServicePointManager.DefaultConnectionLimit = 16;
            }
            catch { }
            List<string> argv = new List<string>();
            if (args != null) foreach (string a in args) argv.Add(a ?? "");

            string first = argv.Count > 0 ? argv[0].ToLowerInvariant() : "";

            if (first == "--register") { Console.WriteLine(Protocol.Register()); Console.WriteLine(Protocol.Dump()); return 0; }
            if (first == "--unregister") { Console.WriteLine(Protocol.Unregister()); return 0; }
            if (first == "--dump-protocol") { Console.WriteLine(Protocol.Dump()); return 0; }
            if (first == "--help" || first == "-h" || first == "/?") { Help(); return 0; }
            if (first == "--version") { Console.WriteLine("MiniPlayer 1.0 protocol-v1"); return 0; }
            if (first == "--ports") { Console.WriteLine(Paths.ReadText(Paths.Runtime("port.json")) ?? "no port.json"); return 0; }
            // D3: the watchdog runs as its own process and returns before the mutex/WPF start-up, so it can
            // exist next to a live mini player and never touches the window itself.
            if (first == "--watch") return Watch(argv);
            if (first == "--unwatch") return Unwatch();

            // control verbs handled by the running instance
            if (first == "--hide" || first == "--show" || first == "--toggle" || first == "--top" ||
                first == "--theme" || first == "--queue" || first == "--taskbar" || first == "--quit" ||
                first == "--info" || first == "--state" || first == "--rect")
            {
                // Robustness trap fixed: "MiniPlayer.exe --hide" used to be swallowed as a client verb even
                // when NOTHING was running, so the documented cold start left no process behind (and a bare
                // "MiniPlayer.exe" cold start always shows the window). With no live instance, --hide now
                // falls through and starts a hidden server; every other verb keeps the old client behaviour.
                if (first == "--hide" && LivePort() == 0 && !argv.Contains("--nohttp"))
                {
                    Log.W("cold start hidden (--hide with no live instance)");
                }
                else
                {
                    string verb = first.Substring(2);
                    if (verb == "taskbar" && argv.Count > 1) verb = "taskbar:" + argv[1].ToLowerInvariant();
                    return Remote(verb);
                }
            }
            if (first == "--uri") return Uri(argv.Count > 1 ? argv[1] : "");
            if (first.StartsWith("ncmmini://", StringComparison.OrdinalIgnoreCase)) return Uri(first);
            if (first == "--click" && argv.Count > 1) return RemotePost("/click", "{\"which\":" + JsonWriter.Encode(argv[1]) + "}");
            if (first == "--cover" && argv.Count > 1) return Remote("cover:" + argv[1].ToLowerInvariant());
            if (first == "--play" && argv.Count > 1)
            {
                int pi;
                string argJson = int.TryParse(argv[1], out pi) ? pi.ToString(CultureInfo.InvariantCulture) : JsonWriter.Encode(argv[1]);
                return RemotePost("/cmd", "{\"cmd\":\"playIndex\",\"arg\":" + argJson + "}");
            }
            if (first == "--cmd" && argv.Count > 1) return RemotePost("/cmd", "{\"cmd\":" + JsonWriter.Encode(argv[1]) + ",\"arg\":null}");

            bool launchNcm = argv.Contains("--launch-ncm");
            bool startHidden = argv.Contains("--hide");
            bool noCdp = argv.Contains("--nocdp");
            bool noHttp = argv.Contains("--nohttp");
            int portBase = 46210;
            for (int i = 0; i + 1 < argv.Count; i++)
                if (argv[i] == "--port" && int.TryParse(argv[i + 1], out portBase)) { }
            string fixture = null;
            for (int i = 0; i + 1 < argv.Count; i++) if (argv[i] == "--fixture") fixture = argv[i + 1];
            for (int i = 0; i + 1 < argv.Count; i++) if (argv[i] == "--cover") { try { Config c0 = Config.Load(); c0.CoverMode = argv[i + 1].ToLowerInvariant(); c0.Save(); } catch { } }

            if (launchNcm) EnsureNcm();

            bool createdNew;
            Mutex single = new Mutex(false, LockName, out createdNew);
            bool mine = false;
            try { mine = single.WaitOne(TimeSpan.FromMilliseconds(400), false); }
            catch { mine = true; }
            if (!mine)
            {
                Console.WriteLine("already running (mutex held) - use --toggle/--quit");
                return 0;
            }

            Core core = new Core();
            core.Cfg.HttpPortBase = portBase;
            Ui ui = new Ui(core);

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ui.Build();                     // window must exist before channels can push state
            ui.Attach();
            core.HotspotsJson = delegate { return ui.HotspotsText(); };
            core.DebugProbe = delegate (string verb) { return ui.DebugProbe(verb); };
            core.UiAction = delegate (string verb)
            {
                app.Dispatcher.BeginInvoke(new Action(delegate
                {
                    ui.UiAction(verb);
                    if (verb == "quit") app.Shutdown();
                }));
            };

            if (!noHttp)
            {
                core.Http = new HttpServer(core);
                if (!core.Http.Start(portBase)) Log.W("http start FAILED: " + core.Http.FailReason);
            }
            else
            {
                // D6: --nohttp still advertises itself so `--quit`/verbs can find the pid.
                core.Backend = "none";
                core.Port = 0;
                WritePortFile(core, 0, "none", "nohttp");
            }

            if (!noCdp && core.Cfg.Cdp)
            {
                core.Cdp = new CdpClient(core);
                core.Cdp.Start();
            }

            WriteLock(core);
            if (core.Cfg.QueueOpen) ui.RestoreQueue();
            if (!startHidden) ui.Show();
            if (fixture != null) LoadFixture(core, fixture);
            ui.Refresh();
            app.DispatcherUnhandledException += delegate (object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                Log.W("UI exception: " + e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            { Log.W("domain exception: " + e.ExceptionObject); };

            app.Run();
            Cleanup(core);
            Log.W("dispatcher stopped, exiting pid=" + Core.ProcessId());
            try { if (mine) single.ReleaseMutex(); } catch { }
            try { single.Dispose(); } catch { }
            return 0;
        }

        private static void LoadFixture(Core core, string path)
        {
            try
            {
                string txt = File.ReadAllText(path);
                core.Store.Apply(txt, "fixture");
                Log.W("fixture loaded " + path);
            }
            catch (Exception e2) { Log.W("fixture " + e2.Message); }
        }

        private static void WritePortFile(Core core, int port, string backend, string reason)
        {
            Paths.WriteText(Paths.Runtime("port.json"),
                new JsonWriter().Begin()
                    .Num("port", port).Num("pid", Core.ProcessId())
                    .Str("backend", backend)
                    .Str("prefix", port > 0 ? "http://127.0.0.1:" + port + "/" : "")
                    .B("urlaclNeeded", false).Str("reason", reason)
                    .Str("at", DateTime.Now.ToString("s", CultureInfo.InvariantCulture)).End().ToString());
            Log.W("port.json written port=" + port + " backend=" + backend);
        }

        private static void WriteLock(Core core)
        {
            try
            {
                File.WriteAllText(LockFile, new JsonWriter().Begin()
                    .Num("pid", Core.ProcessId()).Num("port", core.Port)
                    .Str("backend", core.Backend).Str("start", DateTime.Now.ToString("s", CultureInfo.InvariantCulture))
                    .End().ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static void Cleanup(Core core)
        {
            try { if (core.Cdp != null) core.Cdp.Stop(); } catch { }
            try { if (core.Http != null) core.Http.Stop(); } catch { }
            try { if (File.Exists(LockFile)) File.Delete(LockFile); } catch { }
            try
            {
                string pj = Paths.Runtime("port.json");
                if (File.Exists(pj))
                {
                    JValue v;
                    if (JsonParser.TryParse(File.ReadAllText(pj), out v) && v != null && (int)v.Num("pid", -1) == (int)Core.ProcessId())
                        File.Delete(pj);
                }
            }
            catch { }
            Log.W("stopped pid=" + Core.ProcessId());
            Log.Close();                       // #7: the log is buffered now, so the tail must be pushed out
        }

        private static int Uri(string raw)
        {
            Console.WriteLine("uri " + raw);
            try
            {
                Uri u = new Uri(raw, UriKind.Absolute);
                string verb = u.Host;
                string path = u.AbsolutePath.Trim(new char[] { (char)47 });
                if (!string.IsNullOrEmpty(path)) verb = path;
                if (string.IsNullOrEmpty(verb)) verb = "toggle";
                return Remote(verb);
            }
            catch (Exception e) { Console.WriteLine("bad uri: " + e.Message); return 1; }
        }

        private static int Remote(string verb)
        {
            int port = LivePort();
            if (port < 0)
            {
                Console.WriteLine("MiniPlayer pid=" + LastNoServicePid + " is running but backend=" + LastNoServiceBackend + " (no HTTP service) - verb '" + verb + "' unavailable; use 'taskkill /PID " + LastNoServicePid + "' or restart without --nohttp");
                return 2;
            }
            if (port == 0)
            {
                if (verb == "toggle" || verb == "show")
                {
                    Console.WriteLine("not running - starting one");
                    try { Process.Start(Paths.Exe, ""); } catch (Exception e) { Console.WriteLine(e.Message); return 0; }
                    return 0;
                }
                Console.WriteLine("no running MiniPlayer (port.json / pid check failed) - verb '" + verb + "' needs a live instance");
                return 1;
            }
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/ctl/" + verb);
                rq.Timeout = 2500;
                rq.Method = "GET";
                using (HttpWebResponse res = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    Console.WriteLine((int)res.StatusCode + " " + sr.ReadToEnd());
                    return 0;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("remote " + verb + " failed: " + e.GetType().Name + " " + e.Message);
                return 1;
            }
        }

        private static int RemotePost(string path, string body)
        {
            int port = LivePort();
            if (port < 0) { Console.WriteLine("MiniPlayer pid=" + LastNoServicePid + " running with backend=" + LastNoServiceBackend + " (no HTTP service)"); return 2; }
            if (port <= 0) { Console.WriteLine("no running MiniPlayer"); return 1; }
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + path);
                rq.Timeout = 3000;
                rq.Method = "POST";
                byte[] b = Encoding.UTF8.GetBytes(body);
                rq.ContentLength = b.Length;
                rq.ContentType = "application/json";
                using (Stream s = rq.GetRequestStream()) s.Write(b, 0, b.Length);
                using (HttpWebResponse res = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    Console.WriteLine((int)res.StatusCode + " " + sr.ReadToEnd());
                    return 0;
                }
            }
            catch (Exception e) { Console.WriteLine("post failed: " + e.Message); return 1; }
        }

        // >0 = usable port, 0 = no instance at all, -1 = instance alive but with no HTTP service
        public static int LivePort()
        {
            string txt = Paths.ReadText(Paths.Runtime("port.json"));
            if (txt == null) return 0;
            JValue v;
            if (!JsonParser.TryParse(txt, out v) || v == null) return 0;
            int port = (int)v.Num("port", 0);
            int pid = (int)v.Num("pid", 0);
            bool alive = false;
            try { using (Process p = Process.GetProcessById(pid)) { alive = !p.HasExited; } } catch { alive = false; }
            if (!alive) return 0;
            if (port <= 0) { LastNoServicePid = pid; LastNoServiceBackend = v.Str("backend") ?? "none"; return -1; }
            return port;
        }

        public static int LastNoServicePid = 0;
        public static string LastNoServiceBackend = "none";

        public static bool NcmRunning()
        {
            foreach (Process p in Process.GetProcessesByName("cloudmusic")) { try { if (!p.HasExited) return true; } catch { } }
            return false;
        }

        private static int EnsureNcm()
        {
            if (NcmRunning())
            {
                Console.WriteLine("cloudmusic already running:");
                foreach (Process p in Process.GetProcessesByName("cloudmusic")) Console.WriteLine("  pid " + p.Id);
                return 0;
            }
            if (!File.Exists(NcmPath)) { Console.WriteLine("missing " + NcmPath); return 1; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(NcmPath, "--remote-debugging-port=9222");
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(NcmPath);
                psi.UseShellExecute = false;
                Process p = Process.Start(psi);
                Console.WriteLine("launched cloudmusic pid=" + p.Id);
                Thread.Sleep(2500);
                return 0;
            }
            catch (Exception e) { Console.WriteLine("launch failed: " + e.Message); return 1; }
        }

        // ================================================================ D3 watchdog
        // The card drives the netease client over the DevTools port only, so the two failure modes that
        // actually happen are (a) cloudmusic running without --remote-debugging-port=9222 (started by hand,
        // or restarted by an update) -> the CDP channel is dead and every command silently queues, and
        // (b) cloudmusic exiting -> the card keeps showing a frozen snapshot. --watch watches for both.
        //   --watch                     run the loop (foreground, own process)
        //   --watch --dry               decision-only: prints what it WOULD do, starts/stops/hides nothing
        //   --watch --once              single pass (what the static self-check uses)
        //   --interval-ms <n>           poll period, default 3000
        //   --watch-max-minutes <n>     self-exit, default 720
        //   --unwatch                   stop a watchdog started earlier (its pid is in runtime/watch.json)
        private static readonly string WatchFile = Paths.Runtime("watch.json");
        private static readonly string[] NcmFlags = new string[] {
            "--remote-debugging-port=9222",
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows" };

        private static int Watch(List<string> argv)
        {
            bool dry = argv.Contains("--dry");
            bool once = argv.Contains("--once");
            int intervalMs = 3000, maxMinutes = 720, n;
            for (int i = 0; i + 1 < argv.Count; i++)
            {
                if (argv[i] == "--interval-ms" && int.TryParse(argv[i + 1], out n) && n >= 500) intervalMs = n;
                if (argv[i] == "--watch-max-minutes" && int.TryParse(argv[i + 1], out n) && n >= 1) maxMinutes = n;
            }
            Console.WriteLine("watch start pid=" + Core.ProcessId() + " dry=" + dry + " once=" + once +
                              " intervalMs=" + intervalMs + " maxMinutes=" + maxMinutes);
            Log.W("watch start pid=" + Core.ProcessId() + " dry=" + dry + " once=" + once);
            if (!dry)
            {
                Paths.WriteText(WatchFile, new JsonWriter().Begin()
                    .Num("pid", Core.ProcessId()).Num("intervalMs", intervalMs).Num("maxMinutes", maxMinutes)
                    .B("dry", false)
                    .Str("flags", string.Join(" ", NcmFlags))
                    .Str("started", DateTime.Now.ToString("s", CultureInfo.InvariantCulture)).End().ToString());
            }
            long stopAt = DateTime.UtcNow.Ticks + (long)maxMinutes * 600000000L;
            bool ncmWasUp = true;            // first pass must not claim "cloudmusic exited" when it never ran
            int pass = 0;
            try
            {
                while (true)
                {
                    pass++;
                    WatchPass(dry, ref ncmWasUp, pass);
                    if (once) return 0;
                    if (DateTime.UtcNow.Ticks > stopAt) { Console.WriteLine("watch stop reason=max-minutes"); Log.W("watch stop max-minutes"); return 0; }
                    Thread.Sleep(intervalMs);
                }
            }
            finally { try { if (!dry && File.Exists(WatchFile)) File.Delete(WatchFile); } catch { } }
        }

        private static void WatchPass(bool dry, ref bool ncmWasUp, int pass)
        {
            Process[] ncm = SafeNcm();
            bool up = ncm.Length > 0;
            bool cdp = CdpAlive(9222);
            string line = "watch pass=" + pass + " cloudmusic=" + ncm.Length + " cdp9222=" + cdp;
            if (!up)
            {
                if (ncmWasUp)
                {
                    line += " decision=hide-mini reason=cloudmusic-exited";
                    if (dry) line += " [dry: /ctl/hide not sent]";
                    else Remote("hide");
                }
                else line += " decision=none reason=cloudmusic-never-up";
                ncmWasUp = false;
                Console.WriteLine(line); Log.W(line);
                return;
            }
            ncmWasUp = true;
            if (cdp) { line += " decision=none reason=channel-alive"; Console.WriteLine(line); Log.W(line); return; }
            line += " decision=restart-cloudmusic flags=" + string.Join(" ", NcmFlags);
            if (dry)
            {
                line += " [dry: no process started, no process closed]";
                Console.WriteLine(line); Log.W(line);
                return;
            }
            Console.WriteLine(line); Log.W(line);
            for (int i = 0; i < ncm.Length; i++)
            {
                try { ncm[i].CloseMainWindow(); }
                catch (Exception ce) { Console.WriteLine("watch close " + ncm[i].Id + " fail " + ce.Message); }
            }
            // Graceful only: if the client refuses to close, do not kill it out from under the user - retry next pass.
            // B6 fix (2026-11-10): the old code closed EVERY cloudmusic process but only waited for ncm[0],
            // so with a helper process still alive it launched a second instance next to the first.
            try
            {
                long dl = DateTime.UtcNow.Ticks + 80000000L;                 // 8s for the whole set
                for (int i = 0; i < ncm.Length; i++)
                {
                    int left;
                    try { if (ncm[i].HasExited) continue; } catch { continue; }
                    left = (int)Math.Max(0, (dl - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerMillisecond);
                    if (!ncm[i].WaitForExit(left))
                    {
                        Console.WriteLine("watch: cloudmusic pid=" + ncm[i].Id + " alive 8s after WM_CLOSE - not killing, retry next pass");
                        Log.W("watch graceful-close-timeout pid=" + ncm[i].Id);
                        return;
                    }
                }
            }
            catch (Exception we) { Console.WriteLine("watch wait fail " + we.Message); }
            if (!File.Exists(NcmPath)) { Console.WriteLine("watch: " + NcmPath + " not found - cannot restart"); return; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(NcmPath, string.Join(" ", NcmFlags));
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(NcmPath);
                psi.UseShellExecute = false;
                Process p = Process.Start(psi);
                Console.WriteLine("watch launched cloudmusic pid=" + p.Id);
                Log.W("watch launched cloudmusic pid=" + p.Id);
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(1000);
                    if (CdpAlive(9222)) { Console.WriteLine("watch cdp9222 back after " + (i + 1) + "s"); Log.W("watch cdp back " + (i + 1) + "s"); break; }
                }
            }
            catch (Exception e) { Console.WriteLine("watch launch failed: " + e.Message); Log.W("watch launch fail " + e.Message); }
        }

        private static Process[] SafeNcm()
        {
            try { return Process.GetProcessesByName("cloudmusic"); } catch { return new Process[0]; }
        }

        // The channel is only really up when the DevTools HTTP endpoint answers, not when the port is open.
        private static bool CdpAlive(int port)
        {
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json/version");
                rq.Timeout = 900;
                rq.ReadWriteTimeout = 900;
                rq.Method = "GET";
                using (HttpWebResponse res = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) { sr.ReadToEnd(); }
                return true;
            }
            catch { return false; }
        }

        private static int Unwatch()
        {
            string txt = Paths.ReadText(WatchFile);
            if (string.IsNullOrEmpty(txt)) { Console.WriteLine("no runtime/watch.json - watchdog is not running"); return 0; }
            JValue v;
            int pid = 0;
            if (JsonParser.TryParse(txt, out v) && v != null) pid = (int)v.Num("pid", 0);
            if (pid <= 0) { Console.WriteLine("watch.json unusable: " + txt); return 1; }
            try
            {
                Process p = Process.GetProcessById(pid);
                string exe = "";
                try { exe = p.MainModule.FileName; } catch { }
                if (!string.IsNullOrEmpty(exe) && !string.Equals(System.IO.Path.GetFileName(exe), "MiniPlayer.exe", StringComparison.OrdinalIgnoreCase))
                { Console.WriteLine("pid " + pid + " is " + exe + " - not a MiniPlayer watchdog; refusing to stop it"); return 1; }
                p.Kill();
                try { p.WaitForExit(3000); } catch { }
                Console.WriteLine("watchdog pid " + pid + " stopped");
            }
            catch (Exception e) { Console.WriteLine("watchdog pid " + pid + " already gone (" + e.GetType().Name + ")"); }
            try { if (File.Exists(WatchFile)) File.Delete(WatchFile); } catch { }
            Log.W("unwatch done");
            return 0;
        }

        private static void Help()
        {
            Console.WriteLine("MiniPlayer.exe [flags]");
            Console.WriteLine("  (none)             show window + start both channels");
            Console.WriteLine("  --hide / --show / --toggle      window visibility (talks to the running instance)");
            Console.WriteLine("  --top              toggle always-on-top");
            Console.WriteLine("  --theme            cycle dark / light / transparent / cover");
            Console.WriteLine("  --queue            toggle the queue panel");
            Console.WriteLine("  --taskbar on|off   show in / skip the taskbar");
            Console.WriteLine("  --quit             stop the running instance");
            Console.WriteLine("  --info / --state   read health / last state");
            Console.WriteLine("  --click <icon>     debug: fire one icon handler remotely");
            Console.WriteLine("  --cmd <name>       debug: enqueue one command");
            Console.WriteLine("  --register / --unregister / --dump-protocol    ncmmini:// handler in HKCU");
            Console.WriteLine("  --launch-ncm       start the netease client with --remote-debugging-port=9222 when it is not running");
            Console.WriteLine("  --cover <sleeve|card|line|auto>   force cover transform state");
            Console.WriteLine("  --play <index>          send playIndex (0-based queue row)");
            Console.WriteLine("  --rect                  re-dump winrect.json / hotspots.json");
            Console.WriteLine("  --nocdp / --nohttp / --port <base> / --fixture <file.json>   test switches");
            Console.WriteLine("  --watch [--dry] [--once] [--interval-ms n] [--watch-max-minutes n]");
            Console.WriteLine("                     D3 watchdog: restarts cloudmusic with " + string.Join(" ", NcmFlags));
            Console.WriteLine("                     when 9222 is dead, and hides the card when cloudmusic exits.");
            Console.WriteLine("                     --dry is decision-only: it starts, stops and hides nothing.");
            Console.WriteLine("  --unwatch          stop the watchdog (runtime/watch.json pid, MiniPlayer.exe only)");
        }
    }
}
