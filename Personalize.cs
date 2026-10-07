// 個人化：系統色彩 / 深淺色、工作列項目與背景圖片、滑鼠游標
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopColorSorter
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    [StructLayout(LayoutKind.Sequential)]
    struct DWMCOLORIZATIONPARAMS
    {
        public uint ColorizationColor, ColorizationAfterglow, ColorizationColorBalance, ColorizationAfterglowBalance,
            ColorizationBlurBalance, ColorizationGlassReflectionIntensity, ColorizationOpaqueBlend;
    }

    static partial class Native
    {
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint uParam, IntPtr pvParam, uint winIni);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadCursorFromFile(string path);
        [DllImport("user32.dll")] public static extern bool DestroyCursor(IntPtr h);
        [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] public static extern int SHLoadIndirectString(string src, StringBuilder buf, int cch, IntPtr reserved);
        [DllImport("dwmapi.dll", EntryPoint = "#127")] public static extern int DwmGetColorizationParameters(out DWMCOLORIZATIONPARAMS p);
        [DllImport("dwmapi.dll", EntryPoint = "#131")] public static extern int DwmSetColorizationParameters(ref DWMCOLORIZATIONPARAMS p, int unknown);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        public delegate void WinEventDelegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventDelegate proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

        public static string ClassName(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }

    // ======================= 程式設定檔 =======================

    static class AppSettings
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopColorSorter");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");
        static Dictionary<string, string> values;

        static Dictionary<string, string> Values
        {
            get
            {
                if (values != null) return values;
                values = new Dictionary<string, string>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                        {
                            int i = line.IndexOf('=');
                            if (i > 0) values[line.Substring(0, i)] = line.Substring(i + 1);
                        }
                }
                catch { }
                return values;
            }
        }

        public static string Get(string key, string def) { string v; return Values.TryGetValue(key, out v) ? v : def; }
        public static int GetInt(string key, int def) { int v; return int.TryParse(Get(key, null), out v) ? v : def; }
        public static bool GetBool(string key, bool def) { return GetInt(key, def ? 1 : 0) != 0; }

        public static void Set(string key, object value)
        {
            Values[key] = value is bool ? ((bool)value ? "1" : "0") : Convert.ToString(value);
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FilePath, Values.Select(kv => kv.Key + "=" + kv.Value).ToArray(), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ======================= Windows 個人化設定（登錄檔） =======================

    static class WinSettings
    {
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string DwmKey = @"Software\Microsoft\Windows\DWM";
        const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
        const string DesktopKey = @"Control Panel\Desktop";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "DesktopColorSorter";

        public static int GetDword(string key, string name, int def)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key))
            {
                object v = k == null ? null : k.GetValue(name);
                if (v is int) return (int)v;
                int parsed;
                if (v is string && int.TryParse((string)v, out parsed)) return parsed;
                return def;
            }
        }

        public static void SetDword(string key, string name, int value)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key)) k.SetValue(name, value, RegistryValueKind.DWord);
        }

        // 通知所有視窗「設定改了」。要逐一等每個視窗回應，放在畫面執行緒會卡住，所以丟到背景，
        // 而且短時間內連續的變更（例如快速點好幾個顏色）會合併成一次
        static readonly object broadcastGate = new object();
        static readonly HashSet<string> pendingAreas = new HashSet<string>();
        static Action pendingDwm;
        static bool broadcasting;

        public static void Broadcast(string area) { QueueBroadcast(area, null); }

        static void QueueBroadcast(string area, Action dwmWork)
        {
            lock (broadcastGate)
            {
                if (area != null) pendingAreas.Add(area);
                if (dwmWork != null) pendingDwm = dwmWork; // 只需要套用最後一次
                if (broadcasting) return;
                broadcasting = true;
            }
            ThreadPool.QueueUserWorkItem(delegate { PumpBroadcasts(); });
        }

        static void PumpBroadcasts()
        {
            Thread.Sleep(80); // 等一下，把連續的變更收集起來
            while (true)
            {
                string[] areas;
                Action dwm;
                lock (broadcastGate)
                {
                    if (pendingAreas.Count == 0 && pendingDwm == null) { broadcasting = false; return; }
                    areas = pendingAreas.ToArray();
                    pendingAreas.Clear();
                    dwm = pendingDwm;
                    pendingDwm = null;
                }
                if (dwm != null) { try { dwm(); } catch { } }
                foreach (string a in areas)
                {
                    IntPtr r;
                    try { Native.SendMessageTimeout((IntPtr)0xFFFF, 0x001A /*WM_SETTINGCHANGE*/, IntPtr.Zero, a, 0x0002 /*SMTO_ABORTIFHUNG*/, 1000, out r); }
                    catch { }
                }
            }
        }

        // ---- 深淺色 / 透明效果 ----
        public static bool SystemLight { get { return GetDword(Personalize, "SystemUsesLightTheme", 1) != 0; } set { SetDword(Personalize, "SystemUsesLightTheme", value ? 1 : 0); Broadcast("ImmersiveColorSet"); } }
        public static bool AppsLight { get { return GetDword(Personalize, "AppsUseLightTheme", 1) != 0; } set { SetDword(Personalize, "AppsUseLightTheme", value ? 1 : 0); Broadcast("ImmersiveColorSet"); } }
        public static bool Transparency { get { return GetDword(Personalize, "EnableTransparency", 1) != 0; } set { SetDword(Personalize, "EnableTransparency", value ? 1 : 0); Broadcast("ImmersiveColorSet"); } }
        public static bool AccentOnStart { get { return GetDword(Personalize, "ColorPrevalence", 0) != 0; } set { SetDword(Personalize, "ColorPrevalence", value ? 1 : 0); Broadcast("ImmersiveColorSet"); } }
        public static bool AccentOnTitle { get { return GetDword(DwmKey, "ColorPrevalence", 0) != 0; } set { SetDword(DwmKey, "ColorPrevalence", value ? 1 : 0); Broadcast("ImmersiveColorSet"); } }

        public static bool AutoAccent
        {
            get { return GetDword(DesktopKey, "AutoColorization", 0) != 0; }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(DesktopKey))
                {
                    // 這個值在不同版本可能是字串或 DWORD，沿用原本的型別
                    RegistryValueKind kind = RegistryValueKind.DWord;
                    try { if (k.GetValue("AutoColorization") != null) kind = k.GetValueKind("AutoColorization"); } catch { }
                    if (kind == RegistryValueKind.String) k.SetValue("AutoColorization", value ? "1" : "0", kind);
                    else k.SetValue("AutoColorization", value ? 1 : 0, RegistryValueKind.DWord);
                }
                Broadcast("ImmersiveColorSet");
            }
        }

        // ---- 強調色 ----
        public static Color Accent
        {
            get
            {
                uint u = unchecked((uint)GetDword(DwmKey, "AccentColor", unchecked((int)0xFFD77800)));
                return Color.FromArgb((int)(u & 0xFF), (int)((u >> 8) & 0xFF), (int)((u >> 16) & 0xFF));
            }
        }

        static int Abgr(Color c) { return unchecked((int)(0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R)); }

        public static void SetAccent(Color c)
        {
            Color[] shades = Shades(c);
            byte[] palette = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                palette[i * 4] = shades[i].R; palette[i * 4 + 1] = shades[i].G; palette[i * 4 + 2] = shades[i].B; palette[i * 4 + 3] = 0;
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(AccentKey))
            {
                k.SetValue("AccentPalette", palette, RegistryValueKind.Binary);
                k.SetValue("AccentColorMenu", Abgr(c), RegistryValueKind.DWord);
                k.SetValue("StartColorMenu", Abgr(shades[4]), RegistryValueKind.DWord);
            }
            int argb = unchecked((int)(0xC4000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(DwmKey))
            {
                k.SetValue("AccentColor", Abgr(c), RegistryValueKind.DWord);
                k.SetValue("ColorizationColor", argb, RegistryValueKind.DWord);
                k.SetValue("ColorizationAfterglow", argb, RegistryValueKind.DWord);
            }
            // 讓視窗框線立即換色（未公開 API，失敗就算了，登錄檔已經寫好）。DWM 重畫所有視窗比較慢，在背景做
            QueueBroadcast("ImmersiveColorSet", delegate
            {
                DWMCOLORIZATIONPARAMS p;
                if (Native.DwmGetColorizationParameters(out p) == 0)
                {
                    p.ColorizationColor = unchecked((uint)argb);
                    p.ColorizationAfterglow = unchecked((uint)argb);
                    Native.DwmSetColorizationParameters(ref p, 0);
                }
            });
        }

        // 產生 Windows 強調色調色盤：3 個較亮、本色、3 個較暗，最後一個是互補色
        static Color[] Shades(Color c)
        {
            double h = c.GetHue(), s = c.GetSaturation(), l = c.GetBrightness();
            double[] d = { 0.30, 0.20, 0.10, 0, -0.10, -0.20, -0.30 };
            Color[] r = new Color[8];
            for (int i = 0; i < 7; i++) r[i] = i == 3 ? c : FromHsl(h, s, Math.Max(0.05, Math.Min(0.95, l + d[i])));
            r[7] = FromHsl((h + 180) % 360, s, l);
            return r;
        }

        public static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = l - c / 2;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            Func<double, int> f = v => Math.Max(0, Math.Min(255, (int)Math.Round((v + m) * 255)));
            return Color.FromArgb(f(r), f(g), f(b));
        }

        // ---- 工作列項目 ----
        public static bool TaskbarLeft { get { return GetDword(Advanced, "TaskbarAl", 1) == 0; } set { SetDword(Advanced, "TaskbarAl", value ? 0 : 1); Broadcast("TraySettings"); } }
        // Windows 11 新版的工作列讀的是 SearchboxTaskbarModeCache，兩個都寫
        public static int SearchMode
        {
            get { return GetDword(SearchKey, "SearchboxTaskbarMode", 1); }
            set
            {
                SetDword(SearchKey, "SearchboxTaskbarMode", value);
                SetDword(SearchKey, "SearchboxTaskbarModeCache", value);
                Broadcast("TraySettings");
            }
        }

        // 「使用者選擇保護驅動程式」(UCPD) 開著時，小工具等設定只有 Microsoft 自己的程式能改
        public static bool UcpdActive
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\UCPD"))
                        return k != null && Convert.ToInt32(k.GetValue("Start", 4)) != 4;
                }
                catch { return false; }
            }
        }

        // 程式結束前把還沒送出的設定變更通知送完（最多等 1.5 秒）
        public static void FlushBroadcasts()
        {
            for (int i = 0; i < 30; i++)
            {
                lock (broadcastGate) { if (!broadcasting && pendingAreas.Count == 0 && pendingDwm == null) return; }
                Thread.Sleep(50);
            }
        }
        public static bool TaskView { get { return GetDword(Advanced, "ShowTaskViewButton", 1) != 0; } set { SetDword(Advanced, "ShowTaskViewButton", value ? 1 : 0); Broadcast("TraySettings"); } }
        public static bool Widgets { get { return GetDword(Advanced, "TaskbarDa", 1) != 0; } set { SetDword(Advanced, "TaskbarDa", value ? 1 : 0); Broadcast("TraySettings"); } }
        public static bool ClockSeconds { get { return GetDword(Advanced, "ShowSecondsInSystemClock", 0) != 0; } set { SetDword(Advanced, "ShowSecondsInSystemClock", value ? 1 : 0); Broadcast("TraySettings"); } }

        public static bool TaskbarAutoHide
        {
            get
            {
                APPBARDATA d = new APPBARDATA();
                d.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
                return ((uint)Native.SHAppBarMessage(4 /*ABM_GETSTATE*/, ref d) & 1) != 0;
            }
            set
            {
                APPBARDATA d = new APPBARDATA();
                d.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
                d.hWnd = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
                d.lParam = (IntPtr)(value ? 1 /*ABS_AUTOHIDE*/ : 2 /*ABS_ALWAYSONTOP*/);
                Native.SHAppBarMessage(10 /*ABM_SETSTATE*/, ref d);
            }
        }

        // ---- 開機自動執行 ----
        public static bool AutoStart
        {
            get { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null; }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --tray");
                    else if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
                }
            }
        }

        public static void RestartExplorer()
        {
            FlushBroadcasts();
            foreach (Process p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
            // Windows 通常會自動把檔案總管叫回來；3 秒內沒回來就自己啟動
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(100);
                if (Process.GetProcessesByName("explorer").Length > 0) return;
            }
            Process.Start("explorer.exe");
        }

        public static void OpenSettings(string page)
        {
            try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); } catch { }
        }
    }

    // ======================= 工作列背景圖片 =======================

    // 在每條工作列上疊一個半透明、滑鼠可穿透的視窗來顯示圖片（不需要修改或注入檔案總管）。
    //  * 圖片視窗的「擁有者」設成工作列：Windows 會讓被擁有的視窗永遠緊貼在擁有者上方，點工作列開程式時就不會被蓋住。
    //    （跨程式的擁有關係會讓兩邊共用輸入狀態，所以圖片視窗放在自己專用、永遠不會忙碌的執行緒上）
    //  * 何時隱藏完全照檔案總管自己的判斷：有全螢幕程式時，檔案總管會把那條工作列改成「非最上層」，這時才隱藏。
    sealed class TaskbarSkin : IDisposable
    {
        // ---- 設定（在畫面執行緒設定，呼叫 Apply() 後生效）----
        public bool Enabled;
        public int OpacityPercent = 35;
        public int Fit;           // 0 填滿 1 延展 2 並排
        public int Position = 50; // 填滿、沒有框選時要顯示圖片的哪一段（0 = 最上面，100 = 最下面）
        public RectangleF Crop;   // 框選的範圍（0~1）；空的就用 Position
        public static readonly string LogPath = Path.Combine(AppSettings.Dir, "taskbar_log.txt");

        readonly object gate = new object();
        Bitmap original;          // 畫面執行緒用的原圖（框選視窗等）
        Bitmap handoff;           // 交給圖片執行緒的複本
        bool handoffChanged;

        // ---- 以下只在圖片執行緒使用 ----
        Thread thread;
        Control pump;
        Bitmap paintImage;
        readonly Dictionary<IntPtr, TaskbarOverlay> overlays = new Dictionary<IntPtr, TaskbarOverlay>();
        readonly Dictionary<IntPtr, bool> lastShown = new Dictionary<IntPtr, bool>();
        System.Windows.Forms.Timer timer;
        Native.WinEventDelegate winEventProc, reorderProc;
        IntPtr winEventHook, reorderHook;
        int fixCount, fixWindowStart;
        bool ticking;

        public Bitmap Image
        {
            get { return original; }
            set
            {
                if (original != null && original != value) original.Dispose();
                original = value;
                // 給圖片執行緒一份自己的複本（GDI+ 的圖片不能兩個執行緒同時用）
                Bitmap copy = null;
                if (value != null)
                {
                    double k = Math.Min(1.0, 2600.0 / Math.Max(value.Width, value.Height));
                    copy = new Bitmap(value, Math.Max(1, (int)(value.Width * k)), Math.Max(1, (int)(value.Height * k)));
                }
                lock (gate)
                {
                    if (handoff != null && handoffChanged) handoff.Dispose(); // 還沒被拿走的舊複本
                    handoff = copy;
                    handoffChanged = true;
                }
            }
        }

        internal Bitmap PaintImage { get { return paintImage; } }

        public double Opacity { get { return Math.Max(0.05, Math.Min(0.9, OpacityPercent / 100.0)); } }

        public void Apply()
        {
            EnsureThread();
            try { pump.BeginInvoke((Action)ApplyCore); } catch { }
        }

        void EnsureThread()
        {
            if (thread != null) return;
            ManualResetEvent ready = new ManualResetEvent(false);
            thread = new Thread(delegate ()
            {
                pump = new Control();
                pump.CreateControl();
                IntPtr unused = pump.Handle;
                timer = new System.Windows.Forms.Timer { Interval = 50 };
                timer.Tick += delegate { Tick(); };
                winEventProc = delegate { Tick(); };
                reorderProc = delegate { FixZOrder(); };
                ready.Set();
                Application.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "TaskbarSkin";
            thread.Start();
            ready.WaitOne(3000);
        }

        void ApplyCore()
        {
            lock (gate)
            {
                if (handoffChanged)
                {
                    if (paintImage != null) paintImage.Dispose();
                    paintImage = handoff;
                    handoff = null;
                    handoffChanged = false;
                }
            }
            if (Enabled && paintImage != null)
            {
                timer.Start();
                // 前景視窗一換就立刻檢查，不用等計時器
                if (winEventHook == IntPtr.Zero)
                    winEventHook = Native.SetWinEventHook(0x0003 /*EVENT_SYSTEM_FOREGROUND*/, 0x0003, IntPtr.Zero, winEventProc, 0, 0, 0 /*WINEVENT_OUTOFCONTEXT*/);
                if (reorderHook == IntPtr.Zero)
                    reorderHook = Native.SetWinEventHook(0x8004 /*EVENT_OBJECT_REORDER*/, 0x8004, IntPtr.Zero, reorderProc, 0, 0, 0x0002 /*OUTOFCONTEXT|SKIPOWNPROCESS*/);
                Tick();
                foreach (TaskbarOverlay o in overlays.Values) if (!o.IsDisposed) { o.SyncOpacity(); o.Invalidate(); }
            }
            else
            {
                timer.Stop();
                if (winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(winEventHook); winEventHook = IntPtr.Zero; }
                if (reorderHook != IntPtr.Zero) { Native.UnhookWinEvent(reorderHook); reorderHook = IntPtr.Zero; }
                CloseAll();
            }
        }

        // 有視窗改變前後順序（例如點工作列上的程式時，工作列被拉到最前面）→ 立刻把圖片放回工作列正上方
        void FixZOrder()
        {
            if (ticking || !Enabled) return;
            // 萬一和其他程式互搶位置，一秒內最多修 30 次，其餘交給計時器
            int now = Environment.TickCount;
            if (now - fixWindowStart > 1000) { fixWindowStart = now; fixCount = 0; }
            if (++fixCount > 30) return;
            ticking = true;
            try
            {
                foreach (KeyValuePair<IntPtr, TaskbarOverlay> kv in overlays)
                    if (!kv.Value.IsDisposed && kv.Value.IsHandleCreated && kv.Value.Visible) KeepJustAbove(kv.Value.Handle, kv.Key);
            }
            catch { }
            finally { ticking = false; }
        }

        void CloseAll()
        {
            foreach (TaskbarOverlay o in overlays.Values) if (!o.IsDisposed) o.Dispose();
            overlays.Clear();
            lastShown.Clear();
        }

        static List<IntPtr> FindTaskbars()
        {
            List<IntPtr> bars = new List<IntPtr>();
            IntPtr main = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            if (main != IntPtr.Zero) bars.Add(main);
            for (IntPtr w = IntPtr.Zero; ; )
            {
                w = Native.FindWindowEx(IntPtr.Zero, w, "Shell_SecondaryTrayWnd", null);
                if (w == IntPtr.Zero) break;
                bars.Add(w);
            }
            return bars;
        }

        void Tick()
        {
            if (ticking || !Enabled || paintImage == null) return;
            ticking = true;
            try
            {
                List<IntPtr> bars = FindTaskbars();
                foreach (IntPtr key in overlays.Keys.ToList())
                {
                    TaskbarOverlay o = overlays[key];
                    if (!bars.Contains(key) || o.IsDisposed || o.HandleLost)
                    {
                        if (!o.IsDisposed) o.Dispose();
                        overlays.Remove(key);
                        lastShown.Remove(key);
                    }
                }
                foreach (IntPtr bar in bars)
                {
                    RECT r;
                    if (!Native.GetWindowRect(bar, out r)) continue;
                    Rectangle rb = r.ToRectangle();
                    bool visible = Native.IsWindowVisible(bar) && rb.Width > 0 && rb.Height > 4;
                    bool onTop = (Native.GetWindowLong(bar, -20 /*GWL_EXSTYLE*/) & 0x8 /*WS_EX_TOPMOST*/) != 0;
                    bool show = visible && onTop;

                    TaskbarOverlay o;
                    if (!overlays.TryGetValue(bar, out o))
                    {
                        o = new TaskbarOverlay(this, bar);
                        overlays[bar] = o;
                    }
                    if (o.Bounds != rb) o.Bounds = rb;
                    if (show && !o.Visible) o.Show();
                    else if (!show && o.Visible) o.Hide();
                    if (show)
                    {
                        o.EnsureOwner();
                        // 點工作列上的程式時，檔案總管會把工作列拉到最前面而蓋住圖片：發現圖片不在工作列正上方就馬上放回去
                        KeepJustAbove(o.Handle, bar);
                    }

                    bool was;
                    if (!lastShown.TryGetValue(bar, out was) || was != show)
                    {
                        lastShown[bar] = show;
                        Log(bar, show, visible ? (onTop ? "工作列在最上層" : "工作列被檔案總管降到非最上層（有全螢幕程式）") : "工作列看不見");
                    }
                }
            }
            catch { }
            finally { ticking = false; }
        }

        // 緊貼在工作列正上方：不搶到所有最上層視窗的最前面（不然會蓋住通知區域的選單）
        static void KeepJustAbove(IntPtr overlay, IntPtr bar)
        {
            IntPtr above = Native.GetWindow(bar, 3 /*GW_HWNDPREV*/);
            if (above == overlay) return;
            Native.SetWindowPos(overlay, above == IntPtr.Zero ? (IntPtr)(-1) /*HWND_TOPMOST*/ : above, 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0010 | 0x0200 /*NOSIZE|NOMOVE|NOACTIVATE|NOOWNERZORDER*/);
        }

        // 記錄每次顯示/隱藏的原因，之後如果還有「閃一下」可以查出是哪個視窗造成的
        static void Log(IntPtr bar, bool show, string reason)
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                uint pid;
                Native.GetWindowThreadProcessId(fg, out pid);
                string proc = "";
                IntPtr hp = Native.OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
                if (hp != IntPtr.Zero)
                {
                    try
                    {
                        StringBuilder psb = new StringBuilder(1024);
                        int n = psb.Capacity;
                        if (Native.QueryFullProcessImageName(hp, 0, psb, ref n)) proc = Path.GetFileNameWithoutExtension(psb.ToString());
                    }
                    finally { Native.CloseHandle(hp); }
                }
                string line = DateTime.Now.ToString("MM-dd HH:mm:ss.fff") + "  " + (Native.ClassName(bar) == "Shell_TrayWnd" ? "主工作列" : "副工作列") +
                    "  " + (show ? "顯示" : "隱藏") + "  " + reason + "  前景=" + Native.ClassName(fg) + " (" + proc + ")";
                Directory.CreateDirectory(AppSettings.Dir);
                List<string> lines = File.Exists(LogPath) ? File.ReadAllLines(LogPath, Encoding.UTF8).ToList() : new List<string>();
                lines.Add(line);
                if (lines.Count > 300) lines.RemoveRange(0, lines.Count - 300);
                File.WriteAllLines(LogPath, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public void Dispose()
        {
            if (thread == null) return;
            try
            {
                pump.BeginInvoke((Action)delegate
                {
                    timer.Stop();
                    if (winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(winEventHook); winEventHook = IntPtr.Zero; }
                    if (reorderHook != IntPtr.Zero) { Native.UnhookWinEvent(reorderHook); reorderHook = IntPtr.Zero; }
                    CloseAll();
                    if (paintImage != null) { paintImage.Dispose(); paintImage = null; }
                    pump.Dispose();
                    Application.ExitThread();
                });
            }
            catch { }
            thread.Join(2000);
            thread = null;
        }

        // 自我測試：建立（但不顯示）一個以工作列為擁有者的視窗，確認擁有關係真的生效
        public static string SelfTest()
        {
            IntPtr bar = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            if (bar == IntPtr.Zero) return "no taskbar";
            string result = null;
            Thread t = new Thread(delegate ()
            {
                using (TaskbarSkin s = new TaskbarSkin())
                using (TaskbarOverlay o = new TaskbarOverlay(s, bar))
                {
                    IntPtr h = o.Handle; // 只建立 handle，不 Show
                    o.EnsureOwner();
                    RECT r; Native.GetWindowRect(bar, out r);
                    result = "overlay handle=" + (h != IntPtr.Zero) + " owner=" + (Native.GetWindow(h, 4 /*GW_OWNER*/) == bar) +
                        " taskbar=" + r.ToRectangle() + " topmost=" + ((Native.GetWindowLong(bar, -20) & 8) != 0) + " bars=" + FindTaskbars().Count;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(5000);
            return result ?? "timeout";
        }
    }

    sealed class TaskbarOverlay : Form
    {
        readonly TaskbarSkin skin;
        readonly IntPtr owner;

        public TaskbarOverlay(TaskbarSkin skin, IntPtr owner)
        {
            this.skin = skin;
            this.owner = owner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Opacity = skin.Opacity; // < 1，WinForms 會建立分層視窗
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 最上層用視窗樣式設定（WinForms 的 TopMost 屬性會在顯示時搶焦點）
                cp.ExStyle |= 0x08 /*TOPMOST*/ | 0x20 /*TRANSPARENT*/ | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnsureOwner();
        }

        // WinForms 建立視窗後會把擁有者清掉，所以自己再設一次；之後每次檢查也會補設
        public void EnsureOwner()
        {
            if (owner == IntPtr.Zero || !IsHandleCreated) return;
            if (Native.GetWindow(Handle, 4 /*GW_OWNER*/) == owner) return;
            if (IntPtr.Size == 8) Native.SetWindowLongPtr64(Handle, -8 /*GWLP_HWNDPARENT*/, owner);
            else Native.SetWindowLong32(Handle, -8, owner.ToInt32());
        }

        // 檔案總管重新啟動時工作列被摧毀，擁有者被摧毀的視窗也會跟著消失 → 標記起來讓 TaskbarSkin 重建
        public bool HandleLost;
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (!Disposing && !IsDisposed && !RecreatingHandle) HandleLost = true;
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084 /*WM_NCHITTEST*/) { m.Result = (IntPtr)(-1) /*HTTRANSPARENT*/; return; }
            base.WndProc(ref m);
        }

        public void SyncOpacity()
        {
            if (Math.Abs(Opacity - skin.Opacity) > 0.001) Opacity = skin.Opacity;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);
            Bitmap img = skin.PaintImage;
            if (img == null) return;
            Rectangle cr = ClientRectangle;
            if (cr.Width <= 0 || cr.Height <= 0) return;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (skin.Fit == 1)
            {
                g.DrawImage(img, cr);
            }
            else if (skin.Fit == 2)
            {
                float k = cr.Height / (float)img.Height, w = img.Width * k;
                for (float x = 0; x < cr.Width; x += w) g.DrawImage(img, x, 0, w, cr.Height);
            }
            else if (!skin.Crop.IsEmpty)
            {
                g.DrawImage(img, cr, CropRect.SourceRect(img.Size, skin.Crop, cr.Width / (double)cr.Height), GraphicsUnit.Pixel);
            }
            else
            {
                double k = Math.Max(cr.Width / (double)img.Width, cr.Height / (double)img.Height);
                float w = (float)(img.Width * k), h = (float)(img.Height * k);
                g.DrawImage(img, (cr.Width - w) / 2, (cr.Height - h) * skin.Position / 100f, w, h);
            }
        }
    }

    // ======================= 滑鼠游標 =======================

    class CursorScheme
    {
        public string Key, Display;
        public string[] Files; // 依 CursorStyler.Roles 順序
        public override string ToString() { return Display; }
    }

    static class CursorStyler
    {
        const string CursorKey = @"Control Panel\Cursors";
        const string SchemesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
        public static readonly string[] Roles = { "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No",
            "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person" };
        static readonly string BackupFile = Path.Combine(AppSettings.Dir, "cursors_backup.txt");
        public static readonly string CursorDir = Path.Combine(AppSettings.Dir, "cursors");

        public static List<CursorScheme> SystemSchemes()
        {
            List<CursorScheme> list = new List<CursorScheme>();
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(SchemesKey))
            {
                if (k == null) return list;
                foreach (string name in k.GetValueNames())
                {
                    string[] parts = (Convert.ToString(k.GetValue(name)) ?? "").Split(',');
                    CursorScheme s = new CursorScheme { Key = name, Display = name };
                    s.Files = new string[Roles.Length];
                    for (int i = 0; i < Roles.Length; i++) s.Files[i] = i < parts.Length ? parts[i] : "";
                    if (parts.Length > Roles.Length && parts[Roles.Length].StartsWith("@"))
                    {
                        // 名稱字串本身含逗號（"@main.cpl,-1020"），要把剩下的部分接回來
                        string res = string.Join(",", parts.Skip(Roles.Length).ToArray());
                        StringBuilder sb = new StringBuilder(260);
                        if (Native.SHLoadIndirectString(res, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0) s.Display = sb.ToString();
                    }
                    s.Display = Translate(s.Display);
                    list.Add(s);
                }
            }
            return list.OrderBy(s => s.Display).ToList();
        }

        static string Translate(string name)
        {
            string[,] map = {
                { "Windows Default", "Windows 預設" }, { "Windows Black", "Windows 黑色" }, { "Windows Inverted", "Windows 反轉" },
                { "Windows Standard", "Windows 標準" }, { "Magnified", "放大" }, { "(extra large)", "（特大）" }, { "(large)", "（大）" },
                { "(system scheme)", "" } };
            for (int i = 0; i < map.GetLength(0); i++) name = name.Replace(map[i, 0], map[i, 1]);
            return name.Trim();
        }

        public static string CurrentSchemeName
        {
            get { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CursorKey)) return k == null ? "" : Convert.ToString(k.GetValue("")); }
        }

        // 第一次修改前，把使用者原本的游標設定存起來
        static void BackupOnce()
        {
            if (File.Exists(BackupFile)) return;
            Directory.CreateDirectory(AppSettings.Dir);
            StringBuilder sb = new StringBuilder();
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CursorKey))
            {
                if (k == null) return;
                sb.Append("\t").Append(Convert.ToString(k.GetValue(""))).Append('\n');
                sb.Append("Scheme Source\t").Append(Convert.ToString(k.GetValue("Scheme Source") ?? 0)).Append('\n');
                foreach (string r in Roles)
                    sb.Append(r).Append('\t').Append(Convert.ToString(k.GetValue(r, "", RegistryValueOptions.DoNotExpandEnvironmentNames))).Append('\n');
            }
            File.WriteAllText(BackupFile, sb.ToString(), Encoding.UTF8);
        }

        public static bool HasBackup { get { return File.Exists(BackupFile); } }

        static void Write(string schemeName, int source, Func<string, string> fileFor)
        {
            BackupOnce();
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(CursorKey))
            {
                foreach (string r in Roles) k.SetValue(r, fileFor(r) ?? "", RegistryValueKind.ExpandString);
                k.SetValue("", schemeName);
                k.SetValue("Scheme Source", source, RegistryValueKind.DWord);
            }
            Native.SystemParametersInfo(0x0057 /*SPI_SETCURSORS*/, 0, IntPtr.Zero, 0x01 | 0x02 /*SPIF_UPDATEINIFILE|SPIF_SENDCHANGE*/);
        }

        public static void ApplyScheme(CursorScheme s)
        {
            Write(s.Key, 2, r => s.Files[Array.IndexOf(Roles, r)]);
        }

        public static void RestoreBackup()
        {
            if (!File.Exists(BackupFile)) return;
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(BackupFile, Encoding.UTF8))
            {
                int i = line.IndexOf('\t');
                if (i >= 0) d[line.Substring(0, i)] = line.Substring(i + 1);
            }
            string name, src, file;
            d.TryGetValue("", out name);
            d.TryGetValue("Scheme Source", out src);
            int source; int.TryParse(src, out source);
            Write(name ?? "", source, r => d.TryGetValue(r, out file) ? file : "");
        }

        // 產生自訂顏色游標（箭頭、手指、文字游標），其餘沿用 Windows 預設
        public static void ApplyCustom(CursorStyle st)
        {
            Directory.CreateDirectory(CursorDir);
            string stamp = DateTime.Now.ToString("HHmmss"); // 換檔名，避免 Windows 用快取裡的舊游標
            foreach (string old in Directory.GetFiles(CursorDir, "*.cur")) { try { File.Delete(old); } catch { } }
            Dictionary<string, string> custom = new Dictionary<string, string>();
            foreach (CursorKind kind in new[] { CursorKind.Arrow, CursorKind.Hand, CursorKind.IBeam })
            {
                string path = Path.Combine(CursorDir, kind.ToString().ToLower() + "_" + stamp + ".cur");
                CursorArt.SaveCur(st, kind, path);
                custom[kind.ToString()] = path;
            }
            CursorScheme aero = SystemSchemes().FirstOrDefault(s => s.Key == "Windows Aero");
            Write("DesktopColorSorter 自訂", 1, r =>
            {
                string p;
                if (custom.TryGetValue(r, out p)) return p;
                return aero == null ? "" : aero.Files[Array.IndexOf(Roles, r)];
            });
        }
    }

    enum CursorKind { Arrow, Hand, IBeam }

    class CursorStyle
    {
        public Color Fill = Color.FromArgb(0, 120, 215);
        public bool Rainbow;
        public bool DarkOutline;
        public int Size = 0; // 0 標準 1 大 2 特大
        public double Scale { get { return Size == 2 ? 1.08 : Size == 1 ? 0.85 : 0.66; } }
    }

    static class CursorArt
    {
        // 以 32×32 為設計格的形狀
        static GraphicsPath Shape(CursorKind kind, out PointF hot)
        {
            GraphicsPath p = new GraphicsPath(FillMode.Winding);
            if (kind == CursorKind.Arrow)
            {
                p.AddPolygon(new[] { new PointF(1, 1), new PointF(1, 24), new PointF(6.5f, 19), new PointF(10.5f, 28), new PointF(14.5f, 26.3f), new PointF(10.6f, 17.6f), new PointF(18, 17.6f) });
                hot = new PointF(1, 1);
            }
            else if (kind == CursorKind.Hand)
            {
                AddRound(p, new RectangleF(9, 1, 6.5f, 19), 3.2f);       // 食指
                AddRound(p, new RectangleF(7, 12, 18, 18), 5f);          // 手掌
                AddRound(p, new RectangleF(2.5f, 15, 8, 6.5f), 3.2f);    // 大拇指
                hot = new PointF(12.2f, 1);
            }
            else
            {
                p.AddRectangle(new RectangleF(8, 2, 12, 3));
                p.AddRectangle(new RectangleF(12.5f, 2, 3, 27));
                p.AddRectangle(new RectangleF(8, 26, 12, 3));
                hot = new PointF(14, 15.5f);
            }
            return p;
        }

        static void AddRound(GraphicsPath p, RectangleF r, float rad)
        {
            GraphicsPath sub = new GraphicsPath();
            float d = rad * 2;
            sub.AddArc(r.X, r.Y, d, d, 180, 90);
            sub.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            sub.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            sub.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            sub.CloseFigure();
            p.AddPath(sub, false);
        }

        public static Bitmap Render(CursorStyle st, CursorKind kind, int canvas, out Point hotspot)
        {
            Bitmap b = new Bitmap(canvas, canvas, PixelFormat.Format32bppArgb);
            PointF hot;
            using (GraphicsPath path = Shape(kind, out hot))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float k = (float)(canvas / 32.0 * st.Scale);
                float outline = Math.Max(1.2f, 1.5f * k);
                using (Matrix m = new Matrix())
                {
                    m.Translate(outline * 0.6f, outline * 0.6f);
                    m.Scale(k, k);
                    path.Transform(m);
                    PointF[] hp = { hot };
                    m.TransformPoints(hp);
                    hotspot = new Point(Math.Max(0, Math.Min(canvas - 1, (int)Math.Round(hp[0].X))), Math.Max(0, Math.Min(canvas - 1, (int)Math.Round(hp[0].Y))));
                }
                Color line = st.DarkOutline ? Color.FromArgb(20, 20, 24) : Color.White;
                using (Pen pen = new Pen(line, outline * 2) { LineJoin = LineJoin.Round })
                    g.DrawPath(pen, path);
                RectangleF bounds = path.GetBounds();
                Brush fill;
                if (st.Rainbow)
                {
                    LinearGradientBrush lg = new LinearGradientBrush(RectangleF.Inflate(bounds, 1, 1), Color.Red, Color.Violet, 60f);
                    ColorBlend cb = new ColorBlend();
                    cb.Colors = new[] { Color.FromArgb(255, 59, 48), Color.FromArgb(255, 149, 0), Color.FromArgb(255, 214, 10), Color.FromArgb(52, 199, 89), Color.FromArgb(0, 122, 255), Color.FromArgb(175, 82, 222) };
                    cb.Positions = new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
                    lg.InterpolationColors = cb;
                    fill = lg;
                }
                else fill = new SolidBrush(st.Fill);
                using (fill) g.FillPath(fill, path);
                if (kind == CursorKind.Hand) // 指節線
                    using (Pen pen = new Pen(Color.FromArgb(110, line), Math.Max(1f, 0.9f * k)))
                    {
                        PointF[] pts = { new PointF(15.5f, 13), new PointF(15.5f, 18.5f), new PointF(20.3f, 13.4f), new PointF(20.3f, 18.5f) };
                        using (Matrix m = new Matrix())
                        {
                            m.Translate(outline * 0.6f, outline * 0.6f);
                            m.Scale(k, k);
                            m.TransformPoints(pts);
                        }
                        g.DrawLine(pen, pts[0], pts[1]);
                        g.DrawLine(pen, pts[2], pts[3]);
                    }
            }
            return b;
        }

        // 寫出含多種尺寸的 .cur（Windows 會依照游標大小 / DPI 自動挑選）
        public static void SaveCur(CursorStyle st, CursorKind kind, string path)
        {
            int[] sizes = { 128, 96, 64, 48, 32 };
            List<byte[]> images = new List<byte[]>();
            List<Point> hots = new List<Point>();
            foreach (int n in sizes)
            {
                Point hot;
                using (Bitmap b = Render(st, kind, n, out hot))
                {
                    images.Add(ToDib(b));
                    hots.Add(hot);
                }
            }
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((ushort)0); w.Write((ushort)2); w.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)hots[i].X); w.Write((ushort)hots[i].Y);
                    w.Write((uint)images[i].Length); w.Write((uint)offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        // 32 位元 BGRA（由下往上）+ 全 0 的 AND 遮罩
        static byte[] ToDib(Bitmap b)
        {
            int n = b.Width;
            byte[] px = new byte[n * n * 4];
            BitmapData d = b.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < n; y++) Marshal.Copy(d.Scan0 + (n - 1 - y) * d.Stride, px, y * n * 4, n * 4);
            b.UnlockBits(d);
            int maskStride = ((n + 31) / 32) * 4;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(px.Length + maskStride * n); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                w.Write(px);
                w.Write(new byte[maskStride * n]);
                return ms.ToArray();
            }
        }

        public static string SelfTest()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dcs_cursor_test");
            Directory.CreateDirectory(dir);
            StringBuilder sb = new StringBuilder();
            foreach (CursorKind k in new[] { CursorKind.Arrow, CursorKind.Hand, CursorKind.IBeam })
            {
                string p = Path.Combine(dir, k + ".cur");
                SaveCur(new CursorStyle { Rainbow = true, Size = 1 }, k, p);
                IntPtr h = Native.LoadCursorFromFile(p); // 只載入檢查格式，不會套用到系統
                sb.Append(k).Append(h != IntPtr.Zero ? " ok " : " FAIL ");
                if (h != IntPtr.Zero) Native.DestroyCursor(h);
            }
            return sb.ToString();
        }
    }

    // ======================= 程式圖示 =======================

    static class AppIcon
    {
        public static Icon Create()
        {
            using (Bitmap b = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Color[] cs = { Color.FromArgb(234, 67, 53), Color.FromArgb(251, 140, 0), Color.FromArgb(251, 192, 45),
                                   Color.FromArgb(67, 160, 71), Color.FromArgb(0, 172, 193), Color.FromArgb(30, 136, 229),
                                   Color.FromArgb(142, 36, 170), Color.FromArgb(216, 27, 96), Color.FromArgb(120, 120, 120) };
                    for (int i = 0; i < 9; i++)
                        using (SolidBrush br = new SolidBrush(cs[i]))
                        using (GraphicsPath p = Tile.Round(new RectangleF(1 + (i % 3) * 10.3f, 1 + (i / 3) * 10.3f, 9, 9), 2.5f))
                            g.FillPath(br, p);
                }
                return Icon.FromHandle(b.GetHicon());
            }
        }
    }
}
