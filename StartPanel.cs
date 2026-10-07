// 自訂開始功能表：外觀與操作盡量比照 Windows 11（25H2）原本的開始功能表
//  搜尋框 → 已釘選 → 建議 → 全部（類別 / 格線 / 清單），底部是帳戶與電源
//  沒有照片時用「擷取背後畫面 + 模糊 + 色調」模擬毛玻璃；有照片時用照片（可框選位置）
//  動畫：從工作列後面滑上來並淡入、關閉時滑下淡出、滑鼠反白漸變、平滑捲動、類別資料夾展開
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopColorSorter
{
    static partial class Native
    {
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("msimg32.dll")] public static extern bool AlphaBlend(IntPtr dest, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, uint blend);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] public static extern void MoveMemory(IntPtr dest, IntPtr src, UIntPtr count);
    }

    // 可以同時用 GDI+ 畫、用 GDI 貼的透明圖（同一塊記憶體）：內容先畫在這裡，捲動時只要整塊貼上去
    sealed class GdiDib : IDisposable
    {
        public readonly IntPtr Dc, Bits;
        public readonly Bitmap Bitmap;
        public readonly int Width, Height, Stride;
        readonly IntPtr hbm, old;

        public GdiDib(int w, int h)
        {
            BITMAPINFO bi = new BITMAPINFO();
            bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
            Dc = Native.CreateCompatibleDC(IntPtr.Zero);
            IntPtr bits;
            hbm = Native.CreateDIBSection(Dc, ref bi, 0, out bits, IntPtr.Zero, 0);
            Bits = bits;
            old = Native.SelectObject(Dc, hbm);
            Width = w; Height = h; Stride = w * 4;
            Bitmap = new Bitmap(w, h, Stride, PixelFormat.Format32bppPArgb, Bits);
        }

        public void Dispose()
        {
            Bitmap.Dispose();
            Native.SelectObject(Dc, old);
            Native.DeleteDC(Dc);
            Native.DeleteObject(hbm);
        }
    }

    // 已經畫好的點陣圖，包成 GDI 的記憶體 DC：動畫每一幀只要貼圖 / 半透明混合，比 GDI+ 快很多
    sealed class GdiSurface : IDisposable
    {
        public readonly IntPtr Dc;
        public readonly int Width, Height;
        readonly IntPtr hbm, old;

        public GdiSurface(Bitmap b)
        {
            hbm = b.GetHbitmap();
            Dc = Native.CreateCompatibleDC(IntPtr.Zero);
            old = Native.SelectObject(Dc, hbm);
            Width = b.Width;
            Height = b.Height;
        }

        public void Dispose()
        {
            Native.SelectObject(Dc, old);
            Native.DeleteDC(Dc);
            Native.DeleteObject(hbm);
        }
    }

    // ======================= 應用程式分類 =======================

    // Windows 用市集資料分類，一般程式拿不到，所以依名稱關鍵字判斷
    static class AppCategories
    {
        public static readonly string[] Names = { "生產力", "公用程式與工具", "創意", "開發人員工具", "社交", "娛樂", "遊戲", "其他" };
        public const int Other = 7;

        // 越前面的規則越優先
        static readonly KeyValuePair<int, string[]>[] Rules =
        {
            new KeyValuePair<int, string[]>(3, new[] { "visual studio", "vs code", "vscode", "pycharm", "intellij", "jetbrains", "webstorm", "rider", "clion", "goland", "android studio",
                "github", "git bash", "git gui", "python", "node.js", "nodejs", "docker", "postman", "unity", "unreal", "sublime", "notepad++", "chatgpt", "claude", "cursor",
                "windsurf", "wsl", "ubuntu", "debian", "kali", "sql", "mysql", "dbeaver", "putty", "winscp", "filezilla", "wireshark", "burp", "nmap", "vmware", "virtualbox",
                "hyper-v", "powershell", "developer", "開發人員", "jdk", "java", "anaconda", "jupyter", "arduino", "eclipse", "xampp", "滲透" }),
            new KeyValuePair<int, string[]>(6, new[] { "steam", "epic games", "riot", "valorant", "league of legends", "battle.net", "ubisoft", "ea app", "origin", "gog galaxy",
                "xbox", "minecraft", "roblox", "genshin", "原神", "虹彩六號", "gta", "遊戲", "game", "games", "bluestacks", "ldplayer", "雷電模擬器", "mumu", "nox", "brawl",
                "rainbow six", "counter-strike", "dota", "apex", "fortnite", "osu", "hoyoplay", "garena", "beanfun", "樂豆", "狂飆" }),
            new KeyValuePair<int, string[]>(4, new[] { "line", "telegram", "whatsapp", "wechat", "微信", "messenger", "discord", "skype", "signal", "instagram", "facebook",
                "twitter", "qq", "kakao", "slack", "threads" }),
            new KeyValuePair<int, string[]>(5, new[] { "spotify", "netflix", "youtube", "music", "音樂", "媒體播放器", "media player", "vlc", "potplayer", "kkbox", "prime video",
                "disney", "twitch", "影片", "movies", "電影", "kodi", "plex", "itunes", "podcast", "tidal" }),
            new KeyValuePair<int, string[]>(2, new[] { "photoshop", "illustrator", "premiere", "after effects", "lightroom", "gimp", "krita", "blender", "paint", "小畫家", "相片",
                "photos", "clipchamp", "obs", "davinci", "capcut", "剪映", "fl studio", "audacity", "figma", "canva", "inkscape", "camera", "相機", "錄音", "sound recorder",
                "affinity", "aseprite", "medibang", "designer", "3d", "cinema", "maya", "zbrush", "audition", "vegas", "filmora", "shotcut" }),
            new KeyValuePair<int, string[]>(1, new[] { "設定", "settings", "control panel", "控制台", "calculator", "小算盤", "snipping", "剪取工具", "7-zip", "winrar", "bandizip",
                "terminal", "終端機", "命令提示字元", "command prompt", "task manager", "工作管理員", "registry", "登錄編輯程式", "clock", "時鐘", "store", "市集", "phone link",
                "手機連結", "remote desktop", "遠端桌面", "vpn", "everything", "powertoys", "ccleaner", "geforce", "nvidia", "amd", "logitech", "razer", "driver", "驅動",
                "backup", "備份", "windows tools", "windows 工具", "系統", "system", "recovery", "defender", "security", "安全性", "工具", "tool", "tools", "utility",
                "manager", "管理", "monitor", "anydesk", "teamviewer", "parsec", "rufus", "cpu-z", "gpu-z", "hwinfo", "afterburner", "armoury", "疑難排解", "tips",
                "get help", "取得說明", "quick assist", "快速助手", "磁碟", "disk", "magnifier", "放大鏡", "narrator", "朗讀程式", "on-screen keyboard", "螢幕小鍵盤",
                "character map", "字元對應表", "wordpad", "小作家", "bluetooth", "藍牙", "印表機", "printer", "scanner", "掃描" }),
            new KeyValuePair<int, string[]>(0, new[] { "word", "excel", "powerpoint", "outlook", "onenote", "office", "teams", "edge", "chrome", "firefox", "opera", "brave",
                "vivaldi", "browser", "瀏覽器", "檔案總管", "file explorer", "記事本", "notepad", "notion", "evernote", "to do", "待辦", "calendar", "行事曆", "mail", "郵件",
                "onedrive", "dropbox", "google drive", "acrobat", "pdf", "zoom", "copilot", "obsidian", "trello", "libreoffice", "wps", "xmind", "translator", "翻譯",
                "dictionary", "字典", "單字" }),
        };

        public static int Of(AppEntry a)
        {
            string text = a.Name + " | " + a.Id;
            foreach (KeyValuePair<int, string[]> r in Rules)
                foreach (string kw in r.Value)
                    if (Has(text, kw)) return r.Key;
            return Other;
        }

        static bool IsWordChar(char c) { return c < 128 && char.IsLetterOrDigit(c); }

        // 英文短字要比對完整單字（避免 "line" 對到 "online"）
        static bool Has(string text, string kw)
        {
            bool ascii = kw.All(c => c < 128);
            int start = 0;
            while (start < text.Length)
            {
                int i = text.IndexOf(kw, start, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return false;
                if (!ascii) return true;
                int end = i + kw.Length;
                bool left = i == 0 || !IsWordChar(text[i - 1]);
                bool right = kw.Length > 4 || end >= text.Length || !IsWordChar(text[end]);
                if (left && right) return true;
                start = i + 1;
            }
            return false;
        }
    }

    // ======================= 深色 / 淺色右鍵選單 =======================

    sealed class MenuColors : ProfessionalColorTable
    {
        readonly bool dark;
        public MenuColors(bool dark) { this.dark = dark; UseSystemColors = false; }
        Color Bg { get { return dark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249); } }
        Color Sel { get { return dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(232, 232, 232); } }
        Color Line { get { return dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(222, 222, 222); } }
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemBorder { get { return Sel; } }
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color MenuItemPressedGradientBegin { get { return Sel; } }
        public override Color MenuItemPressedGradientEnd { get { return Sel; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Bg; } }
        public override Color CheckBackground { get { return Sel; } }
        public override Color CheckSelectedBackground { get { return Sel; } }
        public override Color CheckPressedBackground { get { return Sel; } }
    }

    sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        readonly bool dark;
        public ThemedMenuRenderer(bool dark) : base(new MenuColors(dark)) { this.dark = dark; RoundedEdges = false; }
        Color Fore { get { return dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(26, 26, 26); } }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Fore : Color.Gray;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Fore; base.OnRenderArrow(e); }
    }

    // ======================= 開始功能表視窗 =======================

    sealed class StartPanel : Form
    {
        enum View { Home, Search, Recent }
        enum Kind { Pin, RecentItem, CatIcon, CatCard, GridApp, ListApp, SearchRow, Action, Link, PopupApp, Footer }

        class Hit
        {
            public Kind Kind;
            public Rectangle Rect;
            public AppEntry App;
            public RecentEntry Recent;
            public int Cat = -1;
            public string Action;
            public int Row = -1;
        }

        class SearchResult { public string Section; public AppEntry App; public RecentEntry Recent; public string Query; }

        // ---------- 資料 ----------
        readonly float scale;
        public readonly StartStyle Style = new StartStyle();
        List<AppEntry> apps = new List<AppEntry>();
        Dictionary<string, AppEntry> appsById = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        List<AppEntry>[] cats = EmptyCats();
        List<RecentEntry> recent = new List<RecentEntry>();
        List<string> pins;
        bool loadingApps, appsLoaded, loadingRecent;
        DateTime appsLoadedAt;
        Bitmap userPic;
        string userName;

        // ---------- 畫面狀態 ----------
        View view = View.Home;
        int allMode;               // 0 類別 1 格線 2 清單
        bool pinsExpanded;
        readonly List<Hit> hits = new List<Hit>();        // 會跟著捲動的項目（內容座標）
        readonly List<Hit> fixedHits = new List<Hit>();   // 不捲動的（底部列、搜尋結果的動作按鈕）
        readonly List<Hit> popupHits = new List<Hit>();   // 類別資料夾裡的項目（視窗座標）
        readonly List<KeyValuePair<string, int>> headers = new List<KeyValuePair<string, int>>();
        List<SearchResult> results = new List<SearchResult>();
        int selected;              // 搜尋結果目前選取
        int contentHeight;
        float scrollPos, scrollTarget;
        Hit hover, pressed;
        readonly Dictionary<Hit, float> hoverAlpha = new Dictionary<Hit, float>();
        int popupCat = -1;
        float popupScroll;
        bool menuOpen, activatedOnce;

        // ---------- 動畫 ----------
        readonly Timer timer = new Timer { Interval = 10 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastTick;
        enum Phase { None, Opening, Closing }
        Phase phase;
        double phaseStart, contentFadeStart = -1, popupAnimStart = -1;
        bool popupClosing;
        Point finalLocation;
        Rectangle behindRect;
        Bitmap behind, frame, bg;
        GdiSurface behindSurf, frameSurf;
        bool frameDirty = true, layerDirty = true, hiRes;
        GdiDib cache;              // 內容（透明底）快取：捲動時只要貼上這張圖，不用重畫每個項目
        int cacheTop;              // 快取最上面對應的內容座標
        bool cacheDirty = true;
        readonly List<Point> pendingStrips = new List<Point>(); // 等著重畫的快取區段（內容座標 X=上 Y=下）
        readonly Dictionary<TextFormatFlags, StringFormat> formats = new Dictionary<TextFormatFlags, StringFormat>();
        readonly Dictionary<Bitmap, Dictionary<int, Bitmap>> scaledIcons = new Dictionary<Bitmap, Dictionary<int, Bitmap>>();
        const double OpenMs = 240, CloseMs = 150, FadeMs = 150, PopupMs = 170;

        // ---------- 外觀 ----------
        readonly TextBox search;
        readonly Font font, smallFont, headerFont, glyphFont, glyphSmall, glyphHuge, searchFont, bigFont, titleFont;
        Color text, sub, hoverFill, pressFill, cardFill, cardLine, searchFill, searchLine, accentGlyph;
        static Bitmap noise;

        int S(float v) { return (int)Math.Round(v * scale); }
        int W { get { return ClientSize.Width; } }
        int H { get { return ClientSize.Height; } }
        int FooterH { get { return S(62); } }
        Rectangle SearchBox { get { return new Rectangle(S(32), S(18), W - S(64), S(34)); } }
        Rectangle Viewport
        {
            get
            {
                int top = SearchBox.Bottom + S(18);
                return new Rectangle(S(32), top, W - S(64), H - FooterH - top - S(4));
            }
        }
        int HeaderX { get { return S(31); } }

        static List<AppEntry>[] EmptyCats()
        {
            List<AppEntry>[] c = new List<AppEntry>[AppCategories.Names.Length];
            for (int i = 0; i < c.Length; i++) c[i] = new List<AppEntry>();
            return c;
        }

        public StartPanel(float scale)
        {
            this.scale = scale;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            Text = "開始";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            font = new Font("Microsoft JhengHei UI", 9f);
            smallFont = new Font("Microsoft JhengHei UI", 8.25f);
            headerFont = new Font("Microsoft JhengHei UI", 10.5f, FontStyle.Bold);
            titleFont = new Font("Microsoft JhengHei UI", 13f, FontStyle.Bold);
            bigFont = new Font("Microsoft JhengHei UI", 11f);
            searchFont = new Font("Microsoft JhengHei UI", 10f);
            string glyph = GlyphFamily();
            glyphFont = new Font(glyph, 11f);
            glyphSmall = new Font(glyph, 7.5f);
            glyphHuge = new Font(glyph, 30f);

            search = new TextBox { BorderStyle = BorderStyle.None, Font = searchFont };
            search.HandleCreated += delegate { Native.SendMessage(search.Handle, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, "搜尋應用程式、設定和文件"); };
            search.TextChanged += delegate { OnQueryChanged(); };
            search.KeyDown += OnSearchKey;
            Controls.Add(search); // 搜尋框收到的滾輪會自動轉給視窗，不用另外接

            allMode = Math.Max(0, Math.Min(2, AppSettings.GetInt("StartAllView", 0)));
            userName = UserInfo.DisplayName();
            userPic = UserInfo.Picture();
            string saved = AppSettings.Get("StartPins", null);
            pins = saved == null ? null : saved.Split('|').Where(x => x.Length > 0).ToList();
            timer.Tick += delegate { Tick(); };
            ApplyStyle();
        }

        static string GlyphFamily()
        {
            using (InstalledFontCollection f = new InstalledFontCollection())
                if (f.Families.Any(x => x.Name == "Segoe Fluent Icons")) return "Segoe Fluent Icons";
            return "Segoe MDL2 Assets";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW：不出現在 Alt+Tab
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDwmFrame();
        }

        // Windows 11 圓角、細框線（DWM 也會畫陰影）
        void ApplyDwmFrame()
        {
            if (!IsHandleCreated) return;
            try
            {
                int round = 2; // DWMWCP_ROUND
                Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);
                Color c = Style.Dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(214, 214, 214);
                int colorref = c.R | (c.G << 8) | (c.B << 16);
                Native.DwmSetWindowAttribute(Handle, 34, ref colorref, 4); // DWMWA_BORDER_COLOR
            }
            catch { }
        }

        public void Preload()
        {
            if (!IsHandleCreated) CreateHandle();
            RefreshData();
        }

        public void RefreshUser()
        {
            userName = UserInfo.DisplayName();
            Invalidate();
        }

        // ======================= 外觀 =======================

        public void ApplyStyle()
        {
            Size = Style.Large ? new Size(S(940), S(960)) : new Size(S(812), S(860));
            // 開著的時候改大小：重新貼齊工作列上方，不要長到工作列底下
            bool shown = Visible && phase != Phase.Closing && targetScreen != null;
            if (shown)
            {
                Rectangle wa = targetScreen.WorkingArea;
                finalLocation = new Point(finalLocation.X, Math.Max(wa.Top, wa.Bottom - Height - S(12)));
                if (phase == Phase.None) Location = finalLocation;
            }
            if (Style.Dark)
            {
                text = Color.FromArgb(255, 255, 255); sub = Color.FromArgb(197, 197, 197);
                hoverFill = Color.FromArgb(18, 255, 255, 255); pressFill = Color.FromArgb(10, 255, 255, 255);
                cardFill = Color.FromArgb(14, 255, 255, 255); cardLine = Color.FromArgb(20, 255, 255, 255);
                searchFill = Color.FromArgb(30, 30, 30); searchLine = Color.FromArgb(58, 58, 58);
            }
            else
            {
                text = Color.FromArgb(26, 26, 26); sub = Color.FromArgb(96, 96, 96);
                hoverFill = Color.FromArgb(12, 0, 0, 0); pressFill = Color.FromArgb(7, 0, 0, 0);
                cardFill = Color.FromArgb(150, 255, 255, 255); cardLine = Color.FromArgb(14, 0, 0, 0);
                searchFill = Color.FromArgb(251, 251, 251); searchLine = Color.FromArgb(224, 224, 224);
            }
            Color acc = WinSettings.Accent;
            accentGlyph = Style.Dark ? Blend(acc, Color.White, 0.45f) : Blend(acc, Color.Black, 0.15f);
            search.BackColor = searchFill;
            search.ForeColor = text;
            Rectangle sb = SearchBox;
            search.SetBounds(sb.X + S(46), sb.Y + (sb.Height - search.PreferredHeight) / 2 + 1, sb.Width - S(64), search.PreferredHeight);
            ApplyDwmFrame();
            // 開著的時候沿用剛才擷取的背後畫面，毛玻璃不會變成純色
            if (shown && Style.Photo == null && behind != null && behind.Width >= W && behind.Height >= H)
                using (Bitmap area = behind.Clone(new Rectangle(0, 0, W, H), PixelFormat.Format32bppRgb)) RebuildBackground(area);
            else RebuildBackground(null);
            BuildLayout();
            Invalidate();
        }

        static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // 背景：照片 或 擷取到的背後畫面（模糊 + 色調 + 雜訊 = 毛玻璃），再加上搜尋框與底部列
        void RebuildBackground(Bitmap behindArea)
        {
            int w = W, h = H;
            if (w <= 0 || h <= 0) return;
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                Color tint = Style.Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
                g.Clear(tint);
                if (Style.Photo != null)
                {
                    using (Bitmap photo = CoverAndBlur(Style.Photo, Style.Crop, w, h, Style.Blur))
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ColorMatrix cm = new ColorMatrix();
                        cm.Matrix33 = Math.Max(0.1f, Math.Min(1f, Style.Strength / 100f));
                        ia.SetColorMatrix(cm);
                        g.DrawImage(photo, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                    }
                    // 很淡的漸層，讓文字在花俏的照片上也看得清楚
                    Color scrim = Style.Dark ? Color.Black : Color.White;
                    using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(0, 0, w, h + 1), Color.FromArgb(40, scrim), Color.FromArgb(95, scrim), 90f))
                        g.FillRectangle(lg, 0, 0, w, h);
                }
                else if (behindArea != null)
                {
                    using (Bitmap blurred = Blur(behindArea, w, h, 10))
                        g.DrawImage(blurred, 0, 0, w, h);
                    using (SolidBrush t = new SolidBrush(Color.FromArgb(Style.Dark ? 216 : 206, tint))) g.FillRectangle(t, 0, 0, w, h);
                    using (TextureBrush tb = new TextureBrush(Noise())) g.FillRectangle(tb, 0, 0, w, h);
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // 底部列
                using (SolidBrush fb = new SolidBrush(Style.Dark ? Color.FromArgb(46, 0, 0, 0) : Color.FromArgb(10, 0, 0, 0)))
                    g.FillRectangle(fb, 0, h - FooterH, w, FooterH);
                using (Pen p = new Pen(Style.Dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(16, 0, 0, 0)))
                    g.DrawLine(p, 0, h - FooterH, w, h - FooterH);
                // 搜尋框
                Rectangle sb = SearchBox;
                using (GraphicsPath path = Tile.Round(sb, sb.Height / 2f))
                {
                    using (SolidBrush s = new SolidBrush(searchFill)) g.FillPath(s, path);
                    using (Pen p = new Pen(searchLine)) g.DrawPath(p, path);
                }
            }
            if (bg != null) bg.Dispose();
            bg = b;
        }

        static Bitmap Noise()
        {
            if (noise != null) return noise;
            Bitmap n = new Bitmap(96, 96, PixelFormat.Format32bppArgb);
            Random r = new Random(7);
            for (int y = 0; y < n.Height; y++)
                for (int x = 0; x < n.Width; x++)
                {
                    int v = r.Next(256);
                    n.SetPixel(x, y, Color.FromArgb(7, v, v, v));
                }
            noise = n;
            return n;
        }

        static Bitmap Blur(Bitmap src, int w, int h, int level)
        {
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            double f = 1 + level * 2.2;
            int sw = Math.Max(4, (int)(w / f)), sh = Math.Max(4, (int)(h / f));
            using (Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppRgb))
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, new Rectangle(0, 0, sw, sh), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(small, new Rectangle(0, 0, w, h), 0, 0, sw, sh, GraphicsUnit.Pixel, ia);
                }
            }
            return result;
        }

        // 依框選範圍（沒有就置中）把照片裁成視窗比例，需要時再模糊
        static Bitmap CoverAndBlur(Bitmap src, RectangleF crop, int w, int h, int level)
        {
            RectangleF sr = CropRect.SourceRect(src.Size, crop, w / (double)h);
            Bitmap cover = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(cover))
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h), sr.X, sr.Y, sr.Width, sr.Height, GraphicsUnit.Pixel, ia);
            }
            if (level <= 0) return cover;
            Bitmap blurred = Blur(cover, w, h, level);
            cover.Dispose();
            return blurred;
        }

        // ======================= 開啟 / 關閉（含動畫） =======================

        int fromOffset;            // 這段動畫開始時，視窗在最終位置下方幾像素
        float fromAlpha;           // 這段動畫開始時的不透明度
        Screen targetScreen;       // 在哪個螢幕開（滑鼠所在的螢幕，和原本的開始功能表一樣）
        double hiddenAt = -1e9;    // 最近一次收起來的時間

        public void Toggle()
        {
            if (Visible && phase != Phase.Closing) HideLauncher();
            else ShowLauncher();
        }

        // 原本的開始功能表被打開時（點開始按鈕、Ctrl+Esc）呼叫。
        // 如果自訂的剛剛才因為同一次點擊而收起來，代表使用者是要「關閉」，就不要再打開
        public void ShowFromRealStart()
        {
            if (Now() - hiddenAt < 700) return;
            ShowLauncher();
        }

        float PhaseAlpha()
        {
            if (phase == Phase.None) return Visible ? 1f : 0f;
            double t = (Now() - phaseStart) / (phase == Phase.Opening ? OpenMs : CloseMs);
            double a = phase == Phase.Opening ? fromAlpha + (1 - fromAlpha) * EaseOut(t * 1.15) : fromAlpha * (1 - EaseIn(t));
            return (float)Math.Max(0, Math.Min(1, a));
        }

        public void ShowLauncher()
        {
            if (Visible && phase != Phase.Closing) return;
            bool reopening = Visible && phase == Phase.Closing; // 正在關閉時又按了一次：從目前的位置和透明度接著動
            float curAlpha = reopening ? PhaseAlpha() : 0f;
            int curOffset = reopening ? Top - finalLocation.Y : 0;

            if (!reopening || targetScreen == null) targetScreen = Screen.FromPoint(Cursor.Position);
            Rectangle wa = targetScreen.WorkingArea;
            bool left = false;
            try { left = WinSettings.TaskbarLeft; } catch { }
            int x = left ? wa.Left + S(12) : wa.Left + (wa.Width - Width) / 2;
            finalLocation = new Point(x, Math.Max(wa.Top, wa.Bottom - Height - S(12)));
            int slide = Style.Animate ? S(64) : 0;

            // 先擷取視窗背後（含滑動路徑）的畫面：用來做毛玻璃和淡入
            if (!Visible)
            {
                behindRect = new Rectangle(finalLocation.X, finalLocation.Y, Width, Height + Math.Max(slide, S(64)));
                if (behind != null) behind.Dispose();
                behind = new Bitmap(behindRect.Width, behindRect.Height, PixelFormat.Format32bppRgb);
                if (behindSurf != null) { behindSurf.Dispose(); behindSurf = null; }
                try { using (Graphics g = Graphics.FromImage(behind)) g.CopyFromScreen(behindRect.Location, Point.Empty, behindRect.Size); }
                catch { }
                if (Style.Photo == null)
                    using (Bitmap area = behind.Clone(new Rectangle(0, 0, Width, Height), PixelFormat.Format32bppRgb)) RebuildBackground(area);
            }

            search.Text = "";
            view = View.Home;
            popupCat = -1;
            popupClosing = false;
            popupAnimStart = -1;
            scrollPos = scrollTarget = 0;
            hover = pressed = null;
            hoverAlpha.Clear();
            // 從「正在關閉」直接重新打開時視窗本來就在前景，不會再收到 Activated，要保留狀態
            activatedOnce = Visible && Native.GetForegroundWindow() == Handle;
            BuildLayout();
            RefreshData();

            if (Style.Animate)
            {
                fromOffset = reopening ? curOffset : slide;
                fromAlpha = curAlpha;
                Location = new Point(finalLocation.X, finalLocation.Y + fromOffset);
                search.Visible = false;
                phase = Phase.Opening;
                phaseStart = Now();
                contentFadeStart = -1;
                PrepareFrame(); // 在視窗出現之前就畫好，動畫第一格不會卡
            }
            else Location = finalLocation;
            // 先「隱形」地顯示：等排好 z 順序、畫好第一格才讓它出現在螢幕上，
            // 否則會有一瞬間看到還沒畫的（黑色）視窗壓在工作列上
            bool wasHidden = !Visible;
            if (wasHidden) Cloak(true);
            Show();
            ForceForeground(Handle);
            Activate();
            if (Style.Animate) BelowTaskbar();
            else { KeepTopmost(); search.Visible = true; }
            if (wasHidden)
            {
                Update();
                Cloak(false);
                if (Native.GetForegroundWindow() != Handle) // 隱形時沒拿到焦點就再要一次
                {
                    ForceForeground(Handle);
                    Activate();
                    if (Style.Animate) BelowTaskbar(); else KeepTopmost();
                }
            }
            search.Focus();
            StartTimer();
        }

        public void HideLauncher()
        {
            if (!Visible || phase == Phase.Closing) return;
            hiddenAt = Now();
            popupCat = -1;
            popupClosing = false;
            popupAnimStart = -1;
            if (!Style.Animate) { FinishHide(); return; }
            fromAlpha = phase == Phase.Opening ? PhaseAlpha() : 1f; // 開到一半就關：從目前的樣子接著關
            fromOffset = Top - finalLocation.Y;
            phase = Phase.Closing;
            phaseStart = Now();
            search.Visible = false;
            PrepareFrame(); // 用目前的樣子做關閉動畫（搜尋框藏起來後，文字改由這張圖畫）
            BelowTaskbar();
            StartTimer();
        }

        void FinishHide()
        {
            phase = Phase.None;
            Hide();
            search.Visible = true;
            Location = finalLocation;
        }

        // 動畫期間放在工作列「下面」，看起來就像從工作列後面滑出來。
        // 但工作列被全螢幕程式降成非最上層時不能插在它後面（會失去最上層、被全螢幕視窗蓋住）
        void BelowTaskbar()
        {
            IntPtr bar = TaskbarOn(targetScreen);
            if (bar != IntPtr.Zero && (Native.GetWindowLong(bar, -20 /*GWL_EXSTYLE*/) & 0x8 /*WS_EX_TOPMOST*/) != 0)
                Native.SetWindowPos(Handle, bar, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE | NOMOVE | NOACTIVATE
            else KeepTopmost();
        }

        // DWM 隱形：視窗照常存在、可以畫，只是不顯示在螢幕上
        void Cloak(bool on)
        {
            if (!IsHandleCreated) return;
            int v = on ? 1 : 0;
            try { Native.DwmSetWindowAttribute(Handle, 13 /*DWMWA_CLOAK*/, ref v, 4); } catch { }
        }

        void KeepTopmost()
        {
            Native.SetWindowPos(Handle, (IntPtr)(-1) /*HWND_TOPMOST*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        static IntPtr TaskbarOn(Screen s)
        {
            IntPtr main = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            if (s == null || s.Primary) return main;
            for (IntPtr w = IntPtr.Zero; ; )
            {
                w = Native.FindWindowEx(IntPtr.Zero, w, "Shell_SecondaryTrayWnd", null);
                if (w == IntPtr.Zero) break;
                RECT r;
                if (Native.GetWindowRect(w, out r) && Screen.FromRectangle(r.ToRectangle()).DeviceName == s.DeviceName) return w;
            }
            return main;
        }

        static void ForceForeground(IntPtr h)
        {
            IntPtr fg = Native.GetForegroundWindow();
            uint pid, me = Native.GetCurrentThreadId();
            uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out pid);
            if (fgThread != 0 && fgThread != me)
            {
                Native.AttachThreadInput(me, fgThread, true);
                Native.BringWindowToTop(h);
                Native.SetForegroundWindow(h);
                Native.AttachThreadInput(me, fgThread, false);
            }
            else Native.SetForegroundWindow(h);
        }

        protected override void OnActivated(EventArgs e) { base.OnActivated(e); activatedOnce = true; }

        // Alt+F4 只收起來，不要真的關掉（關掉後 Windows 鍵就沒反應了）
        public bool AllowClose;
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideLauncher(); return; }
            base.OnFormClosing(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!menuOpen && activatedOnce) HideLauncher();
        }

        double Now() { return clock.Elapsed.TotalMilliseconds; }

        void StartTimer()
        {
            if (timer.Enabled) return;
            lastTick = Now();
            timer.Interval = 1;
            if (!hiRes) { Native.timeBeginPeriod(1); hiRes = true; }
            timer.Start();
        }

        void StopTimer()
        {
            timer.Stop();
            if (hiRes) { Native.timeEndPeriod(1); hiRes = false; }
        }

        void StartContentFade()
        {
            if (!Style.Animate) return;
            contentFadeStart = Now();
            layerDirty = true;
            StartTimer();
        }

        static double EaseOut(double t) { t = Math.Max(0, Math.Min(1, t)); return 1 - Math.Pow(1 - t, 4); }
        static double EaseIn(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t; }

        void Tick()
        {
            double now = Now(), dt = Math.Max(1, now - lastTick);
            lastTick = now;
            bool busy = false;

            bool full = phase != Phase.None;
            if (phase == Phase.Opening)
            {
                double t = (now - phaseStart) / OpenMs;
                Location = new Point(finalLocation.X, finalLocation.Y + (int)Math.Round(fromOffset * (1 - EaseOut(t))));
                if (t >= 1)
                {
                    phase = Phase.None;
                    Location = finalLocation;
                    KeepTopmost(); // 滑完了，回到所有最上層視窗的最前面
                    search.Visible = true;
                    search.Focus();
                    search.SelectionStart = search.Text.Length;
                }
                busy = true;
            }
            else if (phase == Phase.Closing)
            {
                double t = (now - phaseStart) / CloseMs;
                int end = S(44);
                Location = new Point(finalLocation.X, finalLocation.Y + (int)Math.Round(fromOffset + (end - fromOffset) * EaseIn(t)));
                if (t >= 1) { FinishHide(); StopTimer(); return; }
                busy = true;
            }

            // 平滑捲動
            if (Math.Abs(scrollTarget - scrollPos) > 0.5f)
            {
                float k = (float)(1 - Math.Exp(-dt / 55.0));
                scrollPos += (scrollTarget - scrollPos) * k;
                if (Math.Abs(scrollTarget - scrollPos) <= 0.5f) scrollPos = scrollTarget;
                Invalidate(Viewport);
                busy = true;
            }

            // 反白漸變
            foreach (Hit h in hoverAlpha.Keys.ToList())
            {
                float target = h == hover ? 1f : 0f, cur = hoverAlpha[h];
                float step = (float)(dt / 110.0);
                float next = target > cur ? Math.Min(target, cur + step) : Math.Max(target, cur - step);
                if (next <= 0 && target <= 0) hoverAlpha.Remove(h);
                else hoverAlpha[h] = next;
                if (next != cur) InvalidateHit(h); // 先更新透明度再重畫
                if (next != target) busy = true;
            }

            if (contentFadeStart >= 0)
            {
                if (now - contentFadeStart >= FadeMs) contentFadeStart = -1;
                else busy = true;
                Invalidate(Viewport);
            }
            if (popupAnimStart >= 0)
            {
                if (now - popupAnimStart >= PopupMs)
                {
                    popupAnimStart = -1;
                    if (popupClosing) { popupCat = -1; popupClosing = false; BuildPopup(); }
                }
                else busy = true;
                full = true;
            }

            if (full) Invalidate();
            if (!busy) { StopTimer(); return; }
            // 立刻畫出這一幀，再等螢幕下一次更新：動畫速度跟螢幕更新頻率一致，不會忽快忽慢
            Update();
            Native.DwmFlush();
        }

        // ======================= 資料 =======================

        void RunOnUi(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        public void RefreshData()
        {
            // 載入完成要回到畫面執行緒更新，視窗必須先存在，否則完成通知會遺失、永遠停在「載入中」
            if (!IsHandleCreated && !IsDisposed) CreateHandle();
            int px = Math.Max(32, S(48));
            if (!loadingApps && (!appsLoaded || DateTime.Now - appsLoadedAt > TimeSpan.FromMinutes(3)))
            {
                loadingApps = true;
                AppCatalog.RunSta(delegate
                {
                    List<AppEntry> list = null;
                    List<AppEntry>[] grouped = null;
                    try
                    {
                        list = AppCatalog.LoadApps(px);
                        grouped = EmptyCats();
                        foreach (AppEntry a in list) grouped[AppCategories.Of(a)].Add(a);
                    }
                    finally
                    {
                        RunOnUi(delegate
                        {
                            loadingApps = false;
                            if (list == null) return;
                            List<AppEntry> old = apps;
                            apps = list;
                            cats = grouped;
                            appsById = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
                            foreach (AppEntry a in list) appsById[a.Id] = a;
                            appsLoaded = true;
                            appsLoadedAt = DateTime.Now;
                            Hit oldPressed = pressed;
                            BuildLayout();
                            BuildPopup();
                            RemapAfterRebuild(oldPressed);
                            Invalidate();
                            DropScaled(old.Select(a => a.Icon));
                            foreach (AppEntry a in old) if (a.Icon != null) a.Icon.Dispose();
                        });
                    }
                });
            }
            if (Style.ShowRecent && !loadingRecent)
            {
                loadingRecent = true;
                AppCatalog.RunSta(delegate
                {
                    List<RecentEntry> list = null;
                    try { list = AppCatalog.LoadRecent(30, px); }
                    finally
                    {
                        RunOnUi(delegate
                        {
                            loadingRecent = false;
                            if (list == null) return;
                            List<RecentEntry> old = recent;
                            recent = list;
                            Hit oldPressed = pressed;
                            BuildLayout();
                            RemapAfterRebuild(oldPressed);
                            Invalidate();
                            DropScaled(old.Select(r => r.Icon));
                            foreach (RecentEntry r in old) if (r.Icon != null) r.Icon.Dispose();
                        });
                    }
                });
            }
            else if (!Style.ShowRecent && recent.Count > 0)
            {
                DropScaled(recent.Select(r => r.Icon));
                foreach (RecentEntry r in recent) if (r.Icon != null) r.Icon.Dispose();
                recent = new List<RecentEntry>();
                BuildLayout();
                Invalidate();
            }
        }

        // 背景載入完成、項目重建後：把「按下中」和「滑鼠所在」對應到新的項目，按到一半的點擊才不會不見
        static bool SameHit(Hit a, Hit b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            return a.Kind == b.Kind && a.Cat == b.Cat && a.Row == b.Row && a.Action == b.Action
                && (a.App == null ? b.App == null : b.App != null && string.Equals(a.App.Id, b.App.Id, StringComparison.OrdinalIgnoreCase))
                && (a.Recent == null ? b.Recent == null : b.Recent != null && string.Equals(a.Recent.Path, b.Recent.Path, StringComparison.OrdinalIgnoreCase));
        }

        void RemapAfterRebuild(Hit oldPressed)
        {
            hoverAlpha.Clear();
            hover = null;
            pressed = null;
            if (!Visible) return;
            Hit now = HitAt(PointToClient(Cursor.Position));
            if (now != null && now.Action == "closepopup") now = null;
            pressed = SameHit(oldPressed, now) ? now : null;
            hover = now;
            if (now != null) hoverAlpha[now] = 1f;
        }

        // 開發用：把「開啟動畫用的預先畫好的畫面」存成圖片檢查
        public void SaveAnimationFrame(string path)
        {
            if (!appsLoaded) LoadNow();
            PrepareFrame();
            frame.Save(path, ImageFormat.Png);
        }

        // 開發用：量測每一幀要花多少時間（在記憶體裡畫，不會出現在螢幕上）
        public string Benchmark()
        {
            StringBuilder sb = new StringBuilder();
            if (!appsLoaded) LoadNow();
            foreach (int mode in new[] { 0, 2 })
            {
                allMode = mode;
                view = View.Home;
                scrollPos = scrollTarget = 0;
                BuildLayout();
                using (Bitmap target = new Bitmap(W, H, PixelFormat.Format32bppRgb))
                using (Graphics g = Graphics.FromImage(target))
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    RenderAll(g, new Rectangle(0, 0, W, H), true);
                    double full = sw.Elapsed.TotalMilliseconds;

                    int steps = 120, maxScroll = Math.Max(0, contentHeight - Viewport.Height);
                    double worst = 0;
                    sw.Restart();
                    for (int i = 0; i < steps; i++)
                    {
                        double t0 = sw.Elapsed.TotalMilliseconds;
                        scrollPos = maxScroll * (float)i / (steps - 1);
                        RenderAll(g, Viewport, false);
                        worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds - t0);
                    }
                    double scrollAvg = sw.Elapsed.TotalMilliseconds / steps;

                    scrollPos = 0;
                    RenderAll(g, Viewport, false);
                    List<Hit> sample = hits.Where(h => h.Kind != Kind.Link).Take(40).ToList();
                    sw.Restart();
                    foreach (Hit h in sample)
                    {
                        hover = h;
                        hoverAlpha[h] = 0.5f;
                        UpdateCacheFor(h);
                        RenderAll(g, Rectangle.Inflate(ClientRectOf(h), 2, 2), false);
                        hoverAlpha.Remove(h);
                    }
                    double hoverAvg = sample.Count == 0 ? 0 : sw.Elapsed.TotalMilliseconds / sample.Count;
                    hover = null;

                    using (GdiSurface fs = new GdiSurface(target))
                    {
                        IntPtr hdc = g.GetHdc();
                        sw.Restart();
                        for (int i = 0; i < steps; i++)
                        {
                            Native.BitBlt(hdc, 0, 0, W, H, fs.Dc, 0, 0, 0x00CC0020);
                            Native.AlphaBlend(hdc, 0, 0, W, H, fs.Dc, 0, 0, W, H, BlendOf(i / (float)steps));
                        }
                        double anim = sw.Elapsed.TotalMilliseconds / steps;
                        g.ReleaseHdc(hdc);
                        sb.AppendLine(string.Format("{0}: content={1}px  full render={2:0.0}ms  scroll frame avg={3:0.00}ms worst={4:0.0}ms  hover frame={5:0.00}ms  open/close frame={6:0.00}ms",
                            mode == 0 ? "類別檢視" : "清單檢視", contentHeight, full, scrollAvg, worst, hoverAvg, anim));
                    }
                }
            }
            allMode = Math.Max(0, Math.Min(2, AppSettings.GetInt("StartAllView", 0)));
            BuildLayout();
            return sb.ToString();
        }

        // 開發/測試用：同步載入
        public void LoadNow()
        {
            int px = Math.Max(32, S(48));
            apps = AppCatalog.LoadApps(px);
            cats = EmptyCats();
            foreach (AppEntry a in apps) { appsById[a.Id] = a; cats[AppCategories.Of(a)].Add(a); }
            appsLoaded = true;
            appsLoadedAt = DateTime.Now;
            if (Style.ShowRecent) recent = AppCatalog.LoadRecent(30, px);
            BuildLayout();
        }

        public int AppCount { get { return apps.Count; } }
        public string CategorySummary { get { return string.Join(" · ", Enumerable.Range(0, cats.Length).Where(i => cats[i].Count > 0).Select(i => AppCategories.Names[i] + " " + cats[i].Count).ToArray()); } }

        List<AppEntry> PinnedApps()
        {
            if (pins == null)
            {
                if (!appsLoaded) return new List<AppEntry>();
                string[] defaults = { "Microsoft Edge", "設定", "Settings", "檔案總管", "File Explorer" };
                pins = apps.Where(a => defaults.Contains(a.Name)).Select(a => a.Id).Distinct().ToList();
                SavePins();
            }
            List<AppEntry> list = new List<AppEntry>();
            foreach (string id in pins)
            {
                AppEntry a;
                if (appsById.TryGetValue(id, out a)) list.Add(a);
            }
            return list;
        }

        void SavePins() { AppSettings.Set("StartPins", string.Join("|", pins.ToArray())); }

        bool IsPinned(AppEntry a) { return pins != null && pins.Any(x => string.Equals(x, a.Id, StringComparison.OrdinalIgnoreCase)); }

        void TogglePin(AppEntry a)
        {
            if (pins == null) pins = new List<string>();
            if (IsPinned(a)) pins.RemoveAll(x => string.Equals(x, a.Id, StringComparison.OrdinalIgnoreCase));
            else pins.Add(a.Id);
            SavePins();
            BuildLayout();
            Invalidate();
        }

        // ======================= 版面 =======================

        void OnQueryChanged()
        {
            string q = search.Text.Trim();
            View next = q.Length > 0 ? View.Search : View.Home;
            bool switching = next != view;
            view = next;
            popupCat = -1;
            selected = 0;
            scrollPos = scrollTarget = 0;
            hover = pressed = null;
            hoverAlpha.Clear();
            BuildLayout();
            if (switching) StartContentFade();
            Invalidate();
        }

        void BuildLayout()
        {
            hits.Clear();
            fixedHits.Clear();
            headers.Clear();
            int cw = Viewport.Width, y = 0, hdr = S(40);
            if (view == View.Search) y = BuildSearch(cw);
            else if (view == View.Recent)
            {
                headers.Add(new KeyValuePair<string, int>("建議", y));
                AddLink("返回", "back", y, cw, true);
                y += hdr + S(4);
                int rowH = S(56);
                for (int i = 0; i < recent.Count; i++)
                {
                    hits.Add(new Hit { Kind = Kind.RecentItem, Recent = recent[i], Rect = new Rectangle(S(17), y, cw - S(34), rowH) });
                    y += rowH;
                }
            }
            else
            {
                // 已釘選
                headers.Add(new KeyValuePair<string, int>("已釘選", y));
                List<AppEntry> pinned = PinnedApps();
                int cols = 8, tileW = cw / cols, tileH = S(86);
                if (pinned.Count > cols * 2) AddLink(pinsExpanded ? "顯示較少" : "全部顯示", "pins", y, cw, false);
                y += hdr;
                int shown = pinsExpanded ? pinned.Count : Math.Min(pinned.Count, cols * 2);
                for (int i = 0; i < shown; i++)
                    hits.Add(new Hit { Kind = Kind.Pin, App = pinned[i], Rect = new Rectangle((i % cols) * tileW, y + (i / cols) * tileH, tileW, tileH) });
                y += Math.Max(1, (shown + cols - 1) / cols) * tileH + S(14);

                // 建議
                if (Style.ShowRecent)
                {
                    headers.Add(new KeyValuePair<string, int>("建議", y));
                    if (recent.Count > 6) AddLink("全部顯示", "recent", y, cw, false);
                    y += hdr;
                    int colW = (cw - S(34)) / 3, rowH = S(56);
                    int n = Math.Min(6, recent.Count);
                    for (int i = 0; i < n; i++)
                        hits.Add(new Hit { Kind = Kind.RecentItem, Recent = recent[i], Rect = new Rectangle(S(17) + (i % 3) * colW, y + (i / 3) * rowH, colW, rowH) });
                    y += Math.Max(1, (n + 2) / 3) * rowH + S(18);
                }

                // 全部
                headers.Add(new KeyValuePair<string, int>(appsLoaded ? "全部" : "全部（載入中…）", y));
                AddLink("檢視：" + new[] { "類別", "格線", "清單" }[allMode], "view", y, cw, false, true);
                y += hdr + S(4);
                if (allMode == 0) y = BuildCategories(cw, y);
                else if (allMode == 1)
                {
                    for (int i = 0; i < apps.Count; i++)
                        hits.Add(new Hit { Kind = Kind.GridApp, App = apps[i], Rect = new Rectangle((i % cols) * tileW, y + (i / cols) * tileH, tileW, tileH) });
                    y += ((apps.Count + cols - 1) / cols) * tileH;
                }
                else
                {
                    string last = null;
                    int rowH = S(40);
                    foreach (AppEntry a in apps)
                    {
                        string g = GroupLetter(a.Name);
                        if (g != last)
                        {
                            headers.Add(new KeyValuePair<string, int>("\u0001" + g, y)); // \u0001 = 小標題
                            y += S(34);
                            last = g;
                        }
                        hits.Add(new Hit { Kind = Kind.ListApp, App = a, Rect = new Rectangle(S(17), y, cw - S(34), rowH) });
                        y += rowH;
                    }
                }
            }
            contentHeight = y + S(12);
            frameDirty = layerDirty = cacheDirty = true;
            scrollTarget = Clamp(scrollTarget);
            scrollPos = Clamp(scrollPos);
            BuildFooter();
        }

        static string GroupLetter(string name)
        {
            if (string.IsNullOrEmpty(name)) return "#";
            char c = char.ToUpperInvariant(name[0]);
            if (c >= 'A' && c <= 'Z') return c.ToString();
            if (c < 128) return "#";
            return "中文";
        }

        // 類別卡片：每張卡 2×2 格，前三格是大圖示（直接開啟），第四格是 4 個小圖示（點了展開整個類別）
        int BuildCategories(int cw, int y)
        {
            int cols = Style.Large ? 5 : 4, card = S(154), avail = cw - 2 * HeaderX + S(2);
            int gap = Math.Max(S(12), (avail - cols * card) / Math.Max(1, cols - 1));
            int rowH = card + S(36) + S(18), i = 0;
            for (int c = 0; c < cats.Length; c++)
            {
                List<AppEntry> list = cats[c];
                if (list.Count == 0) continue;
                Rectangle r = new Rectangle(HeaderX - S(1) + (i % cols) * (card + gap), y + (i / cols) * rowH, card, card);
                hits.Add(new Hit { Kind = Kind.CatCard, Cat = c, Rect = new Rectangle(r.X, r.Y, r.Width, r.Height + S(34)) });
                int pad = S(12), slot = (card - 2 * pad) / 2;
                int bigCount = list.Count <= 4 ? Math.Min(4, list.Count) : 3;
                for (int k = 0; k < bigCount; k++)
                    hits.Add(new Hit { Kind = Kind.CatIcon, Cat = c, App = list[k], Rect = new Rectangle(r.X + pad + (k % 2) * slot, r.Y + pad + (k / 2) * slot, slot, slot) });
                i++;
            }
            return y + Math.Max(1, (i + cols - 1) / cols) * rowH;
        }

        void AddLink(string label, string action, int y, int cw, bool leftSide, bool dropdown = false)
        {
            Size ts = TextRenderer.MeasureText(label, font);
            int w = ts.Width + S(dropdown || !leftSide ? 34 : 30), h = S(28);
            int x = leftSide ? cw - HeaderX - w + S(10) : cw - HeaderX - w + S(10);
            hits.Add(new Hit { Kind = Kind.Link, Action = action + (dropdown ? ":dd" : (leftSide ? ":back" : "")), Rect = new Rectangle(x, y + (S(40) - h) / 2, w, h) });
        }

        int BuildSearch(int cw)
        {
            string q = search.Text.Trim();
            results = new List<SearchResult>();
            List<AppEntry> found = apps.Select(a => new { a, rank = Rank(a.Name, q) }).Where(x => x.rank >= 0)
                .OrderBy(x => x.rank).ThenBy(x => x.a.Name.Length).Select(x => x.a).Take(9).ToList();
            List<RecentEntry> files = recent.Where(r => r.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).Take(5).ToList();
            bool bestIsApp = found.Count > 0;
            if (bestIsApp) results.Add(new SearchResult { Section = "最佳比對", App = found[0] });
            else if (files.Count > 0) results.Add(new SearchResult { Section = "最佳比對", Recent = files[0] });
            foreach (AppEntry a in found.Skip(bestIsApp ? 1 : 0)) results.Add(new SearchResult { Section = "應用程式", App = a });
            foreach (RecentEntry r in files.Skip(bestIsApp ? 0 : 1)) results.Add(new SearchResult { Section = "文件", Recent = r });
            results.Add(new SearchResult { Section = "搜尋", Query = q });
            if (selected >= results.Count) selected = results.Count - 1;

            // 左欄：結果清單（太長時可以捲動）；右欄：詳細資料（固定）
            int leftW = (int)(cw * 0.48), y = 0;
            string lastSection = null;
            for (int i = 0; i < results.Count; i++)
            {
                SearchResult r = results[i];
                if (r.Section != lastSection)
                {
                    headers.Add(new KeyValuePair<string, int>("\u0001" + r.Section, y));
                    y += S(32);
                    lastSection = r.Section;
                }
                int h = i == 0 ? S(64) : S(44);
                hits.Add(new Hit { Kind = Kind.SearchRow, Row = i, App = r.App, Recent = r.Recent, Rect = new Rectangle(0, y, leftW, h) });
                y += h + S(2);
            }
            // 右欄的動作按鈕（固定位置）
            SearchResult cur = results.Count > 0 ? results[Math.Max(0, selected)] : null;
            if (cur != null)
            {
                Rectangle d = DetailRect();
                int ay = d.Y + S(196);
                List<KeyValuePair<string, string>> acts = new List<KeyValuePair<string, string>>();
                if (cur.App != null)
                {
                    acts.Add(new KeyValuePair<string, string>("", "開啟"));
                    acts.Add(new KeyValuePair<string, string>("", "以系統管理員身分執行"));
                    acts.Add(new KeyValuePair<string, string>(IsPinned(cur.App) ? "" : "", IsPinned(cur.App) ? "從開始取消釘選" : "釘選到開始"));
                }
                else if (cur.Recent != null)
                {
                    acts.Add(new KeyValuePair<string, string>("", "開啟"));
                    acts.Add(new KeyValuePair<string, string>("", "開啟檔案位置"));
                }
                else acts.Add(new KeyValuePair<string, string>("", "用 Windows 搜尋"));
                foreach (KeyValuePair<string, string> a in acts)
                {
                    fixedHits.Add(new Hit { Kind = Kind.Action, Action = a.Key + "|" + a.Value, Rect = new Rectangle(d.X + S(16), ay, d.Width - S(32), S(38)) });
                    ay += S(40);
                }
            }
            return y;
        }

        // 搜尋結果用方向鍵選到畫面外時，捲過去
        void EnsureSelectedVisible()
        {
            foreach (Hit h in hits)
                if (h.Kind == Kind.SearchRow && h.Row == selected) { EnsureVisible(h.Rect); return; }
        }

        Rectangle DetailRect()
        {
            Rectangle vp = Viewport;
            int leftW = (int)(vp.Width * 0.48);
            return new Rectangle(vp.X + leftW + S(16), vp.Y, vp.Width - leftW - S(16), Math.Min(vp.Height, S(420)));
        }

        static int Rank(string name, string q)
        {
            if (name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) return 0;
            int i = name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase);
            if (i < 0) return -1;
            if (i == 0) return 0;
            char before = name[i - 1];
            return before == ' ' || before == '-' || before == '(' || before == '_' ? 1 : 2;
        }

        void BuildFooter()
        {
            int b = S(40), y = H - FooterH + (FooterH - b) / 2;
            fixedHits.RemoveAll(h => h.Kind == Kind.Footer);
            fixedHits.Add(new Hit { Kind = Kind.Footer, Action = "user", Rect = new Rectangle(S(52), y, S(240), b) });
            fixedHits.Add(new Hit { Kind = Kind.Footer, Action = "power", Rect = new Rectangle(W - S(52) - b + S(8), y, b, b) });
        }

        // 類別資料夾
        Rectangle PopupRect()
        {
            if (popupCat < 0) return Rectangle.Empty;
            int count = cats[popupCat].Count, cols = Style.Large ? 6 : 5, tile = S(96), tileH = S(88);
            int w = cols * tile + S(48);
            int rows = (count + cols - 1) / cols;
            int h = Math.Min(H - S(150), S(78) + rows * tileH + S(16));
            return new Rectangle((W - w) / 2, Math.Max(S(70), (H - FooterH - h) / 2), w, h);
        }

        void BuildPopup()
        {
            popupHits.Clear();
            if (popupCat < 0) return;
            Rectangle r = PopupRect();
            int cols = Style.Large ? 6 : 5, tile = S(96), tileH = S(88);
            List<AppEntry> list = cats[popupCat];
            for (int i = 0; i < list.Count; i++)
                popupHits.Add(new Hit { Kind = Kind.PopupApp, App = list[i], Rect = new Rectangle(r.X + S(24) + (i % cols) * tile, r.Y + S(66) + (i / cols) * tileH, tile, tileH) });
        }

        void OpenPopup(int cat)
        {
            popupCat = cat;
            popupScroll = 0;
            popupClosing = false;
            BuildPopup();
            popupAnimStart = Style.Animate ? Now() : -1;
            hover = null;
            StartTimer();
            Invalidate();
        }

        void ClosePopup()
        {
            if (popupCat < 0 || popupClosing) return; // 已經在關了，不要重新開始關閉動畫
            if (!Style.Animate) { popupCat = -1; BuildPopup(); Invalidate(); return; }
            popupClosing = true;
            popupAnimStart = Now();
            StartTimer();
        }

        float PopupProgress()
        {
            if (popupCat < 0) return 0;
            if (popupAnimStart < 0) return 1;
            double t = (Now() - popupAnimStart) / PopupMs;
            return (float)(popupClosing ? 1 - EaseIn(t) : EaseOut(t));
        }

        // ======================= 捲動 =======================

        float Clamp(float v) { return Math.Max(0, Math.Min(v, Math.Max(0, contentHeight - Viewport.Height))); }

        void Wheel(int delta)
        {
            if (popupCat >= 0)
            {
                Rectangle r = PopupRect();
                int max = Math.Max(0, popupHits.Count == 0 ? 0 : popupHits.Max(h => h.Rect.Bottom) + S(16) - r.Bottom);
                popupScroll = Math.Max(0, Math.Min(max, popupScroll - delta * scale * 0.8f));
                Invalidate();
                return;
            }
            scrollTarget = Clamp(scrollTarget - delta * scale);
            StartTimer();
        }

        void EnsureVisible(Rectangle r)
        {
            int vh = Viewport.Height;
            if (r.Top < scrollTarget) scrollTarget = r.Top;
            else if (r.Bottom > scrollTarget + vh) scrollTarget = r.Bottom - vh;
            scrollTarget = Clamp(scrollTarget);
            StartTimer();
        }

        // ======================= 命中測試 =======================

        Rectangle ToClient(Rectangle r)
        {
            Rectangle vp = Viewport;
            return new Rectangle(vp.X + r.X, vp.Y + r.Y - (int)Math.Round(scrollPos), r.Width, r.Height);
        }

        Hit HitAt(Point p)
        {
            if (phase != Phase.None) return null;
            if (popupCat >= 0)
            {
                Rectangle pr = PopupRect();
                if (!pr.Contains(p)) return new Hit { Kind = Kind.Link, Action = "closepopup" };
                foreach (Hit h in popupHits)
                {
                    Rectangle r = h.Rect;
                    r.Offset(0, -(int)popupScroll);
                    if (r.Contains(p) && r.Top >= pr.Top + S(56) - S(4)) return h;
                }
                return null;
            }
            foreach (Hit h in fixedHits) if (h.Rect.Contains(p)) return h;
            Rectangle vp = Viewport;
            if (!vp.Contains(p)) return null;
            // 先找最小（最上層）的：類別卡片裡的圖示優先於卡片本身
            Hit best = null;
            foreach (Hit h in hits)
                if (ToClient(h.Rect).Contains(p) && (best == null || h.Kind == Kind.CatIcon || h.Kind == Kind.Link)) best = h;
            return best;
        }

        // ======================= 繪圖 =======================

        const TextFormatFlags One = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (phase != Phase.None && behind != null)
            {
                // 動畫中：完整畫面只在開始時畫一次，之後每一幀只貼「背後畫面」再半透明疊上去
                // 畫面在動畫開始前就準備好了；動畫中途資料更新也不重畫（避免卡一下），動畫結束後自然會顯示最新內容
                if (frameSurf == null || frameSurf.Width != W || frameSurf.Height != H) PrepareFrame();
                if (behindSurf == null) behindSurf = new GdiSurface(behind);
                int sx = Left - behindRect.X, sy = Top - behindRect.Y;
                IntPtr hdc = g.GetHdc();
                try
                {
                    if (sx >= 0 && sy >= 0 && sx + W <= behindSurf.Width && sy + H <= behindSurf.Height)
                        Native.BitBlt(hdc, 0, 0, W, H, behindSurf.Dc, sx, sy, 0x00CC0020 /*SRCCOPY*/);
                    else Native.BitBlt(hdc, 0, 0, W, H, IntPtr.Zero, 0, 0, 0x00000042 /*BLACKNESS*/);
                    Native.AlphaBlend(hdc, 0, 0, W, H, frameSurf.Dc, 0, 0, W, H, BlendOf(PhaseAlpha()));
                }
                finally { g.ReleaseHdc(hdc); }
                return;
            }
            RenderAll(g, e.ClipRectangle, false);
        }

        // 把整個開始功能表先畫成一張圖，開啟 / 關閉動畫的每一幀只要貼這張圖
        void PrepareFrame()
        {
            if (W <= 0 || H <= 0) return;
            if (frame == null || frame.Width != W || frame.Height != H)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(W, H, PixelFormat.Format32bppRgb);
            }
            using (Graphics fg = Graphics.FromImage(frame)) RenderAll(fg, new Rectangle(0, 0, W, H), true);
            if (frameSurf != null) frameSurf.Dispose();
            frameSurf = new GdiSurface(frame);
            frameDirty = false;
        }

        // BLENDFUNCTION：AC_SRC_OVER、整張圖同一個透明度
        static uint BlendOf(float a) { return BlendOf(a, false); }

        static uint BlendOf(float a, bool perPixelAlpha)
        {
            int v = (int)Math.Round(Math.Max(0f, Math.Min(1f, a)) * 255);
            return (uint)(v << 16) | (perPixelAlpha ? 0x01000000u /*AC_SRC_ALPHA*/ : 0u);
        }

        // clip：這次要重畫的範圍（只重畫需要的地方，滑鼠移過時就不用整個視窗重畫）
        void RenderAll(Graphics g, Rectangle clip, bool forFrame)
        {
            if (bg != null) g.DrawImage(bg, clip, clip, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // 搜尋框圖示與（動畫中）文字
            Rectangle sb = SearchBox;
            Txt(g, "", glyphFont, new Rectangle(sb.X + S(12), sb.Y, S(28), sb.Height), accentGlyph,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (!search.Visible)
                Txt(g, search.Text.Length > 0 ? search.Text : "搜尋應用程式、設定和文件", searchFont,
                    new Rectangle(search.Left - 1, sb.Y, search.Width, sb.Height), search.Text.Length > 0 ? text : sub,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            // 內容：先畫在透明的快取圖上，這裡只要把看得到的那一段貼上去（切換畫面時淡入並微微上移）
            Rectangle vp = Viewport, vclip = Rectangle.Intersect(vp, clip);
            if (!vclip.IsEmpty && vp.Width > 0 && vp.Height > 0)
            {
                int scroll = (int)Math.Round(scrollPos);
                EnsureCache(scroll);
                foreach (Point s in pendingStrips) RenderCache(s.X, s.Y);
                pendingStrips.Clear();
                double t = !forFrame && contentFadeStart >= 0 ? EaseOut((Now() - contentFadeStart) / FadeMs) : 1;
                int rise = (int)Math.Round((1 - t) * S(10));
                Rectangle dst = rise > 0 ? new Rectangle(vp.X, vp.Y + rise, vp.Width, vp.Height - rise) : vclip;
                int sx = dst.X - vp.X, sy = scroll - cacheTop + (dst.Y - rise - vp.Y);
                if (dst.Width > 0 && dst.Height > 0 && sx >= 0 && sy >= 0 && sx + dst.Width <= cache.Width && sy + dst.Height <= cache.Height)
                {
                    if (forFrame)
                    {
                        // 畫進記憶體圖片（開啟動畫用）時，GDI 的半透明混合在這種圖上會算錯（半透明的卡片變黑），改用 GDI+ 貼；只做一次，慢一點沒關係
                        g.DrawImage(cache.Bitmap, dst, new Rectangle(sx, sy, dst.Width, dst.Height), GraphicsUnit.Pixel);
                    }
                    else
                    {
                        IntPtr hdc = g.GetHdc();
                        try { Native.AlphaBlend(hdc, dst.X, dst.Y, dst.Width, dst.Height, cache.Dc, sx, sy, dst.Width, dst.Height, BlendOf((float)t, true)); }
                        finally { g.ReleaseHdc(hdc); }
                    }
                }
                DrawScrollbar(g);
                if (view == View.Search) DrawDetail(g, 0, 0);
            }
            if (clip.IntersectsWith(new Rectangle(0, H - FooterH, W, FooterH))) DrawFooter(g);
            if (popupCat >= 0) DrawPopup(g);
        }

        // 確保快取涵蓋目前看得到的範圍。捲動時把舊的像素整塊搬移，只補畫新露出來的那一條
        void EnsureCache(int scroll)
        {
            Rectangle vp = Viewport;
            int w = Math.Max(1, vp.Width), vh = Math.Max(1, vp.Height);
            int total = Math.Max(contentHeight, vh);
            int bandH = Math.Min(total, Math.Max(vh * 4, S(3200)));
            if (cache == null || cache.Width != w || cache.Height != bandH)
            {
                if (cache != null) cache.Dispose();
                cache = new GdiDib(w, bandH);
                cacheDirty = true;
            }
            int maxTop = Math.Max(0, total - bandH);
            int want = Math.Max(0, Math.Min(maxTop, scroll - (bandH - vh) / 2));
            if (cacheDirty)
            {
                pendingStrips.Clear(); // 整張重畫，等著的小段就不用了
                cacheTop = want;
                RenderCache(cacheTop, cacheTop + bandH);
                cacheDirty = false;
                return;
            }
            // 還在快取範圍裡、離邊緣也夠遠：什麼都不用做
            bool nearTop = scroll - cacheTop < vh / 2 && cacheTop > 0;
            bool nearBottom = cacheTop + bandH - (scroll + vh) < vh / 2 && cacheTop < maxTop;
            if (scroll >= cacheTop && scroll + vh <= cacheTop + bandH && !nearTop && !nearBottom) return;
            int delta = want - cacheTop;
            if (delta == 0) return;
            if (Math.Abs(delta) >= bandH) { cacheTop = want; RenderCache(cacheTop, cacheTop + bandH); return; }
            Native.GdiFlush();
            long stride = cache.Stride;
            if (delta > 0) Native.MoveMemory(cache.Bits, new IntPtr(cache.Bits.ToInt64() + delta * stride), new UIntPtr((ulong)((bandH - delta) * stride)));
            else Native.MoveMemory(new IntPtr(cache.Bits.ToInt64() + (-delta) * stride), cache.Bits, new UIntPtr((ulong)((bandH + delta) * stride)));
            cacheTop = want;
            if (delta > 0) RenderCache(cacheTop + bandH - delta, cacheTop + bandH);
            else RenderCache(cacheTop, cacheTop - delta);
        }

        // 重畫快取裡內容座標 y0~y1 這一段（先清成透明再畫）
        void RenderCache(int y0, int y1)
        {
            if (cache == null) return;
            int a = Math.Max(y0, cacheTop), b = Math.Min(y1, cacheTop + cache.Height);
            if (b <= a) return;
            Rectangle r = new Rectangle(0, a - cacheTop, cache.Width, b - a);
            using (Graphics g = Graphics.FromImage(cache.Bitmap))
            {
                g.SetClip(r);
                g.CompositingMode = CompositingMode.SourceCopy;
                using (SolidBrush clear = new SolidBrush(Color.Transparent)) g.FillRectangle(clear, r);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawContent(g, 0, 0, r, cacheTop);
            }
        }

        // 某個項目的樣子變了（反白、按下）：只重畫快取裡它那一小段
        void UpdateCacheFor(Hit h)
        {
            if (h == null || cache == null || cacheDirty) return;
            if (h.Kind == Kind.Footer || h.Kind == Kind.Action || h.Kind == Kind.PopupApp) return;
            Point s = new Point(h.Rect.Top - 2, h.Rect.Bottom + 2); // 同一列的項目共用同一段
            if (!pendingStrips.Contains(s)) pendingStrips.Add(s);
        }

        void DrawScrollbar(Graphics g)
        {
            if (view == View.Search || contentHeight <= Viewport.Height) return;
            Rectangle vp = Viewport;
            int th = Math.Max(S(36), vp.Height * vp.Height / contentHeight);
            int ty = (int)((vp.Height - th) * (scrollPos / Math.Max(1f, contentHeight - vp.Height)));
            FillRound(g, new Rectangle(vp.Right - S(3), vp.Y + ty, S(3), th), S(1.5f), Color.FromArgb(Style.Dark ? 90 : 110, sub));
        }

        // 用 GDI+ 畫字（灰階反鋸齒）：可以畫在透明的快取上，和 Windows 11 的 XAML 文字看起來一樣
        void Txt(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0 || r.Height <= 0) return;
            StringFormat sf;
            if (!formats.TryGetValue(flags, out sf))
            {
                sf = new StringFormat(StringFormat.GenericDefault);
                sf.Alignment = (flags & TextFormatFlags.HorizontalCenter) != 0 ? StringAlignment.Center : (flags & TextFormatFlags.Right) != 0 ? StringAlignment.Far : StringAlignment.Near;
                sf.LineAlignment = (flags & TextFormatFlags.VerticalCenter) != 0 ? StringAlignment.Center : (flags & TextFormatFlags.Bottom) != 0 ? StringAlignment.Far : StringAlignment.Near;
                sf.Trimming = (flags & TextFormatFlags.EndEllipsis) != 0 ? StringTrimming.EllipsisCharacter : StringTrimming.None;
                if ((flags & TextFormatFlags.SingleLine) != 0) sf.FormatFlags |= StringFormatFlags.NoWrap;
                sf.HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None;
                formats[flags] = sf;
            }
            if (g.TextRenderingHint != TextRenderingHint.AntiAliasGridFit) g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }

        float HoverOf(Hit h)
        {
            float a;
            return hoverAlpha.TryGetValue(h, out a) ? a : 0f;
        }

        void FillRound(Graphics g, Rectangle r, float radius, Color c)
        {
            if (c.A == 0 || r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Tile.Round(r, radius))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        void DrawHover(Graphics g, Hit h, Rectangle r)
        {
            float a = HoverOf(h);
            if (h == pressed) FillRound(g, r, S(6), pressFill);
            else if (a > 0) FillRound(g, r, S(6), Color.FromArgb((int)(hoverFill.A * a), hoverFill));
        }

        void DrawIcon(Graphics g, Bitmap icon, Rectangle r, bool pressedDown)
        {
            if (icon == null)
            {
                if (pressedDown) r.Inflate(-Math.Max(1, r.Width / 14), -Math.Max(1, r.Height / 14));
                FillRound(g, r, r.Width / 5f, Color.FromArgb(60, sub));
                return;
            }
            Bitmap s = Scaled(icon, r.Width);
            if (pressedDown)
            {
                r.Inflate(-Math.Max(1, r.Width / 14), -Math.Max(1, r.Height / 14));
                g.DrawImage(s, r);
            }
            else g.DrawImage(s, new Rectangle(r.X, r.Y, s.Width, s.Height), 0, 0, s.Width, s.Height, GraphicsUnit.Pixel);
        }

        // 每個圖示每種大小只縮放一次（高品質），之後直接貼上
        Bitmap Scaled(Bitmap src, int size)
        {
            Dictionary<int, Bitmap> bySize;
            if (!scaledIcons.TryGetValue(src, out bySize)) { bySize = new Dictionary<int, Bitmap>(); scaledIcons[src] = bySize; }
            Bitmap b;
            if (!bySize.TryGetValue(size, out b))
            {
                b = new Bitmap(Math.Max(1, size), Math.Max(1, size), PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height));
                }
                bySize[size] = b;
            }
            return b;
        }

        void DropScaled(IEnumerable<Bitmap> sources)
        {
            foreach (Bitmap src in sources)
            {
                Dictionary<int, Bitmap> bySize;
                if (src == null || !scaledIcons.TryGetValue(src, out bySize)) continue;
                foreach (Bitmap b in bySize.Values) b.Dispose();
                scaledIcons.Remove(src);
            }
        }

        // 某個項目目前在視窗上的位置（用來只重畫那一塊）
        Rectangle ClientRectOf(Hit h)
        {
            if (h == null) return Rectangle.Empty;
            if (h.Kind == Kind.Footer || h.Kind == Kind.Action) return h.Rect;
            if (h.Kind == Kind.PopupApp) { Rectangle r = h.Rect; r.Offset(0, -(int)popupScroll); return r; }
            return Rectangle.Intersect(ToClient(h.Rect), Viewport);
        }

        void InvalidateHit(Hit h)
        {
            UpdateCacheFor(h);
            Rectangle r = ClientRectOf(h);
            if (!r.IsEmpty) Invalidate(Rectangle.Inflate(r, 2, 2));
        }

        // ox, oy：內容座標 (0,0) 在 g 上的位置；clip：可見範圍
        void DrawContent(Graphics g, int ox, int oy, Rectangle clip, int scroll)
        {
            GraphicsState clipState = g.Save();
            g.SetClip(clip);
            Func<Rectangle, Rectangle> at = r => new Rectangle(ox + r.X, oy + r.Y - scroll, r.Width, r.Height);
            int cw = Viewport.Width;

            foreach (KeyValuePair<string, int> h in headers)
            {
                bool small = h.Key.Length > 0 && h.Key[0] == '\u0001';
                string label = small ? h.Key.Substring(1) : h.Key;
                Rectangle r = at(new Rectangle(small ? HeaderX : HeaderX, h.Value, cw - 2 * HeaderX, small ? S(32) : S(40)));
                if (r.Bottom < clip.Top || r.Top > clip.Bottom) continue;
                Txt(g, label, small ? font : headerFont, r, small ? sub : text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | One);
            }

            foreach (Hit h in hits)
            {
                Rectangle r = at(h.Rect);
                if (r.Bottom < clip.Top || r.Top > clip.Bottom) continue;
                switch (h.Kind)
                {
                    case Kind.Pin:
                    case Kind.GridApp:
                        DrawHover(g, h, r);
                        DrawIcon(g, h.App.Icon, new Rectangle(r.X + (r.Width - S(32)) / 2, r.Y + S(14), S(32), S(32)), h == pressed);
                        Txt(g, h.App.Name, font, new Rectangle(r.X + S(4), r.Y + S(52), r.Width - S(8), S(20)), text, TextFormatFlags.HorizontalCenter | One);
                        break;
                    case Kind.RecentItem:
                        DrawHover(g, h, r);
                        DrawIcon(g, h.Recent.Icon, new Rectangle(r.X + S(14), r.Y + (r.Height - S(32)) / 2, S(32), S(32)), h == pressed);
                        Txt(g, h.Recent.Name, font, new Rectangle(r.X + S(58), r.Y + S(9), r.Width - S(66), S(20)), text, One);
                        Txt(g, Ago(h.Recent.Time), smallFont, new Rectangle(r.X + S(58), r.Y + S(29), r.Width - S(66), S(18)), sub, One);
                        break;
                    case Kind.ListApp:
                        DrawHover(g, h, r);
                        DrawIcon(g, h.App.Icon, new Rectangle(r.X + S(14), r.Y + (r.Height - S(24)) / 2, S(24), S(24)), h == pressed);
                        Txt(g, h.App.Name, font, new Rectangle(r.X + S(52), r.Y, r.Width - S(60), r.Height), text, TextFormatFlags.VerticalCenter | One);
                        break;
                    case Kind.CatCard:
                        DrawCategoryCard(g, h, r);
                        break;
                    case Kind.CatIcon:
                        {
                            float a = HoverOf(h);
                            if (h == pressed) FillRound(g, r, S(6), pressFill);
                            else if (a > 0) FillRound(g, r, S(6), Color.FromArgb((int)(hoverFill.A * 1.6f * a), hoverFill));
                            int isz = S(36);
                            DrawIcon(g, h.App.Icon, new Rectangle(r.X + (r.Width - isz) / 2, r.Y + (r.Height - isz) / 2, isz, isz), h == pressed);
                            break;
                        }
                    case Kind.Link:
                        DrawLink(g, h, r);
                        break;
                    case Kind.SearchRow:
                        DrawSearchRow(g, h, r);
                        break;
                }
            }
            g.Restore(clipState);
        }

        void DrawCategoryCard(Graphics g, Hit h, Rectangle r)
        {
            Rectangle card = new Rectangle(r.X, r.Y, r.Width, r.Width);
            float a = HoverOf(h);
            Color fill = cardFill;
            if (a > 0) fill = Color.FromArgb(Math.Min(255, (int)(cardFill.A + (Style.Dark ? 10 : 60) * a)), Style.Dark ? Color.White : Color.White);
            FillRound(g, card, S(8), fill);
            using (GraphicsPath p = Tile.Round(card, S(8)))
            using (Pen pen = new Pen(cardLine)) g.DrawPath(pen, p);
            List<AppEntry> list = cats[h.Cat];
            if (list.Count > 4)
            {
                // 第四格：4 個小圖示
                int pad = S(12), slot = (card.Width - 2 * pad) / 2, mini = S(16), gap = S(6);
                Rectangle q = new Rectangle(card.X + pad + slot, card.Y + pad + slot, slot, slot);
                int total = mini * 2 + gap, sx = q.X + (q.Width - total) / 2, sy = q.Y + (q.Height - total) / 2;
                for (int k = 0; k < 4 && 3 + k < list.Count; k++)
                    DrawIcon(g, list[3 + k].Icon, new Rectangle(sx + (k % 2) * (mini + gap), sy + (k / 2) * (mini + gap), mini, mini), false);
            }
            Txt(g, AppCategories.Names[h.Cat], font, new Rectangle(r.X - S(10), card.Bottom + S(6), r.Width + S(20), S(24)), text, TextFormatFlags.HorizontalCenter | One);
        }

        void DrawLink(Graphics g, Hit h, Rectangle r)
        {
            DrawHover(g, h, r);
            string[] parts = h.Action.Split(':');
            string action = parts[0];
            bool dropdown = h.Action.EndsWith(":dd"), back = h.Action.EndsWith(":back");
            string label = action == "pins" ? (pinsExpanded ? "顯示較少" : "全部顯示")
                : action == "recent" ? "全部顯示"
                : action == "back" ? "返回"
                : action == "view" ? "檢視：" + new[] { "類別", "格線", "清單" }[allMode] : action;
            Color c = text;
            if (back)
            {
                Txt(g, "", glyphSmall, new Rectangle(r.X + S(6), r.Y, S(16), r.Height), c, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                Txt(g, label, font, new Rectangle(r.X + S(22), r.Y, r.Width - S(24), r.Height), c, TextFormatFlags.VerticalCenter | One);
            }
            else
            {
                Txt(g, label, font, new Rectangle(r.X + S(10), r.Y, r.Width - S(30), r.Height), c, TextFormatFlags.VerticalCenter | One);
                Txt(g, dropdown ? "" : "", glyphSmall, new Rectangle(r.Right - S(22), r.Y, S(14), r.Height), c,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
        }

        void DrawSearchRow(Graphics g, Hit h, Rectangle r)
        {
            bool sel = h.Row == selected;
            if (sel) FillRound(g, r, S(6), Color.FromArgb(Style.Dark ? 26 : 16, Style.Dark ? Color.White : Color.Black));
            else DrawHover(g, h, r);
            if (sel) FillRound(g, new Rectangle(r.X + S(1), r.Y + r.Height / 2 - S(8), S(3), S(16)), S(1.5f), accentGlyph);
            SearchResult res = results[h.Row];
            bool big = h.Row == 0;
            int isz = big ? S(32) : S(24);
            Rectangle ir = new Rectangle(r.X + S(14), r.Y + (r.Height - isz) / 2, isz, isz);
            string name, kind;
            if (res.App != null) { DrawIcon(g, res.App.Icon, ir, h == pressed); name = res.App.Name; kind = "應用程式"; }
            else if (res.Recent != null) { DrawIcon(g, res.Recent.Icon, ir, h == pressed); name = res.Recent.Name; kind = "檔案"; }
            else
            {
                Txt(g, "", glyphFont, ir, sub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                name = "用 Windows 搜尋「" + res.Query + "」"; kind = null;
            }
            int tx = ir.Right + S(14);
            if (big && kind != null)
            {
                Txt(g, name, bigFont, new Rectangle(tx, r.Y + S(12), r.Right - tx - S(8), S(24)), text, One);
                Txt(g, kind, smallFont, new Rectangle(tx, r.Y + S(36), r.Right - tx - S(8), S(18)), sub, One);
            }
            else Txt(g, name, font, new Rectangle(tx, r.Y, r.Right - tx - S(8), r.Height), text, TextFormatFlags.VerticalCenter | One);
        }

        void DrawDetail(Graphics g, int dx, int dy)
        {
            if (results.Count == 0) return;
            SearchResult cur = results[Math.Max(0, Math.Min(selected, results.Count - 1))];
            Rectangle d = DetailRect();
            d.Offset(dx, dy);
            FillRound(g, d, S(8), cardFill);
            using (GraphicsPath p = Tile.Round(d, S(8)))
            using (Pen pen = new Pen(cardLine)) g.DrawPath(pen, p);
            Rectangle icon = new Rectangle(d.X + (d.Width - S(64)) / 2, d.Y + S(28), S(64), S(64));
            string name, kind;
            if (cur.App != null) { DrawIcon(g, cur.App.Icon, icon, false); name = cur.App.Name; kind = "應用程式"; }
            else if (cur.Recent != null) { DrawIcon(g, cur.Recent.Icon, icon, false); name = cur.Recent.Name; kind = "檔案 · " + Ago(cur.Recent.Time); }
            else
            {
                Txt(g, "", glyphHuge, icon, accentGlyph, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                name = cur.Query; kind = "用 Windows 搜尋";
            }
            Txt(g, name, bigFont, new Rectangle(d.X + S(16), icon.Bottom + S(14), d.Width - S(32), S(26)), text, TextFormatFlags.HorizontalCenter | One);
            Txt(g, kind, smallFont, new Rectangle(d.X + S(16), icon.Bottom + S(42), d.Width - S(32), S(20)), sub, TextFormatFlags.HorizontalCenter | One);
            using (Pen line = new Pen(cardLine)) g.DrawLine(line, d.X + S(16), d.Y + S(184), d.Right - S(16), d.Y + S(184));
            foreach (Hit h in fixedHits)
            {
                if (h.Kind != Kind.Action) continue;
                Rectangle r = h.Rect;
                r.Offset(dx, dy);
                DrawHover(g, h, r);
                string[] parts = h.Action.Split('|');
                Txt(g, parts[0], glyphSmall, new Rectangle(r.X + S(10), r.Y, S(20), r.Height), accentGlyph, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                Txt(g, parts[1], font, new Rectangle(r.X + S(40), r.Y, r.Width - S(48), r.Height), text, TextFormatFlags.VerticalCenter | One);
            }
        }

        void DrawFooter(Graphics g)
        {
            foreach (Hit f in fixedHits)
            {
                if (f.Kind != Kind.Footer) continue;
                DrawHover(g, f, f.Rect);
                if (f.Action == "user")
                {
                    int d = S(32);
                    Rectangle pic = new Rectangle(f.Rect.X + S(10), f.Rect.Y + (f.Rect.Height - d) / 2, d, d);
                    using (GraphicsPath circle = new GraphicsPath())
                    {
                        circle.AddEllipse(pic);
                        if (userPic != null)
                        {
                            Region old = g.Clip;
                            g.SetClip(circle, CombineMode.Intersect);
                            g.DrawImage(userPic, pic);
                            g.Clip = old;
                        }
                        else
                        {
                            using (SolidBrush b = new SolidBrush(WinSettings.Accent)) g.FillPath(b, circle);
                            Txt(g, userName.Length > 0 ? userName.Substring(0, 1) : "?", font, pic, Color.White,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        }
                    }
                    Txt(g, userName, font, new Rectangle(pic.Right + S(12), f.Rect.Y, f.Rect.Width - d - S(30), f.Rect.Height), text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                }
                else
                    Txt(g, "", glyphFont, f.Rect, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        void DrawPopup(Graphics g)
        {
            float p = PopupProgress();
            using (SolidBrush dim = new SolidBrush(Color.FromArgb((int)(90 * p), 0, 0, 0))) g.FillRectangle(dim, 0, 0, W, H);
            Rectangle r = PopupRect();
            int lift = (int)Math.Round((1 - p) * S(24));
            r.Offset(0, lift);
            Color panel = Style.Dark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
            // 用 p 讓面板在動畫初期偏向背景色，看起來像淡入
            FillRound(g, r, S(8), Color.FromArgb((int)(255 * Math.Min(1, p * 1.4)), panel));
            using (GraphicsPath path = Tile.Round(r, S(8)))
            using (Pen pen = new Pen(Color.FromArgb((int)(cardLine.A * 2 * p), Style.Dark ? Color.White : Color.Black))) g.DrawPath(pen, path);
            if (p < 0.35f) return;
            Txt(g, AppCategories.Names[popupCat], titleFont, new Rectangle(r.X + S(28), r.Y + S(14), r.Width - S(56), S(36)), text, TextFormatFlags.VerticalCenter | One);
            Region old = g.Clip;
            g.SetClip(new Rectangle(r.X, r.Y + S(56), r.Width, r.Height - S(60)));
            foreach (Hit h in popupHits)
            {
                Rectangle hr = h.Rect;
                hr.Offset(0, lift - (int)popupScroll);
                if (hr.Bottom < r.Top || hr.Top > r.Bottom) continue;
                DrawHover(g, h, hr);
                DrawIcon(g, h.App.Icon, new Rectangle(hr.X + (hr.Width - S(32)) / 2, hr.Y + S(14), S(32), S(32)), h == pressed);
                Txt(g, h.App.Name, font, new Rectangle(hr.X + S(4), hr.Y + S(52), hr.Width - S(8), S(20)), text, TextFormatFlags.HorizontalCenter | One);
            }
            g.Clip = old;
        }

        static string Ago(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "剛剛";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " 分鐘前";
            if (d.TotalHours < 24) return (int)d.TotalHours + " 小時前";
            if (d.TotalDays < 2) return "昨天";
            if (d.TotalDays < 7) return (int)d.TotalDays + " 天前";
            return t.ToString("M月d日");
        }

        // ======================= 滑鼠 =======================

        void SetHover(Hit h)
        {
            if (h != null && h.Action == "closepopup") h = null;
            if (h == hover) return;
            Hit old = hover;
            hover = h;
            if (h != null && !hoverAlpha.ContainsKey(h)) hoverAlpha[h] = Style.Animate ? 0f : 1f;
            if (!Style.Animate)
            {
                foreach (Hit k in hoverAlpha.Keys.ToList()) if (k != h) hoverAlpha.Remove(k);
            }
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            // 只重畫前後兩個項目，不整個視窗重畫
            InvalidateHit(old);
            InvalidateHit(h);
            if (Style.Animate) StartTimer();
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); SetHover(HitAt(e.Location)); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetHover(null); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Wheel(e.Delta); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Hit h = HitAt(e.Location);
            if (h != null && h.Action != "closepopup") { pressed = h; InvalidateHit(h); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Hit was = pressed;
            pressed = null;
            Hit h = HitAt(e.Location);
            InvalidateHit(was);
            if (h == null) return;
            if (h.Action == "closepopup") { ClosePopup(); return; }
            if (h != was) return;
            if (e.Button == MouseButtons.Right)
            {
                if (h.App != null) ShowAppMenu(h.App, e.Location);
                return;
            }
            if (e.Button == MouseButtons.Left) Activate(h, e.Location);
        }

        void Activate(Hit h, Point at)
        {
            switch (h.Kind)
            {
                case Kind.Pin:
                case Kind.GridApp:
                case Kind.ListApp:
                case Kind.CatIcon:
                case Kind.PopupApp:
                    Launch(h.App);
                    break;
                case Kind.RecentItem:
                    OpenTarget(h.Recent.Path);
                    break;
                case Kind.CatCard:
                    OpenPopup(h.Cat);
                    break;
                case Kind.SearchRow:
                    if (h.Row == selected) RunResult(results[h.Row], "開啟");
                    else { selected = h.Row; BuildLayout(); Invalidate(); }
                    break;
                case Kind.Action:
                    if (results.Count > 0) RunResult(results[Math.Max(0, selected)], h.Action.Split('|')[1]);
                    break;
                case Kind.Footer:
                    if (h.Action == "user") { OpenTarget("ms-settings:yourinfo"); }
                    else ShowPowerMenu(h.Rect);
                    break;
                case Kind.Link:
                    string a = h.Action.Split(':')[0];
                    if (a == "pins") { pinsExpanded = !pinsExpanded; BuildLayout(); StartContentFade(); }
                    else if (a == "recent") { view = View.Recent; scrollPos = scrollTarget = 0; hover = null; hoverAlpha.Clear(); BuildLayout(); StartContentFade(); }
                    else if (a == "back") GoHome();
                    else if (a == "view") ShowViewMenu(ToClient(h.Rect));
                    Invalidate();
                    break;
            }
        }

        void GoHome()
        {
            view = View.Home;
            scrollPos = scrollTarget = 0;
            hover = null;
            hoverAlpha.Clear();
            BuildLayout();
            StartContentFade();
            Invalidate();
        }

        void Launch(AppEntry a)
        {
            try { AppCatalog.Launch(a); }
            catch (Exception ex) { MessageBox.Show("無法開啟：" + ex.Message, "開始"); }
            HideLauncher();
        }

        void OpenTarget(string target)
        {
            try { AppCatalog.Open(target); }
            catch (Exception ex) { MessageBox.Show("無法開啟：" + ex.Message, "開始"); }
            HideLauncher();
        }

        void RunResult(SearchResult r, string action)
        {
            try
            {
                if (r.App != null)
                {
                    if (action == "以系統管理員身分執行")
                    {
                        System.Diagnostics.Process.Start(new ProcessStartInfo("shell:AppsFolder\\" + r.App.Id) { UseShellExecute = true, Verb = "runas" });
                        HideLauncher();
                    }
                    else if (action == "釘選到開始" || action == "從開始取消釘選") { TogglePin(r.App); BuildLayout(); Invalidate(); }
                    else Launch(r.App);
                }
                else if (r.Recent != null)
                {
                    if (action == "開啟檔案位置")
                    {
                        // 最近使用的項目是捷徑，要找出真正的檔案位置
                        string target = AppCatalog.LinkTarget(r.Recent.Path);
                        if (target != null && (System.IO.File.Exists(target) || System.IO.Directory.Exists(target)))
                            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + target + "\"");
                        else MessageBox.Show("找不到「" + r.Recent.Name + "」原本的位置，檔案可能已經被移動或刪除。", "開始");
                        HideLauncher();
                    }
                    else OpenTarget(r.Recent.Path);
                }
                else OpenTarget("search-ms:query=" + Uri.EscapeDataString(r.Query));
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) MessageBox.Show("無法開啟：" + ex.Message, "開始"); // 1223 = 在系統管理員確認視窗按了「否」
            }
            catch (Exception ex) { MessageBox.Show("無法開啟：" + ex.Message, "開始"); }
        }

        // ======================= 選單 =======================

        ContextMenuStrip NewMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip { Font = font, Renderer = new ThemedMenuRenderer(Style.Dark), ShowImageMargin = false, Padding = new Padding(S(4)) };
            m.Opened += delegate
            {
                menuOpen = true;
                int round = 2;
                try { Native.DwmSetWindowAttribute(m.Handle, 33, ref round, 4); } catch { }
            };
            m.Closed += delegate
            {
                menuOpen = false;
                BeginInvoke((Action)delegate { if (Visible && Form.ActiveForm != this) HideLauncher(); });
            };
            return m;
        }

        ToolStripMenuItem Item(string label, Action click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(label, null, delegate { click(); });
            it.Padding = new Padding(S(4), S(5), S(16), S(5));
            return it;
        }

        void ShowAppMenu(AppEntry app, Point at)
        {
            ContextMenuStrip m = NewMenu();
            bool pinned = IsPinned(app);
            m.Items.Add(Item(pinned ? "從開始取消釘選" : "釘選到開始", delegate { TogglePin(app); }));
            if (pinned)
            {
                // 只在「看得到的釘選」之間移動（已解除安裝的程式留在清單最後，不影響順序）
                List<string> vis = PinnedApps().Select(a => a.Id).ToList();
                int idx = vis.FindIndex(x => string.Equals(x, app.Id, StringComparison.OrdinalIgnoreCase));
                Action<int> moveTo = target =>
                {
                    vis.RemoveAt(idx);
                    vis.Insert(target, app.Id);
                    pins = vis.Concat(pins.Where(id => !vis.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
                    SavePins();
                    BuildLayout();
                    Invalidate();
                };
                if (idx > 0) m.Items.Add(Item("移到最前面", delegate { moveTo(0); }));
                if (idx > 0) m.Items.Add(Item("往前移", delegate { moveTo(idx - 1); }));
                if (idx >= 0 && idx < vis.Count - 1) m.Items.Add(Item("往後移", delegate { moveTo(idx + 1); }));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("以系統管理員身分執行", delegate { RunResult(new SearchResult { App = app }, "以系統管理員身分執行"); }));
            m.Show(this, at);
        }

        void ShowViewMenu(Rectangle anchor)
        {
            ContextMenuStrip m = NewMenu();
            string[] names = { "類別", "格線", "清單" };
            for (int i = 0; i < 3; i++)
            {
                int mode = i;
                ToolStripMenuItem it = Item(names[i], delegate
                {
                    allMode = mode;
                    AppSettings.Set("StartAllView", mode);
                    BuildLayout();
                    StartContentFade();
                    Invalidate();
                });
                it.Checked = i == allMode;
                m.Items.Add(it);
            }
            m.ShowImageMargin = true;
            m.Show(this, new Point(anchor.Right, anchor.Bottom + S(2)), ToolStripDropDownDirection.BelowLeft);
        }

        void ShowPowerMenu(Rectangle anchor)
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(Item("鎖定", delegate { HideLauncher(); Native.LockWorkStation(); }));
            m.Items.Add(Item("登出", delegate { HideLauncher(); Run("shutdown.exe", "/l"); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("睡眠", delegate { HideLauncher(); Application.SetSuspendState(PowerState.Suspend, false, false); }));
            m.Items.Add(Item("關機", delegate { HideLauncher(); Run("shutdown.exe", "/s /hybrid /t 0"); })); // 和開始功能表一樣用快速啟動的關機
            m.Items.Add(Item("重新啟動", delegate { HideLauncher(); Run("shutdown.exe", "/r /t 0"); }));
            m.Show(this, new Point(anchor.Right, anchor.Top - S(4)), ToolStripDropDownDirection.AboveLeft);
        }

        static void Run(string exe, string args)
        {
            try { System.Diagnostics.Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }

        // ======================= 鍵盤 =======================

        // 動畫期間搜尋框暫時隱藏，先把打的字收進來
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (search.Visible || e.Handled) return;
            if (e.KeyChar == '\b') { if (search.Text.Length > 0) search.Text = search.Text.Substring(0, search.Text.Length - 1); }
            else if (!char.IsControl(e.KeyChar)) search.Text += e.KeyChar;
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (search.Visible || e.Handled) return;
            if (e.KeyCode == Keys.Escape) { HideLauncher(); e.Handled = true; }
            // 開啟動畫期間按的 Enter / 方向鍵也要有作用（例如快速打「calc」再按 Enter）
            else if (phase == Phase.Opening) OnSearchKey(search, e);
        }

        void OnSearchKey(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    if (popupCat >= 0 && !popupClosing) ClosePopup();
                    else if (search.Text.Length > 0) search.Text = "";
                    else if (view == View.Recent) GoHome();
                    else HideLauncher();
                    break;
                case Keys.Down:
                    if (view == View.Search && results.Count > 0) { selected = Math.Min(results.Count - 1, selected + 1); BuildLayout(); EnsureSelectedVisible(); Invalidate(); }
                    else { scrollTarget = Clamp(scrollTarget + S(80)); StartTimer(); }
                    break;
                case Keys.Up:
                    if (view == View.Search && results.Count > 0) { selected = Math.Max(0, selected - 1); BuildLayout(); EnsureSelectedVisible(); Invalidate(); }
                    else { scrollTarget = Clamp(scrollTarget - S(80)); StartTimer(); }
                    break;
                case Keys.PageDown: scrollTarget = Clamp(scrollTarget + Viewport.Height); StartTimer(); break;
                case Keys.PageUp: scrollTarget = Clamp(scrollTarget - Viewport.Height); StartTimer(); break;
                case Keys.Enter:
                    if (view == View.Search && results.Count > 0) RunResult(results[Math.Max(0, selected)], "開啟");
                    break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                StopTimer();
                foreach (GdiSurface s in new[] { behindSurf, frameSurf }) if (s != null) s.Dispose();
                if (cache != null) cache.Dispose();
                foreach (StringFormat sf in formats.Values) sf.Dispose();
                foreach (Bitmap b in new[] { bg, behind, frame }) if (b != null) b.Dispose();
                DropScaled(scaledIcons.Keys.ToList());
                foreach (AppEntry a in apps) if (a.Icon != null) a.Icon.Dispose();
                foreach (RecentEntry r in recent) if (r.Icon != null) r.Icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
