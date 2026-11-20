using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mini
{
    public sealed class BarFlags
    {
        public bool Prev = true, Play = true, Next = true, Like = true, Loop = true, Queue = true, Vol = true, Seek = true;
        public void Load(JValue o)
        {
            if (o == null) return;
            Prev = o.Bool("prev", Prev);
            Play = o.Bool("play", Play);
            Next = o.Bool("next", Next);
            Like = o.Bool("like", Like);
            Loop = o.Bool("loop", Loop);
            Queue = o.Bool("queue", Queue);
            Vol = o.Bool("vol", Vol);
            Seek = o.Bool("seek", Seek);
        }
    }

    public sealed class Lyric
    {
        public int Index = -1;
        public string Line = "";
        public string Next = "";
        public List<string> Lines = new List<string>();
        public void Load(JValue o)
        {
            if (o == null) return;
            Index = (int)o.Num("index", Index);
            Line = o.Str("line") ?? "";
            Next = o.Str("next") ?? "";
            JValue a = o.ArrOf("lines");
            if (a != null) { Lines.Clear(); foreach (JValue v in a.AsArr()) Lines.Add(v.AsStr("")); }
        }
    }

    public sealed class QueueRow
    {
        public string Title = "";
        public string Artist = "";
        public bool Current;
        public int Index = -1;          // contract v1.2 #1: 0-based, same base as playIndex arg
        public string Id = null;        // netease songId, may be null
        public void Load(JValue o, int fallbackIndex)
        {
            Title = o.Str("title") ?? "";
            Artist = o.Str("artist") ?? "";
            Current = o.Bool("current", false);
            Index = (int)o.Num("index", o.Get("index") == null ? fallbackIndex : -1);
            Id = o.Str("id");
            if (string.IsNullOrEmpty(Id) && o.Get("id") != null) Id = Command.ArgJson(o.Get("id"));
        }
    }

    public sealed class State
    {
        public int V = 1;
        public string Src = "";
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public string Cover = null;
        public bool Playing;
        public double Pos;
        public double Dur;
        public string TimeText = "";
        public bool Liked;
        public string Loop = "off";
        public double Vol = -1;
        public Lyric Lyrics = new Lyric();
        public List<QueueRow> Queue = new List<QueueRow>();
        public BarFlags Bar = new BarFlags();
        public bool Ok = true;
        public string Err = null;

        public string Raw = "";
        public string Source = "";     // "http" | "cdp"
        public long ReceivedTicks = 0;

        public static State FromJson(string text)
        {
            JValue root;
            if (!JsonParser.TryParse(text, out root) || root == null || root.Kind != JKind.Obj) return null;
            State s = new State();
            s.Raw = text;
            s.V = (int)root.Num("v", 1);
            s.Src = root.Str("src") ?? "";
            s.Title = root.Str("title") ?? "";
            s.Artist = root.Str("artist") ?? "";
            s.Album = root.Str("album") ?? "";
            s.Cover = root.Str("cover");
            s.Playing = root.Bool("playing", false);
            s.Pos = root.Num("pos", 0);
            s.Dur = root.Num("dur", 0);
            s.TimeText = root.Str("timeText") ?? "";
            s.Liked = root.Bool("liked", false);
            s.Loop = root.Str("loop") ?? "off";
            s.Vol = root.Num("vol", -1);
            s.Lyrics.Load(root.ObjOf("lyric"));
            s.Bar.Load(root.ObjOf("bar"));
            s.Ok = root.Bool("ok", true);
            s.Err = root.Str("err");
            JValue q = root.ArrOf("queue");
            if (q != null)
            {
                int i = 0;
                foreach (JValue r in q.AsArr())
                {
                    if (r != null && r.Kind == JKind.Obj) { QueueRow row = new QueueRow(); row.Load(r, i); s.Queue.Add(row); }
                    i++;
                }
            }
            return s;
        }

        // Content fingerprint (ignores nothing but the payload itself).
        public string Fingerprint()
        {
            StringBuilder b = new StringBuilder();
            b.Append(V).Append('|').Append(Src).Append('|').Append(Title).Append('|').Append(Artist)
             .Append('|').Append(Album).Append('|').Append(Cover).Append('|').Append(Playing)
             .Append('|').Append(Pos.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
             .Append(Dur.ToString("0.00", CultureInfo.InvariantCulture)).Append('|').Append(TimeText)
             .Append('|').Append(Liked).Append('|').Append(Loop).Append('|').Append(Vol)
             .Append('|').Append(Lyrics.Index).Append('|').Append(Lyrics.Line).Append('|').Append(Lyrics.Next)
             .Append('|').Append(Queue.Count);
            for (int i = 0; i < Queue.Count && i < 40; i++) b.Append('/').Append(Queue[i].Index).Append(':').Append(Queue[i].Title).Append(':').Append(Queue[i].Id);
            b.Append('|').Append(Ok).Append('|').Append(Err);
            return b.ToString();
        }

        public string TimeDisplay()
        {
            if (!string.IsNullOrEmpty(TimeText)) return TimeText;
            return Fmt(Pos) + " / " + Fmt(Dur);
        }

        public static string Fmt(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            int total = (int)Math.Round(seconds);
            int m = total / 60, s = total % 60;
            if (m >= 60) { int h = m / 60; m = m % 60; return h + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture); }
            return m + ":" + s.ToString("00", CultureInfo.InvariantCulture);
        }

        public double Progress01()
        {
            if (Dur <= 0.001) return 0;
            double r = Pos / Dur;
            if (r < 0) r = 0; if (r > 1) r = 1;
            return r;
        }
    }

    public sealed class Command
    {
        public string Name;
        public JValue Arg;          // may be null => JSON null
        public long Id;
        private static long seq = 0;

        // Per-process token for the page-side CDP guard. The PAGE survives an overlay restart (a reload is
        // out of scope), so window.__NCM_MINI_CDPSENT__ still remembers every id the PREVIOUS instance used -
        // and the id sequence deliberately restarts at 1 in every new instance (verify-all.ps1:189 documents
        // that as a fact and reads enqueue ids with byte marks, so the sequence must NOT be pid-seeded).
        // Measured failure without the epoch: after the 21:20 restart, /cmd next id=1..3 answered 'dup' in
        // 0ms, nothing was executed, the title never changed, and the stale ack of the old instance (id=75)
        // was still sitting in the page (server.log 21:21:28-21:21:37). Epoch re-scopes the guard per start.
        public static string Epoch = "0";

        static Command()
        {
            long pid = 0;
            try { pid = System.Diagnostics.Process.GetCurrentProcess().Id; } catch { }
            Epoch = pid.ToString(CultureInfo.InvariantCulture) + "-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        public Command(string name, JValue arg)
        {
            Name = name;
            Arg = arg;
            Id = System.Threading.Interlocked.Increment(ref seq);
        }

        public static JValue NumArg(double d) { JValue v = new JValue(); v.Kind = JKind.Num; v.N = d; return v; }
        public static JValue BoolArg(bool b) { JValue v = new JValue(); v.Kind = JKind.Bool; v.B = b; return v; }
        public static JValue StrArg(string s) { JValue v = new JValue(); v.Kind = JKind.Str; v.S = s; return v; }

        public string ToCdpExpression()
        {
            string arg = Arg == null ? "null" : ArgJson(Arg);
            return "window.__NCM_MINI__.cmd(" + JsonWriter.Encode(Name) + "," + arg + ")";
        }

        // L1 (latency brief): the IMMEDIATE CDP channel. This expression fires the command and returns a
        // short verdict string right away instead of handing the promise to CDP, so the caller (an HTTP
        // worker, or the UI thread behind a real click) is blocked for one websocket round trip and never
        // for the bridge's own settle budget (next/prev = 900ms, toggle = 700ms).
        // Two page-side guards, both written by us, both keyed on OUR command id:
        //   window.__NCM_MINI_CDPSENT__[id] -> the same id can never be executed twice over CDP;
        //   window.__NCM_MINI_CDPACK__      -> the bridge's own settle verdict, read back as evidence.
        // Note on the bridge's dedupe: API.cmd() does NOT go through mini.js handleCommand, so a
        // CDP-delivered command never touches http.lastId and can never be swallowed as a duplicate of the
        // command the previous POST response carried. Core.IssueCommand picks exactly one carrier per id,
        // so the same command is never on both paths either.
        public string ToCdpFireExpression()
        {
            string arg = Arg == null ? "null" : ArgJson(Arg);
            string id = Id.ToString(CultureInfo.InvariantCulture);
            return "(function(){try{var a=window.__NCM_MINI__;"
                 + "if(!a||typeof a.cmd!=='function')return 'no-bridge';"
                 + "var E=" + JsonWriter.Encode(Epoch) + ";var G=window.__NCM_MINI_CDPSENT__;"
                 + "if(!G||G.e!==E){G={e:E,n:0};try{window.__NCM_MINI_CDPSENT__=G}catch(e0){}}"   // a restarted overlay is a new id space
                 + "var ID=" + id + ";if(G[ID])return 'dup';G[ID]=1;G.n++;"
                 + "if(G.n>256){try{for(var k in G){if(k!=='n'&&+k<ID-64)delete G[k]}G.n=64}catch(e1){}}"
                 + "var p=a.cmd(" + JsonWriter.Encode(Name) + "," + arg + ");"
                 + "if(p&&p.then)p.then(function(r){try{window.__NCM_MINI_CDPACK__={id:ID,ok:!!(r&&r.ok),err:(r&&r.err)||null,via:(r&&r.via)||null,settledMs:(r&&r.settledMs===undefined?null:r.settledMs),t:Date.now()}}catch(e2){}},"
                 + "function(e){try{window.__NCM_MINI_CDPACK__={id:ID,ok:false,err:'rej '+String((e&&e.message)||e),t:Date.now()}}catch(e3){}});"
                 + "return 'sent';}catch(e){return 'err '+String((e&&e.message)||e)}})()";
        }

        public static string ArgJson(JValue a)
        {
            if (a == null) return "null";
            switch (a.Kind)
            {
                case JKind.Bool: return a.B ? "true" : "false";
                case JKind.Num: return a.N.ToString("0.######", CultureInfo.InvariantCulture);
                case JKind.Str: return JsonWriter.Encode(a.S);
                case JKind.Null: return "null";
                default: return "null";
            }
        }

        public string ToWire()
        {
            StringBuilder b = new StringBuilder();
            b.Append('{');
            b.Append(JsonWriter.Encode("cmd")).Append(':').Append(JsonWriter.Encode(Name)).Append(',');
            b.Append(JsonWriter.Encode("arg")).Append(':').Append(ArgJson(Arg)).Append(',');
            b.Append(JsonWriter.Encode("id")).Append(':').Append(Id.ToString(CultureInfo.InvariantCulture));
            b.Append('}');
            return b.ToString();
        }
    }

    // Holds latest state + pending commands; the single place where the two
    // channels are merged (contract: first channel with data wins, dedupe).
    public sealed class Store
    {
        private readonly object gate = new object();
        private State current;
        private string lastFingerprint = "";
        private string preferredSource = "";
        private long lastApplyTicks = 0;
        private long lastIngestTicks = 0;
        private readonly List<Command> pending = new List<Command>();
        public event Action<State> Changed;
        public long LastHttpRequestTicks = 0;
        public int AppliedCount = 0;           // frames that CHANGED the state (fingerprint differs)
        public int FrameCount = 0;             // every well-formed frame, changed or not
        public string LastApplyReason = "";    // changed | same-fingerprint | channel-* | parse-fail

        // Returns true when the UI should refresh. The rejection reason is recorded separately so the
        // log never claims "applied=False" for a healthy stream of identical (paused) frames.
        public bool Apply(string json, string source) { return Apply(json, source, false); }

        // L1 (latency brief): authoritative=true is used ONLY by the CDP read burst that follows a command
        // this process just delivered over CDP. Both channels read the SAME page, so the fingerprint below
        // still decides whether anything changed; the flag only stops the "the other channel is preferred"
        // gate from discarding the freshest frame in the pipeline - the one that carries the command's
        // effect. That discard is what made a paused-state command observable up to one full POST cycle
        // (1500ms) after the page had already changed.
        public bool Apply(string json, string source, bool authoritative)
        {
            State s = State.FromJson(json);
            if (s == null) { LastApplyReason = "parse-fail"; return false; }
            s.Source = source;
            s.ReceivedTicks = DateTime.UtcNow.Ticks;
            System.Threading.Interlocked.Exchange(ref lastIngestTicks, s.ReceivedTicks);
            System.Threading.Interlocked.Increment(ref FrameCount);
            string fp = s.Fingerprint();
            lock (gate)
            {
                bool sameChannelPreferred = preferredSource == "" || preferredSource == source;
                bool otherStale = (DateTime.UtcNow.Ticks - lastApplyTicks) > TimeSpan.TicksPerMillisecond * 3000;
                if (fp == lastFingerprint) { s.Source = preferredSource; current = s; LastApplyReason = "same-fingerprint"; return false; }
                if (!authoritative && !sameChannelPreferred && !otherStale) { LastApplyReason = "channel-" + preferredSource + "-wins"; return false; }
                current = s;
                lastFingerprint = fp;
                // L1: an authoritative frame (the read burst behind a command we just fired over CDP) must
                // land, but it must NOT steal the channel preference - otherwise every command would flip the
                // pipeline to the 500/1500ms CDP cadence and throw away the bridge's 250ms POST frames.
                if (!authoritative || preferredSource == "") preferredSource = source;
                lastApplyTicks = s.ReceivedTicks;
                AppliedCount++;
                LastApplyReason = authoritative ? "changed-authoritative" : "changed";
            }
            Action<State> h = Changed;
            if (h != null) h(s);
            return true;
        }

        public State Snapshot() { lock (gate) { return current; } }
        public string RawJson() { lock (gate) { return current == null ? "{}" : current.Raw; } }

        public void Enqueue(Command c)
        {
            lock (gate)
            {
                pending.Add(c);
                while (pending.Count > 8) pending.RemoveAt(0);
            }
        }

        public Command Take()
        {
            lock (gate)
            {
                if (pending.Count == 0) return null;
                Command c = pending[0];
                pending.RemoveAt(0);
                return c;
            }
        }

        // L1: put a command back at the HEAD, with its ORIGINAL id, when its CDP retry failed - the HTTP
        // response channel is still its carrier, nothing is dropped and nothing is sent twice. Keeping the
        // id also keeps the bridge's single-slot dedupe (mini.js handleCommand -> http.lastId) meaningful:
        // both carriers draw ids from this one sequence.
        public void Requeue(Command c)
        {
            if (c == null) return;
            lock (gate)
            {
                pending.Insert(0, c);
                while (pending.Count > 8) pending.RemoveAt(pending.Count - 1);
            }
        }

        public int PendingCount { get { lock (gate) { return pending.Count; } } }
        public long AgeSinceLastApplyMs()
        {
            lock (gate) { return lastApplyTicks == 0 ? long.MaxValue : (DateTime.UtcNow.Ticks - lastApplyTicks) / TimeSpan.TicksPerMillisecond; }
        }
        // pipeline age = since the last well-formed frame, whether or not it changed anything
        public long AgeSinceLastIngestMs()
        {
            long t = System.Threading.Interlocked.Read(ref lastIngestTicks);
            return t == 0 ? long.MaxValue : (DateTime.UtcNow.Ticks - t) / TimeSpan.TicksPerMillisecond;
        }
        public string PreferredSource { get { lock (gate) { return preferredSource; } } }
    }
}
