// 自訂開始功能表：按 Windows 鍵開啟，可以放背景照片、調整濃度和模糊
// （Windows 不允許其他程式在原本的開始功能表上加東西，所以做一個可以完全自訂的來代替）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Windows.Automation;

namespace DesktopColorSorter
{
    // ======================= Win32 / COM =======================

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare([MarshalAs(UnmanagedType.Interface)] IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem rgelt, out uint pceltFetched);
        void Skip(uint celt);
        void Reset();
        void Clone([MarshalAs(UnmanagedType.Interface)] out IEnumShellItems ppenum);
    }

    [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; public uint lPrivate; }

    static partial class Native
    {
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int nCode, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, string l);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [DllImport("secur32.dll", CharSet = CharSet.Unicode)] public static extern bool GetUserNameEx(int format, StringBuilder name, ref uint size);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    }

    // ======================= Windows 鍵攔截 =======================

    // 單獨按一下 Windows 鍵（沒有搭配其他鍵）時開啟自訂開始功能表；Win+E、Win+D 等組合鍵完全不受影響。
    // 鍵盤掛勾放在自己的執行緒，主視窗忙碌時也不會讓整台電腦的鍵盤卡住。
    // 用滑鼠點工作列上的開始按鈕、按 Ctrl+Esc 也改開自訂的（直接攔下來，原本的根本不會打開）。
    // 開始按鈕的位置用 UI 自動化查（Windows 11 的開始按鈕不是獨立視窗），每 3 秒更新一次。
    sealed class WinKeyHook : IDisposable
    {
        const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MASK = 0xE8, VK_ESCAPE = 0x1B; // 0xE8 = 沒有用途的按鍵
        const uint LLKHF_INJECTED = 0x10, WM_QUIT = 0x12;
        static readonly IntPtr SelfTag = (IntPtr)0x44435344; // 標記「這是我們自己送出的按鍵」
        readonly Native.LowLevelKeyboardProc proc, mouseProc;
        readonly Native.WinEventDelegate foregroundProc;
        IntPtr mouseHook;
        Thread finder;
        volatile bool running;
        volatile int finderGen;
        volatile Rectangle[] startButtons = new Rectangle[0];
        bool swallowUp, ctrlDown, escSwallowed; // 只在掛勾執行緒上讀寫
        readonly Action onTap, onRealStart;
        Thread thread;
        uint threadId;
        IntPtr hook, winEvent;
        // 以下只在掛勾執行緒上讀寫
        bool winDown, otherKey, lastDown;
        uint lastVk;
        uint startHostPid, searchHostPid;
        public volatile bool ReplaceRealStart = true;

        public WinKeyHook(Action onTap, Action onRealStart)
        {
            this.onTap = onTap;
            this.onRealStart = onRealStart;
            proc = Callback;               // 必須留住委派，避免被回收
            mouseProc = MouseCallback;
            foregroundProc = OnForeground;
        }

        public bool Installed { get { return hook != IntPtr.Zero; } }

        public void Start()
        {
            if (thread != null) return;
            ManualResetEvent ready = new ManualResetEvent(false);
            thread = new Thread(delegate ()
            {
                threadId = Native.GetCurrentThreadId();
                winDown = otherKey = lastDown = swallowUp = ctrlDown = escSwallowed = false;
                hook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
                mouseHook = Native.SetWindowsHookEx(WH_MOUSE_LL, mouseProc, Native.GetModuleHandle(null), 0);
                winEvent = Native.SetWinEventHook(0x0003 /*EVENT_SYSTEM_FOREGROUND*/, 0x0003, IntPtr.Zero, foregroundProc, 0, 0, 0x0002 /*OUTOFCONTEXT|SKIPOWNPROCESS*/);
                ready.Set();
                MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
                if (winEvent != IntPtr.Zero) { Native.UnhookWinEvent(winEvent); winEvent = IntPtr.Zero; }
                if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
                if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            });
            thread.IsBackground = true;
            thread.Name = "WinKeyHook";
            thread.Start();
            ready.WaitOne(3000);

            // 另一條執行緒定期找開始按鈕的位置（UI 自動化比較慢，不能放在掛勾執行緒）
            running = true;
            int gen = ++finderGen;
            finder = new Thread(delegate ()
            {
                while (running && finderGen == gen)
                {
                    Rectangle[] found = FindStartButtons();
                    if (found.Length > 0 || startButtons.Length == 0) startButtons = found;
                    for (int i = 0; i < 30 && running && finderGen == gen; i++)
                    {
                        Thread.Sleep(100);
                        if (i >= 2 && CursorOnTaskbar()) break;
                    }
                }
            });
            finder.IsBackground = true;
            finder.Name = "StartButtonFinder";
            finder.Start();
        }

        internal static Rectangle[] FindStartButtons()
        {
            List<Rectangle> list = new List<Rectangle>();
            foreach (string cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
                for (IntPtr w = IntPtr.Zero; ; )
                {
                    w = Native.FindWindowEx(IntPtr.Zero, w, cls, null);
                    if (w == IntPtr.Zero) break;
                    try
                    {
                        AutomationElement bar = AutomationElement.FromHandle(w);
                        AutomationElement b = bar.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"));
                        if (b == null) continue;
                        System.Windows.Rect r = b.Current.BoundingRectangle;
                        if (!r.IsEmpty && r.Width > 0 && r.Height > 0) list.Add(new Rectangle((int)r.X, (int)r.Y, (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height)));
                    }
                    catch { }
                }
            return list.ToArray();
        }

        static bool KeyHeld(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        bool OnStartButton(POINT p)
        {
            bool hit = false;
            foreach (Rectangle r in startButtons) if (r.Contains(p.X, p.Y)) { hit = true; break; }
            if (!hit) return false;
            // 全螢幕遊戲 / 影片蓋住工作列、自動隱藏的工作列滑走了、檔案總管重新啟動中…這些時候位置還在但點到的不是開始按鈕
            string c = Native.ClassName(Native.GetAncestor(Native.WindowFromPoint(p), 2 /*GA_ROOT*/));
            return c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
        }

        static bool CursorOnTaskbar()
        {
            Point p = Cursor.Position;
            foreach (string cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
                for (IntPtr w = IntPtr.Zero; ; )
                {
                    w = Native.FindWindowEx(IntPtr.Zero, w, cls, null);
                    if (w == IntPtr.Zero) break;
                    RECT r;
                    if (Native.GetWindowRect(w, out r) && r.ToRectangle().Contains(p)) return true;
                }
            return false;
        }

        // 左鍵點在開始按鈕上：按下和放開都吃掉（原本的開始功能表就不會打開），放開時開 / 關自訂的
        IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x0201 /*WM_LBUTTONDOWN*/ || msg == 0x0202 /*WM_LBUTTONUP*/)
                {
                    MSLLHOOKSTRUCT m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    bool injected = (m.flags & 0x1 /*LLMHF_INJECTED*/) != 0;
                    if (!injected)
                    {
                        if (msg == 0x0201)
                        {
                            swallowUp = ReplaceRealStart && OnStartButton(m.pt);
                            if (swallowUp) return (IntPtr)1;
                        }
                        else if (swallowUp)
                        {
                            swallowUp = false;
                            try { onTap(); } catch { }
                            return (IntPtr)1;
                        }
                    }
                }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        public void Stop()
        {
            running = false;
            if (thread == null) return;
            Native.PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(2000);
            thread = null;
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                bool injected = (k.flags & LLKHF_INJECTED) != 0;
                if (!(injected && k.dwExtraInfo == SelfTag)) // 只略過我們自己送出的按鍵
                {
                    int msg = wParam.ToInt32();
                    bool down = msg == 0x0100 || msg == 0x0104; // WM_KEYDOWN / WM_SYSKEYDOWN
                    bool isWin = k.vkCode == VK_LWIN || k.vkCode == VK_RWIN;
                    // 「上一個實體按鍵事件就是 Win 按下」才算自動重複；Win+L、安全桌面等情況會漏掉 Win 放開，要重新計算
                    bool prevWinDown = lastDown && (lastVk == VK_LWIN || lastVk == VK_RWIN);
                    if (!injected) { lastVk = k.vkCode; lastDown = down; }
                    // Ctrl+Esc（Windows 開「開始」的快速鍵）也改開自訂的
                    if (!injected && k.vkCode == VK_ESCAPE && ReplaceRealStart)
                    {
                        if (down && !escSwallowed && !winDown && KeyHeld(0x11 /*Ctrl*/) && !KeyHeld(0x10 /*Shift*/) && !KeyHeld(0x12 /*Alt*/))
                        { escSwallowed = true; try { onTap(); } catch { } return (IntPtr)1; }
                        if (escSwallowed) { if (!down) escSwallowed = false; return (IntPtr)1; } // 按住不放的自動重複也吃掉
                    }
                    if (isWin)
                    {
                        if (!injected)
                        {
                            if (down)
                            {
                                if (!(winDown && prevWinDown)) { winDown = true; otherKey = false; }
                            }
                            else
                            {
                                bool tap = winDown && !otherKey;
                                winDown = false;
                                // 原本的開始功能表或搜尋正開著時，讓 Windows 自己處理（關掉它）
                                if (tap && !ShellFlyoutForeground())
                                {
                                    // 在放開 Windows 鍵之前插入一個沒有作用的按鍵，Windows 就會當成組合鍵、不開原本的開始功能表；
                                    // 原本的「放開」事件吃掉，改由我們在無作用按鍵之後送出
                                    INPUT[] seq = { Key(VK_MASK, false), Key(VK_MASK, true), Key((int)k.vkCode, true) };
                                    uint sent = Native.SendInput((uint)seq.Length, seq, Marshal.SizeOf(typeof(INPUT)));
                                    if (sent == (uint)seq.Length)
                                    {
                                        try { onTap(); } catch { }
                                        return (IntPtr)1;
                                    }
                                    // 送不出去（例如被系統擋下）：放行原本的放開事件，至少不會讓 Windows 鍵卡住
                                    if (sent == 1) { INPUT[] up = { Key(VK_MASK, true) }; Native.SendInput(1, up, Marshal.SizeOf(typeof(INPUT))); }
                                }
                            }
                        }
                    }
                    else if (winDown && down) otherKey = true; // 按著 Windows 鍵時按了其他鍵（包括其他工具送出的）= 組合鍵
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        // 原本的開始功能表被打開了 → 關掉它，改開自訂的
        void OnForeground(IntPtr h, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (!ReplaceRealStart || hwnd == IntPtr.Zero || !IsHost(hwnd, "StartMenuExperienceHost.exe", ref startHostPid)) return;
            INPUT[] esc = { Key(VK_ESCAPE, false), Key(VK_ESCAPE, true) };
            Native.SendInput((uint)esc.Length, esc, Marshal.SizeOf(typeof(INPUT)));
            try { onRealStart(); } catch { }
        }

        bool ShellFlyoutForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            return fg != IntPtr.Zero && (IsHost(fg, "StartMenuExperienceHost.exe", ref startHostPid) || IsHost(fg, "SearchHost.exe", ref searchHostPid));
        }

        // 視窗是不是某個系統程式的 CoreWindow（記住 PID，之後不用再查）
        static bool IsHost(IntPtr hwnd, string exe, ref uint cachedPid)
        {
            if (Native.ClassName(hwnd) != "Windows.UI.Core.CoreWindow") return false;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;
            if (pid == cachedPid) return true;
            IntPtr hp = Native.OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
            if (hp == IntPtr.Zero) return false;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int n = sb.Capacity;
                if (!Native.QueryFullProcessImageName(hp, 0, sb, ref n)) return false;
                if (!string.Equals(Path.GetFileName(sb.ToString()), exe, StringComparison.OrdinalIgnoreCase)) return false;
                cachedPid = pid;
                return true;
            }
            finally { Native.CloseHandle(hp); }
        }

        static INPUT Key(int vk, bool up)
        {
            INPUT i = new INPUT();
            i.type = 1; // INPUT_KEYBOARD
            i.u.ki.wVk = (ushort)vk;
            i.u.ki.dwFlags = (up ? 0x0002u : 0u) | (vk == VK_LWIN || vk == VK_RWIN ? 0x0001u : 0u); // KEYUP | EXTENDEDKEY
            i.u.ki.dwExtraInfo = SelfTag;
            return i;
        }

        public void Dispose() { Stop(); }
    }

    // ======================= 應用程式清單 =======================

    class AppEntry { public string Name, Id; public Bitmap Icon; }
    class RecentEntry { public string Name, Path; public DateTime Time; public Bitmap Icon; }

    static class AppCatalog
    {
        const uint SIGDN_NORMALDISPLAY = 0, SIGDN_PARENTRELATIVEPARSING = 0x80018001;
        const int SIIGBF_ICONONLY = 0x4;
        static readonly Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        static readonly Guid IID_IEnumShellItems = new Guid("70629033-e363-4a28-a567-0db78006e6d7");
        static readonly Guid BHID_EnumItems = new Guid("94f60519-2850-4924-aa5a-d15e84868039");
        static readonly Guid IID_ImageFactory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        // 讀取「所有應用程式」（shell:AppsFolder，傳統程式和市集 App 都在裡面）。必須在 STA 執行緒呼叫
        public static List<AppEntry> LoadApps(int iconSize)
        {
            List<AppEntry> list = new List<AppEntry>();
            Guid iid = IID_IShellItem;
            object o;
            if (Native.SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iid, out o) != 0 || o == null) return list;
            IShellItem folder = (IShellItem)o;
            IEnumShellItems en = null;
            try
            {
                Guid bhid = BHID_EnumItems, eiid = IID_IEnumShellItems;
                object eo;
                folder.BindToHandler(IntPtr.Zero, ref bhid, ref eiid, out eo);
                en = (IEnumShellItems)eo;
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IShellItem item;
                uint fetched;
                while (en.Next(1, out item, out fetched) == 0 && fetched == 1 && item != null)
                {
                    try
                    {
                        string name = (DisplayName(item, SIGDN_NORMALDISPLAY) ?? "").Trim(), id = DisplayName(item, SIGDN_PARENTRELATIVEPARSING);
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id) || IsClutter(name) || !seen.Add(id)) continue;
                        Bitmap icon;
                        try { IconLoader.FromFactory(item, iconSize, SIIGBF_ICONONLY, out icon); } catch { icon = null; }
                        list.Add(new AppEntry { Name = name, Id = id, Icon = icon });
                    }
                    catch { }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally
            {
                if (en != null) Marshal.ReleaseComObject(en);
                Marshal.ReleaseComObject(folder);
            }
            StringComparer cmp = StringComparer.Create(CultureInfo.CurrentCulture, true);
            return list.OrderBy(a => a.Name, cmp).ToList();
        }

        // 最近開啟的檔案（和 Windows 開始功能表的「建議」一樣，來自「最近使用的項目」資料夾）
        public static List<RecentEntry> LoadRecent(int max, int iconSize)
        {
            List<RecentEntry> list = new List<RecentEntry>();
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
            foreach (FileInfo f in new DirectoryInfo(dir).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(max))
                list.Add(new RecentEntry { Name = Path.GetFileNameWithoutExtension(f.Name), Path = f.FullName, Time = f.LastWriteTime, Icon = IconFor(f.FullName, iconSize) });
            return list;
        }

        static Bitmap IconFor(string path, int size)
        {
            Guid iid = IID_ImageFactory;
            object o;
            if (Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out o) != 0 || o == null) return null;
            try { Bitmap b; IconLoader.FromFactory(o, size, SIIGBF_ICONONLY, out b); return b; }
            catch { return null; }
            finally { Marshal.ReleaseComObject(o); }
        }

        static string DisplayName(IShellItem item, uint sigdn)
        {
            IntPtr p;
            item.GetDisplayName(sigdn, out p);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Native.CoTaskMemFree(p); }
        }

        // 解除安裝程式之類的捷徑不放進清單
        static bool IsClutter(string name)
        {
            return name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("解除安裝") || name.Contains("卸載");
        }

        public static void RunSta(Action work)
        {
            Thread t = new Thread(delegate () { try { work(); } catch { } });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        public static void Launch(AppEntry a)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"shell:AppsFolder\\" + a.Id + "\"") { UseShellExecute = false });
        }

        // 捷徑 (.lnk) 指向的真正檔案；找不到就回傳 null
        public static string LinkTarget(string lnk)
        {
            object sh = null, sc = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                sh = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                string p = (string)sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                return string.IsNullOrEmpty(p) ? null : p;
            }
            catch { return null; }
            finally
            {
                if (sc != null) Marshal.ReleaseComObject(sc);
                if (sh != null) Marshal.ReleaseComObject(sh);
            }
        }

        public static void Open(string target)
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }

    static class UserInfo
    {
        public static string DisplayName()
        {
            string custom = AppSettings.Get("StartUserName", "").Trim();
            if (custom.Length > 0) return custom;
            try
            {
                StringBuilder sb = new StringBuilder(256);
                uint n = (uint)sb.Capacity;
                if (Native.GetUserNameEx(3 /*NameDisplay*/, sb, ref n) && sb.Length > 0) return sb.ToString();
            }
            catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI"))
                {
                    string s = k == null ? null : k.GetValue("LastLoggedOnDisplayName") as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return Environment.UserName;
        }

        public static Bitmap Picture()
        {
            try
            {
                string sid = WindowsIdentity.GetCurrent().User.Value;
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid))
                {
                    if (k == null) return null;
                    foreach (string n in new[] { "Image192", "Image240", "Image96", "Image448" })
                    {
                        string p = k.GetValue(n) as string;
                        if (!string.IsNullOrEmpty(p) && File.Exists(p))
                            using (Image img = Image.FromFile(p)) return new Bitmap(img, 96, 96);
                    }
                }
            }
            catch { }
            return null;
        }
    }

    // ======================= 開始功能表設定 =======================

    class StartStyle
    {
        public Bitmap Photo;
        public RectangleF Crop;     // 照片框選的範圍（0~1）；空的就置中
        public int Strength = 75;   // 照片濃度 10~100
        public int Blur = 3;        // 模糊 0~10
        public bool Dark = true;
        public bool Large;
        public bool ShowRecent = true;
        public bool Animate = true;
    }
}
