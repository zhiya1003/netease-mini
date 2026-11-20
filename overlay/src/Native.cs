using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Mini
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    public static class Win
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_LAYERED = 0x00080000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_APPWINDOW = 0x00040000L;

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetDeviceCaps(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern void ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int idx);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, int flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMon, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
        // M1a: the tray menu's "打开网易云" needs to bring the NCM main window forward (never click its controls).
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmdShow);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static long GetExStyle(IntPtr h)
        {
            try { return GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64(); }
            catch { return 0; }
        }

        public static void SetExStyle(IntPtr h, long style)
        {
            try { SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(style)); }
            catch { }
        }

        // N4: DPI of the monitor that owns a physical point. A window that has never been shown is
        // attributed by GetDpiForWindow to the primary monitor (96 here), and GetDpiForSystem follows
        // the process awareness context - neither is the 125% screen the card lives on.
        public static int DpiAt(int x, int y)
        {
            try
            {
                POINT p = new POINT(); p.X = x; p.Y = y;
                IntPtr mon = MonitorFromPoint(p, 2 /*MONITOR_DEFAULTTONEAREST*/);
                if (mon == IntPtr.Zero) return 0;
                uint dx = 0, dy = 0;
                if (GetDpiForMonitor(mon, 0 /*MDT_EFFECTIVE_DPI*/, out dx, out dy) == 0 && dx >= 96 && dx <= 480) return (int)dx;
                if (GetDpiForMonitor(mon, 1 /*MDT_RAW_DPI*/, out dx, out dy) == 0 && dx >= 96 && dx <= 480) return (int)dx;
            }
            catch { }
            return 0;
        }

        public static int DpiX()
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            try { int v = GetDeviceCaps(dc, 88 /*LOGPIXELSX*/); return v <= 0 ? 96 : v; }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }

        // Work area of the monitor containing the given physical point, in physical px.
        public static RECT WorkAreaAt(int x, int y)
        {
            POINT p = new POINT(); p.X = x; p.Y = y;
            IntPtr mon = MonitorFromPoint(p, 2 /*MONITOR_DEFAULTTONEAREST*/);
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(mon, ref mi)) return mi.rcWork;
            RECT r = new RECT();
            r.Left = 0; r.Top = 0;
            r.Right = GetSystemMetrics(78 /*SM_CXSCREEN*/);
            r.Bottom = GetSystemMetrics(79 /*SM_CYSCREEN*/);
            return r;
        }
    }

    public static class Paths
    {
        public static string Root;          // netease-mini root folder
        public static string OverlayDir;
        public static string RuntimeDir;
        public static string Exe;
        public static string CacheDir;

        static Paths()
        {
            Exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            try { Exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; } catch { }
            OverlayDir = AppDomain.CurrentDomain.BaseDirectory;
            Root = Path.GetFullPath(Path.Combine(OverlayDir, ".."));
            RuntimeDir = Path.Combine(Root, "runtime");
            CacheDir = Path.Combine(RuntimeDir, "cache");
            try { Directory.CreateDirectory(RuntimeDir); } catch { }
        }

        public static string Runtime(string name) { return Path.Combine(RuntimeDir, name); }
        public static string Overlay(string name) { return Path.Combine(OverlayDir, name); }

        public static void WriteText(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch { }
        }

        public static string ReadText(string path)
        {
            try { if (File.Exists(path)) return File.ReadAllText(path); } catch { }
            return null;
        }
    }

    public static class Log
    {
        private static readonly object gate = new object();
        private static string file = Paths.Runtime("server.log");
        // USER 2026-11-10 #7: File.AppendAllText opened, seeked, wrote and CLOSED the (23 MB) log on every
        // single line, on whichever thread called it - the UI thread for probe/paper/lyric/click lines. That
        // is 4..8 open/close round trips per second inside the render loop, i.e. inside the animation.
        //
        // Now: lines are formatted into a buffer under a short lock, and ONE append happens on a timer thread
        // every 200 ms. The file stays CLOSED the rest of the time, so the existing verify scripts that read
        // server.log keep working (a held-open writer would make their plain ReadAllLines fail with
        // "being used by another process"), and the UI thread never touches the disk for a log line.
        private static readonly StringBuilder buf = new StringBuilder();
        private static int pending;
        private static readonly System.Threading.Timer flushTimer =
            new System.Threading.Timer(delegate { Flush(); }, null, 200, 200);

        public static void W(string what)
        {
            bool over;
            lock (gate)
            {
                try
                {
                    buf.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                       .Append(' ').Append(what).Append(Environment.NewLine);
                    pending++;
                }
                catch { }
                over = pending >= 48;
            }
            if (over) Flush();                       // a burst must not wait for the tick
        }

        public static void Flush()
        {
            string chunk = null;
            lock (gate)
            {
                if (pending > 0 && buf.Length > 0) { chunk = buf.ToString(); buf.Length = 0; pending = 0; }
            }
            if (chunk == null) return;               // never hold the lock across the file IO
            try { File.AppendAllText(file, chunk, new UTF8Encoding(false)); } catch { }
        }

        public static void Close() { Flush(); }
    }

    public sealed class Config
    {
        public bool Topmost = true;
        public bool Taskbar = false;
        public string Theme = "dark";        // dark | wash | light | transparent | cover
        public bool Cdp = true;
        public bool Http = true;
        public bool ClickThrough = true;
        public bool QueueOpen = false;
        public int HttpPortBase = 46210;
        public bool ExpandCover = true;      // REMOTE_CONTROL_EXPAND_COVER, default true
        public string CoverMode = "auto";    // auto | sleeve | card | line (debug override)
        public string QueueDir = "down";     // last chosen expansion side

        private static string File { get { return Paths.Runtime("config.json"); } }

        public static Config Load()
        {
            Config c = new Config();
            string txt = Paths.ReadText(File);
            if (txt != null)
            {
                JValue v;
                if (JsonParser.TryParse(txt, out v) && v != null && v.Kind == JKind.Obj)
                {
                    c.Topmost = v.Bool("topmost", c.Topmost);
                    c.Taskbar = v.Bool("taskbar", c.Taskbar);
                    c.Theme = v.Str("theme") ?? c.Theme;
                    c.Cdp = v.Bool("cdp", c.Cdp);
                    c.Http = v.Bool("http", c.Http);
                    c.ClickThrough = v.Bool("clickThrough", c.ClickThrough);
                    c.HttpPortBase = (int)v.Num("httpPortBase", c.HttpPortBase);
                    c.QueueOpen = v.Bool("queueOpen", c.QueueOpen);
                    c.ExpandCover = v.Bool("expandCover", c.ExpandCover);
                    c.CoverMode = v.Str("coverMode") ?? c.CoverMode;
                    c.QueueDir = v.Str("queueDir") ?? c.QueueDir;
                }
            }
            // "wash" (blurred cover + scrim) is a first-class theme and the palette cycles into it;
            // it used to be missing here, so every restart silently degraded wash -> dark (D1 could not persist).
            if (c.Theme != "dark" && c.Theme != "wash" && c.Theme != "light" && c.Theme != "transparent" && c.Theme != "cover") c.Theme = "dark";
            return c;
        }

        public void Save()
        {
            string txt = new JsonWriter().Begin()
                .B("topmost", Topmost).B("taskbar", Taskbar).Str("theme", Theme)
                .B("cdp", Cdp).B("http", Http).B("clickThrough", ClickThrough).B("queueOpen", QueueOpen)
                .Num("httpPortBase", HttpPortBase)
                .B("expandCover", ExpandCover).Str("coverMode", CoverMode).Str("queueDir", QueueDir).End().ToString();
            Paths.WriteText(File, txt);
        }
    }

    public static class Position
    {
        // Stored values are DIP (SPEC 4.9: what is remembered is always the card position).
        // A file written by a different DPI (e.g. the old scale=0.8 build) is rejected so the
        // SPEC initial-position formula can take over again (SPEC 1.5 restore fallback).
        public static double[] Load(int dpi, double phys)
        {
            string txt = Paths.ReadText(Paths.Runtime("position.json"));
            if (txt == null) return null;
            JValue v;
            if (!JsonParser.TryParse(txt, out v) || v == null || v.Kind != JKind.Obj) return null;
            int storedDpi = (int)v.Num("dpi", 0);
            double storedPhys = v.Num("phys", 0);
            string unit = v.Str("unit") ?? "";
            if (storedDpi != dpi || Math.Abs(storedPhys - phys) > 0.001 || unit != "dip")
            {
                Log.W("position stale (dpi=" + storedDpi + "/" + dpi + " phys=" + storedPhys + "/" + phys + " unit=" + unit + ") -> use initial formula");
                return null;
            }
            double l = v.Num("left", double.NaN), t = v.Num("top", double.NaN);
            if (double.IsNaN(l) || double.IsNaN(t)) return null;
            return new double[] { l, t };
        }

        // N4: the DPI the user last parked the card on, straight from position.json. A window that has
        // never been shown has no monitor affinity yet, so this is the only honest source for phys.
        public static int StoredDpi()
        {
            string txt = Paths.ReadText(Paths.Runtime("position.json"));
            if (txt == null) return 0;
            JValue v;
            if (!JsonParser.TryParse(txt, out v) || v == null || v.Kind != JKind.Obj) return 0;
            return (int)v.Num("dpi", 0);
        }

        public static void Save(double leftDip, double topDip, int dpi, double phys)
        {
            Paths.WriteText(Paths.Runtime("position.json"),
                new JsonWriter().Begin().Num("left", leftDip).Num("top", topDip)
                    .Num("dpi", dpi).Num("phys", phys).Num("scale", 1)
                    .Str("unit", "dip")
                    .Str("at", DateTime.Now.ToString("s", CultureInfo.InvariantCulture)).End().ToString());
        }

        // Clamp in DIP space against the (physical) work area, keeping the *drawn* part
        // visible: inset 178 DIP is the strip (SPEC 1.4).
        public static double[] Clamp(double left, double top, double width, double height, double phys)
        {
            RECT wa = Win.WorkAreaAt((int)((left + width / 2) * phys), (int)((top + height / 2) * phys));
            double waL = wa.Left / phys, waT = wa.Top / phys, waR = wa.Right / phys, waB = wa.Bottom / phys;
            double maxL = waR - width;
            double maxT = waB - height;
            if (left > maxL) left = maxL;
            if (top > maxT) top = maxT;
            if (left < waL + (width - 594) - 4) left = waL + (width - 594) - 4;    // keep drawn (non-strip) part on screen
            if (left < waL) left = waL;
            if (top < waT) top = waT;
            return new double[] { left, top };
        }
    }

    public static class Protocol
    {
        public const string Scheme = "ncmmini";
        private const string KeyPath = @"Software\Classes\" + Scheme;
        private const string SubOpen = @"shell\open\command";
        private const string Q = "\"";

        public static string ExePath
        {
            get
            {
                string p = Paths.Exe;
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) p = Paths.Overlay("MiniPlayer.exe");
                return p;
            }
        }

        public static string Register()
        {
            string exe = Q + ExePath + Q + " --uri " + Q + "%1" + Q;
            using (RegistryKey root = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (root == null) return "FAIL: cannot create HKCU\\" + KeyPath;
                root.SetValue("", "URL:NCM Mini Protocol");
                root.SetValue("URL Protocol", "");
                using (RegistryKey icon = root.CreateSubKey("DefaultIcon"))
                    if (icon != null) icon.SetValue("", Q + ExePath + Q + ",0");
                using (RegistryKey cmd = root.CreateSubKey(SubOpen))
                    if (cmd != null) cmd.SetValue("", exe);
            }
            return "HKCU\\" + KeyPath + " written; open command = " + exe;
        }

        public static string Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
            bool exists = Registry.CurrentUser.OpenSubKey(KeyPath) != null;
            return exists ? ("FAIL: HKCU\\" + KeyPath + " still present") : ("deleted HKCU\\" + KeyPath);
        }

        public static string Dump()
        {
            StringBuilder b = new StringBuilder();
            using (RegistryKey root = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                if (root == null) return "ABSENT HKCU\\" + KeyPath;
                b.Append("HKCU\\" + KeyPath);
                b.Append(" | (default)=" + root.GetValue(""))
                 .Append(" | URL Protocol=[" + root.GetValue("URL Protocol") + "]");
                b.AppendLine();
                using (RegistryKey c = root.OpenSubKey(SubOpen))
                    if (c != null) b.AppendLine("shell\\open\\command | (default)=" + c.GetValue(""));
                using (RegistryKey i = root.OpenSubKey("DefaultIcon"))
                    if (i != null) b.AppendLine("DefaultIcon | (default)=" + i.GetValue(""));
            }
            return b.ToString();
        }
    }
}
