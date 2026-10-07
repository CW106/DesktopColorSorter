// 桌面顏色整理器
//  - 依圖示主色把 Windows 桌面上的圖示排成彩虹順序
//  - 像素圖案：用桌面上的應用程式圖示排出愛心、星星、文字或照片
//  - 一鍵隱藏 / 顯示全部桌面圖示（Ctrl+Alt+H）
// 編譯：build.bat（使用 Windows 內建的 .NET Framework csc，不需安裝任何東西）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopColorSorter
{
    // ======================= Win32 / COM 介面 =======================

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; public SIZE(int w, int h) { cx = w; cy = h; } }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int pad0, pad1, pad2, pad3; // 保留給色彩表，避免 GetDIBits 越界寫入
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface IShellWindows
    {
        void _get_Count(); void _Item(); void _NewEnum(); void _Register(); void _RegisterPending();
        void _Revoke(); void _OnNavigate(); void _OnActivated();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object pvarLoc, ref object pvarLocRoot, int swClass, out int phwnd, int swfwOptions);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IComServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid guidService, ref Guid riid);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        void GetWindow(out IntPtr phwnd); void ContextSensitiveHelp(int f);
        void _InsertMenusSB(); void _SetMenuSB(); void _RemoveMenusSB(); void _SetStatusTextSB(); void _EnableModelessSB();
        void _TranslateAcceleratorSB(); void _BrowseObject(); void _GetViewStateStream(); void _GetControlWindow(); void _SendControlMsg();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryActiveShellView();
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleWindow { void GetWindow(out IntPtr phwnd); void ContextSensitiveHelp(int f); }

    [ComImport, Guid("1af3a467-214f-4298-908e-06b03e0b39f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFolderView2
    {
        // IFolderView
        void GetCurrentViewMode(out uint mode);
        void SetCurrentViewMode(uint mode);
        void GetFolder(ref Guid riid, out IntPtr ppv);
        void Item(int index, out IntPtr ppidl);
        void ItemCount(uint flags, out int count);
        void Items(uint flags, ref Guid riid, out IntPtr ppv);
        void GetSelectionMarkedItem(out int item);
        void GetFocusedItem(out int item);
        void GetItemPosition(IntPtr pidl, out POINT pt);
        void GetSpacing(out POINT pt);
        void GetDefaultSpacing(out POINT pt);
        [PreserveSig] int GetAutoArrange();
        void SelectItem(int item, uint flags);
        void SelectAndPositionItems(uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            [MarshalAs(UnmanagedType.LPArray)] POINT[] apt, uint flags);
        // IFolderView2
        void _SetGroupBy(); void _GetGroupBy(); void _SetViewProperty(); void _GetViewProperty();
        void _SetTileViewProperties(); void _SetExtendedTileViewProperties(); void _SetText();
        void SetCurrentFolderFlags(uint mask, uint flags);
        void GetCurrentFolderFlags(out uint flags);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    static partial class Native
    {
        [DllImport("shell32.dll")] public static extern int SHCreateItemFromIDList(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [DllImport("shell32.dll")] public static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdn, out IntPtr ppszName);
        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);
        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] public static extern int GetObjectBitmap(IntPtr h, int c, out BITMAP bm);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFO bmi, uint usage);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    // ======================= 桌面存取 =======================

    class DesktopItem
    {
        public IntPtr Pidl;
        public int Index;   // 在桌面清單（SysListView32）裡的位置
        public string Name, ParsingName;
        public POINT Pos;
        public Bitmap Icon;
        public ColorInfo Color;
    }

    sealed class Desktop : IDisposable
    {
        const uint SVGIO_ALLVIEW = 0x2;
        const uint SVSI_POSITIONITEM = 0x80;
        public const uint FWF_AUTOARRANGE = 0x1;
        public const uint FWF_SNAPTOGRID = 0x4;
        const uint FWF_NOICONS = 0x1000;

        public uint Flags { get { uint f; View.GetCurrentFolderFlags(out f); return f; } }
        public void SetFlags(uint mask, uint value) { View.SetCurrentFolderFlags(mask, value); }

        // 直接移動（不動資料夾設定），給動畫用
        // 直接移動桌面清單裡的圖示（立刻重畫）。用在動畫：Windows 11 的 SelectAndPositionItems 在位置一直變的時候會延後才畫
        public void MoveFast(int[] indices, POINT[] pts)
        {
            for (int i = 0; i < indices.Length; i++)
                Native.PostMessage(ListView, 0x100F /*LVM_SETITEMPOSITION*/, (IntPtr)indices[i], (IntPtr)(((pts[i].Y & 0xFFFF) << 16) | (pts[i].X & 0xFFFF)));
        }

        public void MoveRaw(IntPtr[] pidls, POINT[] pts)
        {
            if (pidls.Length > 0) View.SelectAndPositionItems((uint)pidls.Length, pidls, pts, SVSI_POSITIONITEM);
        }

        public IFolderView2 View;
        public IntPtr ListView;
        public POINT Spacing;
        public List<DesktopItem> Items = new List<DesktopItem>();

        public static Desktop Open()
        {
            Type t = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            IShellWindows sw = (IShellWindows)Activator.CreateInstance(t);
            object loc = 0;      // CSIDL_DESKTOP
            object root = null;
            int hwnd;
            object disp = sw.FindWindowSW(ref loc, ref root, 8 /*SWC_DESKTOP*/, out hwnd, 1 /*SWFO_NEEDDISPATCH*/);
            if (disp == null) throw new InvalidOperationException("找不到桌面視窗（Explorer 是否正在執行？）");

            Guid sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
            Guid iid = new Guid("000214E2-0000-0000-C000-000000000046"); // IID_IShellBrowser
            IShellBrowser browser = (IShellBrowser)((IComServiceProvider)disp).QueryService(ref sid, ref iid);
            object shellView = browser.QueryActiveShellView();

            Desktop d = new Desktop();
            d.View = (IFolderView2)shellView;
            d.ListView = FindDesktopListView((IntPtr)hwnd);
            d.View.GetSpacing(out d.Spacing);
            if (d.Spacing.X <= 0 || d.Spacing.Y <= 0) d.View.GetDefaultSpacing(out d.Spacing);
            return d;
        }

        // 桌面圖示所在的 SysListView32：通常在 Progman 底下，換桌布動畫後可能被移到某個 WorkerW 底下
        static IntPtr FindDesktopListView(IntPtr desktopHwnd)
        {
            IntPtr defView = IntPtr.Zero;
            if (desktopHwnd != IntPtr.Zero) defView = Native.FindWindowEx(desktopHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
                defView = Native.FindWindowEx(Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            for (IntPtr w = IntPtr.Zero; defView == IntPtr.Zero; )
            {
                w = Native.FindWindowEx(IntPtr.Zero, w, "WorkerW", null);
                if (w == IntPtr.Zero) break;
                defView = Native.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            }
            if (defView == IntPtr.Zero) return desktopHwnd;
            IntPtr lv = Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            return lv != IntPtr.Zero ? lv : defView;
        }

        public void LoadItems(bool withIcons)
        {
            FreeItems();
            int count;
            View.ItemCount(SVGIO_ALLVIEW, out count);
            for (int i = 0; i < count; i++)
            {
                IntPtr pidl;
                View.Item(i, out pidl);
                DesktopItem it = new DesktopItem();
                it.Pidl = pidl;
                it.Index = i;
                View.GetItemPosition(pidl, out it.Pos);
                it.Name = GetName(pidl, 0) ?? "?";                       // SIGDN_NORMALDISPLAY
                it.ParsingName = GetName(pidl, 0x80028000) ?? it.Name;  // SIGDN_DESKTOPABSOLUTEPARSING
                if (withIcons)
                {
                    byte[] px = IconLoader.Load(pidl, 48, out it.Icon);
                    it.Color = ColorAnalyzer.Analyze(px);
                }
                Items.Add(it);
            }
        }

        public bool AutoArrangeOn { get { return View.GetAutoArrange() == 0; } }

        public bool IconsHidden
        {
            get { uint f; View.GetCurrentFolderFlags(out f); return (f & FWF_NOICONS) != 0; }
            set { View.SetCurrentFolderFlags(FWF_NOICONS, value ? FWF_NOICONS : 0); }
        }

        // 把指定圖示放到指定座標；會關閉「自動排列圖示」（否則 Windows 會立刻把它們排回去）
        public void Position(IList<DesktopItem> items, IList<POINT> pts)
        {
            if (items.Count == 0) return;
            uint flags;
            View.GetCurrentFolderFlags(out flags);
            uint toClear = flags & (FWF_AUTOARRANGE | FWF_SNAPTOGRID);
            if (toClear != 0) View.SetCurrentFolderFlags(toClear, 0);
            View.SelectAndPositionItems((uint)items.Count, items.Select(x => x.Pidl).ToArray(), pts.ToArray(), SVSI_POSITIONITEM);
            if ((flags & FWF_SNAPTOGRID) != 0) View.SetCurrentFolderFlags(FWF_SNAPTOGRID, FWF_SNAPTOGRID);
        }

        // 主螢幕工作區（扣掉工作列）在桌面清單檢視中的座標
        public Rectangle WorkAreaClient()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            POINT tl = new POINT(wa.Left, wa.Top);
            Native.ScreenToClient(ListView, ref tl);
            return new Rectangle(tl.X, tl.Y, wa.Width, wa.Height);
        }

        static string GetName(IntPtr pidl, uint sigdn)
        {
            IntPtr p;
            if (Native.SHGetNameFromIDList(pidl, sigdn, out p) != 0 || p == IntPtr.Zero) return null;
            string s = Marshal.PtrToStringUni(p);
            Native.CoTaskMemFree(p);
            return s;
        }

        void FreeItems()
        {
            foreach (DesktopItem it in Items)
                if (it.Pidl != IntPtr.Zero) { Native.CoTaskMemFree(it.Pidl); it.Pidl = IntPtr.Zero; }
            Items.Clear();
        }

        public void Dispose()
        {
            FreeItems();
            if (View != null) { Marshal.ReleaseComObject(View); View = null; }
        }
    }

    // 某一刻的桌面狀態（圖示清單 + 格線資訊），供預覽與排列計算使用
    class Snapshot
    {
        public List<DesktopItem> Items;
        public POINT Spacing, Origin;
        public Rectangle Area;
        public int GridCols, GridRows;

        public static Snapshot Take(Desktop d)
        {
            Snapshot s = new Snapshot();
            s.Items = new List<DesktopItem>(d.Items);
            s.Spacing = d.Spacing;
            s.Area = d.WorkAreaClient();
            s.Origin = ColorLayout.GridOrigin(s.Items, s.Area, s.Spacing);
            s.GridCols = Math.Max(1, (s.Area.Right - s.Origin.X) / s.Spacing.X);
            s.GridRows = Math.Max(1, (s.Area.Bottom - s.Origin.Y) / s.Spacing.Y);
            return s;
        }

        public POINT ToPoint(Point cell) { return new POINT(Origin.X + cell.X * Spacing.X, Origin.Y + cell.Y * Spacing.Y); }
    }

    static class IconLoader
    {
        const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

        // 回傳 BGRA（預乘 alpha）像素，並輸出可顯示的 Bitmap
        public static byte[] Load(IntPtr pidl, int size, out Bitmap bmp)
        {
            bmp = null;
            Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            object o;
            if (Native.SHCreateItemFromIDList(pidl, ref iid, out o) != 0 || o == null) return null;
            try { return FromFactory(o, size, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out bmp); }
            finally { Marshal.ReleaseComObject(o); }
        }

        // o 是任何支援 IShellItemImageFactory 的殼層物件（IShellItem 也可以）
        public static byte[] FromFactory(object o, int size, int flags, out Bitmap bmp)
        {
            bmp = null;
            IntPtr hbm;
            int hr = ((IShellItemImageFactory)o).GetImage(new SIZE(size, size), flags, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                BITMAP bm;
                Native.GetObjectBitmap(hbm, Marshal.SizeOf(typeof(BITMAP)), out bm);
                int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
                if (w <= 0 || h <= 0) return null;
                BITMAPINFO bi = new BITMAPINFO();
                bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
                byte[] px = new byte[w * h * 4];
                IntPtr dc = Native.GetDC(IntPtr.Zero);
                Native.GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0);
                Native.ReleaseDC(IntPtr.Zero, dc);

                bool anyAlpha = false;
                for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
                if (!anyAlpha) for (int i = 3; i < px.Length; i += 4) px[i] = 255;

                bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
                bmp.UnlockBits(data);
                return px;
            }
            finally { Native.DeleteObject(hbm); }
        }
    }

    // ======================= 顏色分析 =======================

    class ColorInfo
    {
        public bool Neutral;
        public double Hue, Sat, Val, Light;
        public int Group;
        public Color Swatch;   // 主色
        public Color Avg;      // 平均色
        public Color Match;    // 拼圖案時用來比對的代表色
        public double SortKey;
    }

    static class ColorAnalyzer
    {
        public static readonly string[] GroupNames = { "紅色系", "橙色系", "黃色系", "綠色系", "青色系", "藍色系", "紫色系", "粉色系", "黑白灰" };
        public static readonly Color[] GroupColors = {
            Color.FromArgb(226, 60, 60), Color.FromArgb(242, 140, 40), Color.FromArgb(236, 196, 30),
            Color.FromArgb(56, 168, 82), Color.FromArgb(32, 178, 196), Color.FromArgb(52, 108, 226),
            Color.FromArgb(140, 76, 210), Color.FromArgb(230, 90, 170), Color.FromArgb(140, 140, 140) };
        public const int NeutralGroup = 8;

        public static int GroupOf(double h)
        {
            if (h >= 345 || h < 15) return 0;
            if (h < 40) return 1;
            if (h < 70) return 2;
            if (h < 165) return 3;
            if (h < 200) return 4;
            if (h < 260) return 5;
            if (h < 300) return 6;
            return 7;
        }

        public static ColorInfo Analyze(byte[] px)
        {
            ColorInfo ci = new ColorInfo();
            double[] w = new double[36], cs = new double[36], sn = new double[36], ss = new double[36], vs = new double[36];
            int opaque = 0, chroma = 0;
            double lum = 0, rs = 0, gs = 0, bs = 0;
            if (px != null)
            {
                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    int a = px[i + 3];
                    if (a < 128) continue;
                    double b = Math.Min(255, px[i] * 255.0 / a);
                    double g = Math.Min(255, px[i + 1] * 255.0 / a);
                    double r = Math.Min(255, px[i + 2] * 255.0 / a);
                    opaque++;
                    rs += r; gs += g; bs += b;
                    lum += (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                    double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    double v = max / 255.0, s = max <= 0 ? 0 : (max - min) / max;
                    if (s < 0.25 || v < 0.2) continue; // 太灰或太暗的像素不算「有顏色」
                    chroma++;
                    double hue = HueOf(r, g, b, max, min);
                    double wt = s * v;
                    int bin = ((int)(hue / 10)) % 36;
                    double rad = hue * Math.PI / 180;
                    w[bin] += wt; cs[bin] += Math.Cos(rad) * wt; sn[bin] += Math.Sin(rad) * wt;
                    ss[bin] += s * wt; vs[bin] += v * wt;
                }
            }
            ci.Light = opaque > 0 ? lum / opaque : 0.5;
            ci.Avg = opaque > 0 ? Color.FromArgb((int)(rs / opaque), (int)(gs / opaque), (int)(bs / opaque)) : Color.Gray;

            if (opaque == 0 || chroma < opaque * 0.12)
            {
                ci.Neutral = true;
                ci.Group = NeutralGroup;
                int gray = (int)Math.Round(ci.Light * 255);
                ci.Swatch = Color.FromArgb(gray, gray, gray);
                ci.SortKey = NeutralGroup * 1000 + (1 - ci.Light) * 100; // 由亮到暗
            }
            else
            {
                // 找出最主要的色相區間（連同左右相鄰區間一起看，避免剛好卡在邊界）
                int best = 0; double bestW = -1;
                for (int k = 0; k < 36; k++)
                {
                    double sum = w[(k + 35) % 36] + w[k] + w[(k + 1) % 36];
                    if (sum > bestW) { bestW = sum; best = k; }
                }
                double sw = 0, sc = 0, ssn = 0, sat = 0, val = 0;
                for (int d = -1; d <= 1; d++)
                {
                    int k = (best + d + 36) % 36;
                    sw += w[k]; sc += cs[k]; ssn += sn[k]; sat += ss[k]; val += vs[k];
                }
                double h = Math.Atan2(ssn, sc) * 180 / Math.PI;
                if (h < 0) h += 360;
                ci.Hue = h; ci.Sat = sat / sw; ci.Val = val / sw;
                ci.Group = GroupOf(h);
                ci.Swatch = FromHsv(h, ci.Sat, ci.Val);
                ci.SortKey = ci.Group * 1000 + (h + 15) % 360;
            }
            ci.Match = Color.FromArgb((ci.Avg.R + ci.Swatch.R) / 2, (ci.Avg.G + ci.Swatch.G) / 2, (ci.Avg.B + ci.Swatch.B) / 2);
            return ci;
        }

        static double HueOf(double r, double g, double b, double max, double min)
        {
            if (max == min) return 0;
            double d = max - min, h;
            if (max == r) h = (g - b) / d;
            else if (max == g) h = 2 + (b - r) / d;
            else h = 4 + (r - g) / d;
            h *= 60;
            return h < 0 ? h + 360 : h;
        }

        public static Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        // 接近人眼感受的 RGB 距離（redmean）
        public static double Distance(Color a, Color b)
        {
            double rm = (a.R + b.R) / 2.0, dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db);
        }
    }

    // ======================= 顏色排列 =======================

    static class ColorLayout
    {
        public static List<DesktopItem> Sort(IEnumerable<DesktopItem> items)
        {
            return items.OrderBy(x => x.Color.SortKey).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static int Mod(int a, int m) { return ((a % m) + m) % m; }

        // 沿用 Windows 現有的格線偏移（大多數圖示對齊的位置），讓排好的圖示和原本格線一致
        public static POINT GridOrigin(List<DesktopItem> items, Rectangle area, POINT sp)
        {
            Dictionary<long, int> counts = new Dictionary<long, int>();
            long bestKey = 0; int bestCount = 0;
            foreach (DesktopItem it in items)
            {
                int mx = Mod(it.Pos.X - area.X, sp.X), my = Mod(it.Pos.Y - area.Y, sp.Y);
                long key = ((long)mx << 32) | (uint)my;
                int c; counts.TryGetValue(key, out c); counts[key] = ++c;
                if (c > bestCount) { bestCount = c; bestKey = key; }
            }
            int ox = 0, oy = 0;
            if (items.Count > 0 && bestCount * 2 >= items.Count)
            {
                ox = (int)(bestKey >> 32); oy = (int)(bestKey & 0xFFFFFFFF);
            }
            return new POINT(area.X + ox, area.Y + oy);
        }

        public static List<POINT> Plan(List<DesktopItem> sorted, Snapshot s, bool vertical, bool groupBreak)
        {
            List<POINT> pts = new List<POINT>();
            int row = 0, col = 0, prevGroup = -1;
            foreach (DesktopItem it in sorted)
            {
                if (groupBreak && prevGroup != -1 && it.Color.Group != prevGroup)
                {
                    if (vertical) { if (row != 0) { col++; row = 0; } }
                    else { if (col != 0) { row++; col = 0; } }
                }
                pts.Add(s.ToPoint(new Point(col, row)));
                if (vertical) { if (++row >= s.GridRows) { row = 0; col++; } }
                else { if (++col >= s.GridCols) { col = 0; row++; } }
                prevGroup = it.Color.Group;
            }
            return pts;
        }
    }

    // ======================= 像素圖案 =======================

    class PatternSource : IDisposable
    {
        public Bitmap Image;
        public bool Colorful;      // true：依圖片顏色挑圖示；false：只用形狀，圖示排成彩虹漸層
        public int BaseCols;       // 點陣字：只用整數倍放大，筆畫才會清楚
        public bool OwnsImage = true;
        public void Dispose() { if (OwnsImage && Image != null) Image.Dispose(); }
    }

    class PatternPlan
    {
        public int Cols, Rows, Used;
        public List<DesktopItem> Items = new List<DesktopItem>();
        public List<Point> Cells = new List<Point>(); // 桌面格線座標；前 Used 個是圖案，其餘是放到角落的
    }

    static class Pattern
    {
        public static PatternPlan Plan(Snapshot s, PatternSource src, bool removeBg)
        {
            PatternPlan plan = new PatternPlan();
            int n = s.Items.Count;
            if (n == 0 || src == null || src.Image == null) return plan;
            bool hasAlpha;
            using (Bitmap work = Prepare(src.Image, out hasAlpha))
            {
                if (work == null) return plan;
                bool useBg = removeBg && !hasAlpha;
                Color bg = useBg ? BorderColor(work) : Color.Empty;
                int sx = s.Spacing.X, sy = s.Spacing.Y;

                // 由大到小嘗試格數，選「需要的圖示數 <= 桌面圖示數」中最大（最精細）的一個
                List<int> cands = new List<int>();
                if (src.BaseCols > 0)
                    for (int k = s.GridCols / src.BaseCols; k >= 1; k--) cands.Add(k * src.BaseCols);
                for (int c = s.GridCols; c >= 1; c--) if (!cands.Contains(c)) cands.Add(c);

                double[] frac = null; Color[] col = null; int cols = 0, rows = 0;
                foreach (int c in cands)
                {
                    int r = Math.Max(1, (int)Math.Round(c * (double)sx * work.Height / ((double)work.Width * sy)));
                    if (r > s.GridRows) continue;
                    double[] f; Color[] cc;
                    Sample(work, c, r, sx, sy, useBg, bg, out f, out cc);
                    int count = f.Count(x => x >= 0.5);
                    if (count > 0 && count <= n) { frac = f; col = cc; cols = c; rows = r; break; }
                }
                if (frac == null)
                {
                    cols = 1;
                    rows = Math.Min(s.GridRows, Math.Max(1, (int)Math.Round((double)sx * work.Height / ((double)work.Width * sy))));
                    Sample(work, cols, rows, sx, sy, useBg, bg, out frac, out col);
                }

                int total = cols * rows;
                List<int> chosen = Enumerable.Range(0, total).Where(i => frac[i] >= 0.5).ToList();
                if (chosen.Count > n) chosen = chosen.OrderByDescending(i => frac[i]).Take(n).ToList();
                int m = chosen.Count;
                List<DesktopItem> pool = s.Items;

                int[] assign = new int[m]; // assign[k] = 第 k 個格子用 pool 裡的哪個圖示
                if (src.Colorful)
                {
                    double[,] cost = new double[m, n];
                    for (int k = 0; k < m; k++)
                        for (int j = 0; j < n; j++)
                            cost[k, j] = ColorAnalyzer.Distance(col[chosen[k]], pool[j].Color.Match);
                    if (m > 0) assign = Hungarian(cost);
                }
                else
                {
                    // 挑最鮮豔的圖示，依色相由左到右排 → 彩虹漸層
                    List<int> pick = Enumerable.Range(0, n)
                        .OrderBy(j => pool[j].Color.Neutral ? 1 : 0).ThenByDescending(j => pool[j].Color.Sat)
                        .Take(m).OrderBy(j => pool[j].Color.SortKey).ToList();
                    List<int> order = Enumerable.Range(0, m).OrderBy(k => chosen[k] % cols).ThenBy(k => chosen[k] / cols).ToList();
                    for (int t = 0; t < m; t++) assign[order[t]] = pick[t];
                }

                int offC = (s.GridCols - cols) / 2, offR = (s.GridRows - rows) / 2;
                bool[,] occ = new bool[s.GridCols, s.GridRows];
                bool[] used = new bool[n];
                for (int k = 0; k < m; k++)
                {
                    Point cell = new Point(offC + chosen[k] % cols, offR + chosen[k] / cols);
                    plan.Items.Add(pool[assign[k]]);
                    plan.Cells.Add(cell);
                    occ[cell.X, cell.Y] = true;
                    used[assign[k]] = true;
                }
                plan.Used = m; plan.Cols = cols; plan.Rows = rows;

                // 用不到的圖示：從右下角往上、往左依序放，避開圖案
                int cx = s.GridCols - 1, cy = s.GridRows - 1, overflow = 0;
                foreach (int j in Enumerable.Range(0, n).Where(j => !used[j]).OrderBy(j => pool[j].Color.SortKey))
                {
                    while (cx >= 0 && occ[cx, cy]) { if (--cy < 0) { cy = s.GridRows - 1; cx--; } }
                    Point cell;
                    if (cx >= 0) { cell = new Point(cx, cy); occ[cx, cy] = true; }
                    else { cell = new Point(s.GridCols + overflow / s.GridRows, overflow % s.GridRows); overflow++; }
                    plan.Items.Add(pool[j]);
                    plan.Cells.Add(cell);
                }
            }
            return plan;
        }

        // 縮小、轉成 32bpp，並判斷是否有透明背景（有的話裁掉透明邊）
        static Bitmap Prepare(Bitmap src, out bool hasAlpha)
        {
            hasAlpha = false;
            if (src.Width < 1 || src.Height < 1) return null;
            double k = Math.Min(1.0, 512.0 / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)Math.Round(src.Width * k)), h = Math.Max(1, (int)Math.Round(src.Height * k));
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            using (ImageAttributes ia = new ImageAttributes())
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            byte[] px = ReadPixels(b);
            int minX = w, minY = h, maxX = -1, maxY = -1, clear = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[(y * w + x) * 4 + 3] < 128) { clear++; continue; }
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            if (maxX < 0) { b.Dispose(); return null; }
            hasAlpha = clear > w * h / 100;
            if (hasAlpha && (minX > 0 || minY > 0 || maxX < w - 1 || maxY < h - 1))
            {
                Bitmap c = b.Clone(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), PixelFormat.Format32bppArgb);
                b.Dispose();
                return c;
            }
            return b;
        }

        // 把圖縮放成 c×r 個桌面格子，算出每格「被主體覆蓋的比例」和平均顏色
        static void Sample(Bitmap work, int c, int r, int sx, int sy, bool useBg, Color bg, out double[] frac, out Color[] col)
        {
            int pw = 8, ph = Math.Max(1, (int)Math.Round(8.0 * sy / sx));
            int W = c * pw, H = r * ph;
            frac = new double[c * r];
            col = new Color[c * r];
            byte[] px;
            using (Bitmap canvas = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    double k = Math.Min((double)W / work.Width, (double)H / work.Height);
                    // 用浮點座標置中，左右才會對稱（整數取整會讓愛心之類的圖案歪一邊）
                    float dw = (float)(work.Width * k), dh = (float)(work.Height * k), dx = (W - dw) / 2, dy = (H - dh) / 2;
                    PointF[] dest = { new PointF(dx, dy), new PointF(dx + dw, dy), new PointF(dx, dy + dh) };
                    g.DrawImage(work, dest, new RectangleF(0, 0, work.Width, work.Height), GraphicsUnit.Pixel, ia);
                }
                px = ReadPixels(canvas);
            }
            for (int cy = 0; cy < r; cy++)
                for (int cx = 0; cx < c; cx++)
                {
                    int fg = 0; long rs = 0, gs = 0, bs = 0;
                    for (int y = cy * ph; y < (cy + 1) * ph; y++)
                        for (int x = cx * pw; x < (cx + 1) * pw; x++)
                        {
                            int i = (y * W + x) * 4;
                            if (px[i + 3] < 128) continue;
                            int B = px[i], G = px[i + 1], R = px[i + 2];
                            if (useBg)
                            {
                                int dr = R - bg.R, dg = G - bg.G, db = B - bg.B;
                                if (dr * dr + dg * dg + db * db < 60 * 60) continue;
                            }
                            fg++; rs += R; gs += G; bs += B;
                        }
                    int idx = cy * c + cx;
                    frac[idx] = fg / (double)(pw * ph);
                    col[idx] = fg > 0 ? Color.FromArgb((int)(rs / fg), (int)(gs / fg), (int)(bs / fg)) : Color.Gray;
                }
        }

        static Color BorderColor(Bitmap b)
        {
            byte[] px = ReadPixels(b);
            int w = b.Width, h = b.Height;
            List<int> rs = new List<int>(), gs = new List<int>(), bs = new List<int>();
            Action<int, int> add = (x, y) => { int i = (y * w + x) * 4; bs.Add(px[i]); gs.Add(px[i + 1]); rs.Add(px[i + 2]); };
            for (int x = 0; x < w; x++) { add(x, 0); add(x, h - 1); }
            for (int y = 0; y < h; y++) { add(0, y); add(w - 1, y); }
            rs.Sort(); gs.Sort(); bs.Sort();
            return Color.FromArgb(rs[rs.Count / 2], gs[gs.Count / 2], bs[bs.Count / 2]);
        }

        static byte[] ReadPixels(Bitmap b)
        {
            int w = b.Width, h = b.Height;
            byte[] px = new byte[w * h * 4];
            BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4);
            b.UnlockBits(d);
            return px;
        }

        // 匈牙利演算法（最小成本指派），n 列 <= m 欄；回傳每一列被指派到的欄
        static int[] Hungarian(double[,] a)
        {
            int n = a.GetLength(0), m = a.GetLength(1);
            double[] u = new double[n + 1], v = new double[m + 1];
            int[] p = new int[m + 1], way = new int[m + 1];
            for (int i = 1; i <= n; i++)
            {
                p[0] = i;
                int j0 = 0;
                double[] minv = new double[m + 1];
                bool[] usedCol = new bool[m + 1];
                for (int j = 0; j <= m; j++) minv[j] = double.MaxValue;
                do
                {
                    usedCol[j0] = true;
                    int i0 = p[j0], j1 = 0;
                    double delta = double.MaxValue;
                    for (int j = 1; j <= m; j++)
                    {
                        if (usedCol[j]) continue;
                        double cur = a[i0 - 1, j - 1] - u[i0] - v[j];
                        if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                        if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                    }
                    for (int j = 0; j <= m; j++)
                    {
                        if (usedCol[j]) { u[p[j]] += delta; v[j] -= delta; }
                        else minv[j] -= delta;
                    }
                    j0 = j1;
                } while (p[j0] != 0);
                do { int j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
            }
            int[] ans = new int[n];
            for (int j = 1; j <= m; j++) if (p[j] != 0) ans[p[j] - 1] = j - 1;
            return ans;
        }
    }

    static class Shapes
    {
        static Bitmap NewCanvas(int w, int h) { return new Bitmap(w, h, PixelFormat.Format32bppArgb); }

        public static PatternSource Heart()
        {
            Bitmap b = NewCanvas(240, 224);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                PointF[] pts = new PointF[160];
                for (int i = 0; i < pts.Length; i++)
                {
                    double t = 2 * Math.PI * i / pts.Length;
                    double x = 16 * Math.Pow(Math.Sin(t), 3);
                    double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                    pts[i] = new PointF((float)(120 + x * 7), (float)(102 - y * 7));
                }
                g.FillPolygon(Brushes.Black, pts);
            }
            return new PatternSource { Image = b };
        }

        public static PatternSource Star()
        {
            Bitmap b = NewCanvas(240, 230);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                PointF[] pts = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double ang = (-90 + i * 36) * Math.PI / 180, rad = i % 2 == 0 ? 118 : 48;
                    pts[i] = new PointF((float)(120 + rad * Math.Cos(ang)), (float)(124 + rad * Math.Sin(ang)));
                }
                g.FillPolygon(Brushes.Black, pts);
            }
            return new PatternSource { Image = b };
        }

        public static PatternSource Smiley()
        {
            Bitmap b = NewCanvas(240, 240);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(Brushes.Black, 4, 4, 232, 232);
                g.CompositingMode = CompositingMode.SourceCopy;
                using (SolidBrush clear = new SolidBrush(Color.Transparent))
                {
                    g.FillEllipse(clear, 66, 58, 36, 56);
                    g.FillEllipse(clear, 138, 58, 36, 56);
                }
                using (Pen p = new Pen(Color.Transparent, 28))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, 56, 70, 128, 116, 25, 130);
                }
            }
            return new PatternSource { Image = b };
        }

        // 英數字用 5×7 點陣字（小尺寸也清楚），其他文字（如中文）用字型繪製
        public static PatternSource Text(string s, int sx, int sy)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return null;
            PatternSource pix = PixelText(s.ToUpperInvariant(), sx, sy);
            if (pix != null) return pix;
            using (Font f = new Font("Microsoft JhengHei UI", 120, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF sz;
                using (Bitmap tmp = new Bitmap(1, 1))
                using (Graphics tg = Graphics.FromImage(tmp)) sz = tg.MeasureString(s, f);
                Bitmap b = NewCanvas(Math.Max(1, (int)sz.Width + 40), Math.Max(1, (int)sz.Height + 40));
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    g.DrawString(s, f, Brushes.Black, 20, 20);
                }
                return new PatternSource { Image = b };
            }
        }

        static PatternSource PixelText(string s, int sx, int sy)
        {
            foreach (char ch in s) if (ch != ' ' && !Font5x7.ContainsKey(ch)) return null;
            List<bool[]> columns = new List<bool[]>();
            foreach (char ch in s)
            {
                if (columns.Count > 0) columns.Add(new bool[7]); // 字距
                if (ch == ' ') { columns.Add(new bool[7]); columns.Add(new bool[7]); continue; }
                byte[] glyph = Font5x7[ch];
                for (int x = 0; x < 5; x++)
                {
                    bool[] c = new bool[7];
                    for (int y = 0; y < 7; y++) c[y] = (glyph[y] & (0x10 >> x)) != 0;
                    columns.Add(c);
                }
            }
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (int x = 0; x < columns.Count; x++)
                for (int y = 0; y < 7; y++)
                    if (columns[x][y]) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (maxX < 0) return null;
            int cols = maxX - minX + 1, rows = maxY - minY + 1;
            int bw = 8, bh = Math.Max(1, (int)Math.Round(8.0 * sy / sx)); // 每個點的長寬比 = 桌面格子的長寬比
            Bitmap b = NewCanvas(cols * bw, rows * bh);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                for (int x = 0; x < cols; x++)
                    for (int y = 0; y < rows; y++)
                        if (columns[minX + x][minY + y]) g.FillRectangle(Brushes.Black, x * bw, y * bh, bw, bh);
            }
            return new PatternSource { Image = b, BaseCols = cols };
        }

        static readonly Dictionary<char, byte[]> Font5x7 = new Dictionary<char, byte[]>
        {
            {'A', new byte[]{0x0E,0x11,0x11,0x1F,0x11,0x11,0x11}}, {'B', new byte[]{0x1E,0x11,0x11,0x1E,0x11,0x11,0x1E}},
            {'C', new byte[]{0x0E,0x11,0x10,0x10,0x10,0x11,0x0E}}, {'D', new byte[]{0x1E,0x11,0x11,0x11,0x11,0x11,0x1E}},
            {'E', new byte[]{0x1F,0x10,0x10,0x1E,0x10,0x10,0x1F}}, {'F', new byte[]{0x1F,0x10,0x10,0x1E,0x10,0x10,0x10}},
            {'G', new byte[]{0x0E,0x11,0x10,0x17,0x11,0x11,0x0F}}, {'H', new byte[]{0x11,0x11,0x11,0x1F,0x11,0x11,0x11}},
            {'I', new byte[]{0x0E,0x04,0x04,0x04,0x04,0x04,0x0E}}, {'J', new byte[]{0x07,0x02,0x02,0x02,0x02,0x12,0x0C}},
            {'K', new byte[]{0x11,0x12,0x14,0x18,0x14,0x12,0x11}}, {'L', new byte[]{0x10,0x10,0x10,0x10,0x10,0x10,0x1F}},
            {'M', new byte[]{0x11,0x1B,0x15,0x15,0x11,0x11,0x11}}, {'N', new byte[]{0x11,0x11,0x19,0x15,0x13,0x11,0x11}},
            {'O', new byte[]{0x0E,0x11,0x11,0x11,0x11,0x11,0x0E}}, {'P', new byte[]{0x1E,0x11,0x11,0x1E,0x10,0x10,0x10}},
            {'Q', new byte[]{0x0E,0x11,0x11,0x11,0x15,0x12,0x0D}}, {'R', new byte[]{0x1E,0x11,0x11,0x1E,0x14,0x12,0x11}},
            {'S', new byte[]{0x0F,0x10,0x10,0x0E,0x01,0x01,0x1E}}, {'T', new byte[]{0x1F,0x04,0x04,0x04,0x04,0x04,0x04}},
            {'U', new byte[]{0x11,0x11,0x11,0x11,0x11,0x11,0x0E}}, {'V', new byte[]{0x11,0x11,0x11,0x11,0x11,0x0A,0x04}},
            {'W', new byte[]{0x11,0x11,0x11,0x15,0x15,0x15,0x0A}}, {'X', new byte[]{0x11,0x11,0x0A,0x04,0x0A,0x11,0x11}},
            {'Y', new byte[]{0x11,0x11,0x0A,0x04,0x04,0x04,0x04}}, {'Z', new byte[]{0x1F,0x01,0x02,0x04,0x08,0x10,0x1F}},
            {'0', new byte[]{0x0E,0x11,0x13,0x15,0x19,0x11,0x0E}}, {'1', new byte[]{0x04,0x0C,0x04,0x04,0x04,0x04,0x0E}},
            {'2', new byte[]{0x0E,0x11,0x01,0x02,0x04,0x08,0x1F}}, {'3', new byte[]{0x1F,0x02,0x04,0x02,0x01,0x11,0x0E}},
            {'4', new byte[]{0x02,0x06,0x0A,0x12,0x1F,0x02,0x02}}, {'5', new byte[]{0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E}},
            {'6', new byte[]{0x06,0x08,0x10,0x1E,0x11,0x11,0x0E}}, {'7', new byte[]{0x1F,0x01,0x02,0x04,0x08,0x08,0x08}},
            {'8', new byte[]{0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E}}, {'9', new byte[]{0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C}},
            {'!', new byte[]{0x04,0x04,0x04,0x04,0x04,0x00,0x04}}, {'?', new byte[]{0x0E,0x11,0x01,0x02,0x04,0x00,0x04}},
            {'-', new byte[]{0x00,0x00,0x00,0x1F,0x00,0x00,0x00}}, {'.', new byte[]{0x00,0x00,0x00,0x00,0x00,0x0C,0x0C}},
            {'+', new byte[]{0x00,0x04,0x04,0x1F,0x04,0x04,0x00}}, {':', new byte[]{0x00,0x0C,0x0C,0x00,0x0C,0x0C,0x00}},
            {'♥', new byte[]{0x00,0x0A,0x1F,0x1F,0x0E,0x04,0x00}}, {'❤', new byte[]{0x00,0x0A,0x1F,0x1F,0x0E,0x04,0x00}},
        };
    }

    static class Backup
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopColorSorter", "backup.tsv");

        public static void Save(List<DesktopItem> items)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            StringBuilder sb = new StringBuilder();
            foreach (DesktopItem it in items) sb.Append(it.Pos.X).Append('\t').Append(it.Pos.Y).Append('\t').Append(it.ParsingName).Append('\n');
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }

        public static Dictionary<string, POINT> Load()
        {
            Dictionary<string, POINT> d = new Dictionary<string, POINT>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(FilePath)) return d;
            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string[] p = line.Split(new[] { '\t' }, 3);
                int x, y;
                if (p.Length == 3 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y)) d[p[2]] = new POINT(x, y);
            }
            return d;
        }
    }

    // ======================= 介面元件 =======================

    class GroupHeader : Control
    {
        readonly Color dot; readonly float scale;
        public GroupHeader(string text, Color dot, float scale)
        {
            this.Text = text; this.dot = dot; this.scale = scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Microsoft JhengHei UI", 10f, FontStyle.Bold);
            Size = new Size((int)(320 * scale), (int)(30 * scale));
            Margin = new Padding((int)(4 * scale), (int)(8 * scale), 0, 0);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int d = (int)(12 * scale), y = (Height - d) / 2;
            using (SolidBrush b = new SolidBrush(dot)) e.Graphics.FillEllipse(b, 2, y, d, d);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(d + (int)(10 * scale), 0, Width, Height),
                Color.FromArgb(40, 44, 52), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    class Tile : Control
    {
        readonly DesktopItem item; readonly float scale; bool hover;
        public Tile(DesktopItem item, float scale)
        {
            this.item = item; this.scale = scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size((int)(84 * scale), (int)(96 * scale));
            Margin = new Padding((int)(3 * scale));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (hover)
                using (GraphicsPath p = Round(new Rectangle(0, 0, Width - 1, Height - 1), (int)(8 * scale)))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(232, 236, 244))) g.FillPath(b, p);
            int isz = (int)(44 * scale);
            Rectangle ir = new Rectangle((Width - isz) / 2, (int)(8 * scale), isz, isz);
            if (item.Icon != null) g.DrawImage(item.Icon, ir);
            int bw = (int)(36 * scale), bh = Math.Max(4, (int)(5 * scale));
            Rectangle br = new Rectangle((Width - bw) / 2, ir.Bottom + (int)(6 * scale), bw, bh);
            using (GraphicsPath p = Round(br, bh / 2))
            using (SolidBrush b = new SolidBrush(item.Color.Swatch)) g.FillPath(b, p);
            Rectangle tr = new Rectangle(2, br.Bottom + (int)(4 * scale), Width - 4, Height - br.Bottom - (int)(4 * scale));
            TextRenderer.DrawText(g, item.Name, Font, tr, Color.FromArgb(50, 54, 62),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Max(1, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // 像素圖案預覽：畫出縮小的螢幕，圖示放在將來的位置
    class PatternPreview : Control
    {
        public Snapshot Snap;
        public PatternPlan Plan;
        readonly float scale;

        public PatternPreview(float scale)
        {
            this.scale = scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (Snap == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            double gw = Snap.GridCols * Snap.Spacing.X, gh = Snap.GridRows * Snap.Spacing.Y;
            float pad = 18 * scale, caption = 26 * scale;
            double k = Math.Min((Width - 2 * pad) / gw, (Height - 2 * pad - caption) / gh);
            if (k <= 0) return;
            float cw = (float)(Snap.Spacing.X * k), ch = (float)(Snap.Spacing.Y * k);
            RectangleF desk = new RectangleF((float)((Width - gw * k) / 2), (float)((Height - caption - gh * k) / 2), (float)(gw * k), (float)(gh * k));
            RectangleF frame = RectangleF.Inflate(desk, 8 * scale, 8 * scale);

            using (GraphicsPath p = Tile.Round(frame, 10 * scale))
            using (LinearGradientBrush b = new LinearGradientBrush(frame, Color.FromArgb(34, 58, 112), Color.FromArgb(16, 24, 48), 90f))
                g.FillPath(b, p);
            using (SolidBrush dotB = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                for (int x = 0; x < Snap.GridCols; x++)
                    for (int y = 0; y < Snap.GridRows; y++)
                        g.FillEllipse(dotB, desk.X + (x + 0.5f) * cw - 1.5f * scale, desk.Y + (y + 0.5f) * ch - 1.5f * scale, 3 * scale, 3 * scale);

            if (Plan != null)
            {
                float isz = Math.Min(cw * 0.86f, ch * 0.7f);
                using (ImageAttributes faded = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = 0.35f;
                    faded.SetColorMatrix(cm);
                    for (int i = 0; i < Plan.Items.Count; i++)
                    {
                        Point c = Plan.Cells[i];
                        Bitmap icon = Plan.Items[i].Icon;
                        if (icon == null || c.X >= Snap.GridCols) continue;
                        Rectangle r = Rectangle.Round(new RectangleF(desk.X + c.X * cw + (cw - isz) / 2, desk.Y + c.Y * ch + (ch - isz) / 2, isz, isz));
                        if (i < Plan.Used) g.DrawImage(icon, r);
                        else g.DrawImage(icon, r, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, faded);
                    }
                }
            }
            TextRenderer.DrawText(g, "預覽：深藍色方框就是你的螢幕（半透明的是用不到、會放到角落的圖示）", Font,
                new Rectangle(0, (int)(frame.Bottom + 6 * scale), Width, (int)caption), Color.FromArgb(110, 116, 126),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }
    }

}
