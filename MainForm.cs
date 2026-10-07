// 主視窗：顏色排列 / 像素圖案 / 個人化色彩 / 工作列 / 滑鼠游標
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DesktopColorSorter
{
    // ======================= 小元件 =======================

    class Swatch : Control
    {
        public Color Value { get; private set; }
        public bool Selected;
        readonly float scale;

        public Swatch(Color c, float scale, int size)
        {
            Value = c; this.scale = scale;
            Size = new Size(size, size);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(1.5f, 1.5f, Width - 3, Height - 3);
            using (GraphicsPath p = Tile.Round(r, 5 * scale))
            {
                using (SolidBrush b = new SolidBrush(Value)) g.FillPath(b, p);
                if (Value.GetBrightness() > 0.85f) using (Pen pen = new Pen(Color.FromArgb(200, 204, 212))) g.DrawPath(pen, p);
                if (Selected)
                {
                    using (Pen outer = new Pen(Color.FromArgb(30, 34, 42), 2 * scale)) g.DrawPath(outer, p);
                    using (GraphicsPath inner = Tile.Round(RectangleF.Inflate(r, -3 * scale, -3 * scale), 3 * scale))
                    using (Pen pen = new Pen(Color.White, 2 * scale)) g.DrawPath(pen, inner);
                }
            }
        }
    }

    class SwatchPanel : FlowLayoutPanel
    {
        public event Action<Color> Picked;
        readonly List<Swatch> items = new List<Swatch>();

        public SwatchPanel(IList<Color> colors, float scale, int perRow)
        {
            int size = (int)(26 * scale), m = (int)(3 * scale);
            WrapContents = true;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Width = perRow * (size + 2 * m);
            Height = ((colors.Count + perRow - 1) / perRow) * (size + 2 * m);
            foreach (Color c in colors)
            {
                Swatch s = new Swatch(c, scale, size) { Margin = new Padding(m) };
                s.Click += delegate { SelectColor(s.Value); if (Picked != null) Picked(s.Value); };
                Controls.Add(s);
                items.Add(s);
            }
        }

        public void SelectColor(Color c)
        {
            foreach (Swatch s in items)
            {
                bool sel = Math.Abs(s.Value.R - c.R) + Math.Abs(s.Value.G - c.G) + Math.Abs(s.Value.B - c.B) < 6;
                if (sel != s.Selected) { s.Selected = sel; s.Invalidate(); }
            }
        }
    }

    class CursorPreview : Control
    {
        public CursorStyle Style;
        readonly float scale;

        public CursorPreview(float scale)
        {
            this.scale = scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Style == null) return;
            int half = Width / 2, canvas = (int)(56 * scale);
            Color[] bgs = { Color.FromArgb(238, 241, 246), Color.FromArgb(34, 38, 48) };
            for (int side = 0; side < 2; side++)
            {
                RectangleF r = new RectangleF(side * half + 2, 2, half - 6, Height - 4);
                using (GraphicsPath p = Tile.Round(r, 10 * scale))
                using (SolidBrush b = new SolidBrush(bgs[side])) g.FillPath(b, p);
                int i = 0;
                foreach (CursorKind k in new[] { CursorKind.Arrow, CursorKind.Hand, CursorKind.IBeam })
                {
                    Point hot;
                    using (Bitmap bmp = CursorArt.Render(Style, k, canvas, out hot))
                        g.DrawImage(bmp, r.X + 14 * scale + i * (r.Width - 28 * scale) / 3f, r.Y + (r.Height - canvas) / 2f, canvas, canvas);
                    i++;
                }
            }
        }
    }

    // 設定頁面排版小幫手：標題、一列一列的「名稱 + 控制項」、說明文字
    class PageBuilder
    {
        readonly Panel host;
        readonly float scale;
        readonly Font sectionFont, noteFont;
        int y;

        int S(float v) { return (int)Math.Round(v * scale); }

        public PageBuilder(Panel host, float scale, Font font)
        {
            this.host = host; this.scale = scale;
            sectionFont = new Font(font.FontFamily, 11.5f, FontStyle.Bold);
            noteFont = new Font(font.FontFamily, 9f);
            y = S(10);
        }

        public void Section(string title)
        {
            y += S(10);
            host.Controls.Add(new Label { Text = title, Font = sectionFont, AutoSize = true, Left = S(24), Top = y, ForeColor = Color.FromArgb(28, 32, 40) });
            y += S(38);
        }

        static int H(Control c) { return c.AutoSize ? c.PreferredSize.Height : c.Height; }
        // TextBox 的 AutoSize 只管高度，寬度要用設定的 Width（否則後面的說明文字會疊上去）
        static int W(Control c) { return c.AutoSize && !(c is TextBoxBase) ? c.PreferredSize.Width : c.Width; }

        public void Row(string label, params Control[] cs)
        {
            int rowH = S(34);
            foreach (Control c in cs) rowH = Math.Max(rowH, H(c));
            if (!string.IsNullOrEmpty(label))
                host.Controls.Add(new Label
                {
                    Text = label, AutoSize = false, Left = S(40), Top = y, Width = S(130), Height = rowH > S(50) ? S(34) : rowH,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(84, 90, 100)
                });
            int x = S(176);
            foreach (Control c in cs)
            {
                host.Controls.Add(c);
                c.Left = x;
                c.Top = y + (rowH - H(c)) / 2;
                x += W(c) + S(12);
            }
            y += rowH + S(10);
        }

        public void Note(string text)
        {
            int w = S(640);
            int h = TextRenderer.MeasureText(text, noteFont, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height + S(4);
            host.Controls.Add(new Label { Text = text, Font = noteFont, ForeColor = Color.FromArgb(120, 126, 136), AutoSize = false, Left = S(176), Top = y, Width = w, Height = h });
            y += h + S(10);
        }

        public void End()
        {
            host.Controls.Add(new Label { Left = 0, Top = y + S(16), Width = 1, Height = 1 });
        }
    }

    // ======================= 主視窗 =======================

    class MainForm : Form
    {
        const int HOTKEY_ID = 1;
        public static readonly string[] TabNames = { "顏色排列", "像素圖案", "個人化色彩", "工作列", "開始功能表", "滑鼠游標" };
        static readonly Color[] WindowsAccents = HexColors(
            "FFB900 FF8C00 F7630C CA5010 DA3B01 EF6950 D13438 FF4343 E74856 E81123 EA005E C30052 " +
            "E3008C BF0077 C239B3 9A0089 0078D7 0063B1 8E8CD8 6B69D6 8764B8 744DA9 B146C2 881798 " +
            "0099BC 2D7D9A 00B7C3 038387 00B294 018574 00CC6A 10893E 7A7574 5D5A58 68768A 515C6B " +
            "567C73 486860 498205 107C10 767676 4C4A48 69797E 4A5459 647C64 525E54 847545 7E735F");
        static readonly Color[] CursorColors = HexColors("FFFFFF 1E1E1E E81123 FF8C00 FFD400 10C060 00B7C3 0078D7 3A3AE8 8764B8 E3008C FF6FAF");

        readonly float scale;
        bool startHidden;
        readonly uint showMessage = Native.RegisterWindowMessage("DesktopColorSorter_Show");
        Panel[] pages;
        Button[] tabs;
        string[] pageStatus = new string[TabNames.Length];
        int currentPage;
        Panel header;
        Label status;
        ToolTip tip = new ToolTip();
        NotifyIcon tray;
        ToolStripMenuItem traySkinItem;
        bool hotkeyOk, reallyExit, scannedOnce, loading;
        bool arrangedThisSession;
        Snapshot snap;
        List<Button> arrangeButtons = new List<Button>();
        Button btnHide;

        // 顏色排列
        Panel colorPreview;
        RadioButton rbVertical, rbHorizontal;
        CheckBox chkGroup;
        // 像素圖案
        PatternPreview patternPreview;
        ComboBox cbSource;
        TextBox txtText;
        Button btnPickImage;
        CheckBox chkRemoveBg;
        Bitmap userImage;
        // 個人化色彩
        RadioButton rbSysLight, rbSysDark, rbAppLight, rbAppDark;
        CheckBox chkTransparency, chkAutoAccent, chkAccentStart, chkAccentTitle;
        SwatchPanel accentSwatches;
        Label lblAccent;
        // 工作列
        readonly TaskbarSkin skin = new TaskbarSkin();
        CheckBox chkSkin, chkAutoStart, chkTaskView, chkWidgets, chkAutoHide, chkSeconds;
        TrackBar trkOpacity, trkPos;
        Label lblOpacity, lblSkinFile;
        ComboBox cbFit, cbAlign, cbSearch;
        // 滑鼠游標
        ComboBox cbScheme, cbOutline, cbCursorSize;
        SwatchPanel cursorSwatches;
        CheckBox chkRainbow;
        CursorPreview cursorPreview;
        readonly CursorStyle cursorStyle = new CursorStyle();
        // 開始功能表
        StartPanel launcher;
        WinKeyHook winHook;
        bool startEnabled;
        CheckBox chkStartEnabled, chkStartRecent, chkStartButton, chkStartAnimate;
        TrackBar trkStartStrength, trkStartBlur;
        Label lblStartStrength, lblStartBlur, lblStartFile;
        ComboBox cbStartTone, cbStartSize;
        TextBox txtStartName;
        ToolStripMenuItem trayStartItem;
        // 工作列項目
        Label lblWidgetsNote;
        Button btnApplyTaskbar;
        bool taskbarPending;
        // 像素圖案動畫
        PatternAnimator animator;
        ComboBox cbAnim, cbAnimSpeed;
        List<string> patternIds = new List<string>();
        // 開發用（--render）：不裝鍵盤掛勾、不顯示工作列圖片和通知區域圖示
        readonly bool devMode;

        int S(float v) { return (int)Math.Round(v * scale); }

        static Color[] HexColors(string s)
        {
            return s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(h => Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16)))
                .ToArray();
        }

        public MainForm(bool startHidden, bool devMode)
        {
            this.startHidden = startHidden;
            this.devMode = devMode;
            AutoScaleMode = AutoScaleMode.None;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            Text = "桌面顏色整理器";
            Font = new Font("Microsoft JhengHei UI", 9.5f);
            ClientSize = new Size(S(980), S(660));
            MinimumSize = new Size(S(900), S(520));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 246, 248);
            Icon = AppIcon.Create();

            // ---- 頂列：分頁 + 一鍵隱藏 ----
            header = new Panel { Dock = DockStyle.Top, Height = S(62), BackColor = Color.White };
            tabs = new Button[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int page = i;
                tabs[i] = new Button { Text = TabNames[i], Left = S(16) + i * S(108), Top = S(13), Width = S(102), Height = S(36), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
                tabs[i].FlatAppearance.BorderSize = 0;
                tabs[i].Font = new Font(Font, FontStyle.Bold);
                tabs[i].Click += delegate { ShowPage(page); };
                header.Controls.Add(tabs[i]);
            }
            btnHide = MakeButton("隱藏全部圖示", 0, S(13), S(140), false);
            btnHide.Click += delegate { ToggleHide(); };
            header.Controls.Add(btnHide);
            header.Layout += delegate { btnHide.Left = header.ClientSize.Width - S(16) - btnHide.Width; };
            header.Paint += (s, e) => DrawBottomLine(e.Graphics, header);

            status = new Label { Dock = DockStyle.Bottom, Height = S(30), Padding = new Padding(S(14), 0, S(14), 0),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(80, 86, 96), BackColor = Color.White };

            pages = new[] { BuildColorPage(), BuildPatternPage(), BuildPersonalPage(), BuildTaskbarPage(), BuildStartPage(), BuildCursorPage() };
            foreach (Panel p in pages) Controls.Add(p);
            Controls.Add(status);
            Controls.Add(header);

            BuildTray();
            InitSkin();
            InitStart();
            LoadTaskbar();
            LoadStart();
            ShowPage(0);

            VisibleChanged += delegate
            {
                if (Visible && !scannedOnce) { scannedOnce = true; BeginInvoke((Action)Scan); }
            };
            Shown += delegate
            {
                tip.SetToolTip(btnHide, hotkeyOk ? "快速鍵：Ctrl+Alt+H（程式開著時，在任何地方都能按）" : "Ctrl+Alt+H 已被其他程式占用，請直接按這個按鈕");
            };
        }

        // ---------- 共用 ----------

        protected override void SetVisibleCore(bool value)
        {
            // 開機自動執行（--tray）：第一次顯示時直接藏到通知區域（建構時已經建立了視窗，所以不能看 IsHandleCreated）
            if (startHidden && value) { startHidden = false; if (!IsHandleCreated) CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hotkeyOk = Native.RegisterHotKey(Handle, HOTKEY_ID, 0x0001 /*ALT*/ | 0x0002 /*CTRL*/ | 0x4000 /*NOREPEAT*/, (uint)Keys.H);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.UnregisterHotKey(Handle, HOTKEY_ID);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 /*WM_HOTKEY*/ && m.WParam.ToInt32() == HOTKEY_ID) { ToggleHide(); return; }
            if (m.Msg == (int)showMessage && showMessage != 0) { ShowFromTray(); return; }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 工作列背景圖片、自訂開始功能表都需要程式在背景執行，所以關閉視窗時縮到通知區域
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing && (skin.Enabled || startEnabled))
            {
                e.Cancel = true;
                Hide();
                if (!AppSettings.GetBool("TrayTipShown", false))
                {
                    tray.ShowBalloonTip(4000, "桌面顏色整理器", "程式會在右下角通知區域繼續執行，讓工作列背景圖片和自訂開始功能表保持運作。按右鍵可以完全離開。", ToolTipIcon.Info);
                    AppSettings.Set("TrayTipShown", true);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            FlushSaves();
            StopAnimation();
            skin.Dispose();
            if (winHook != null) winHook.Dispose();
            if (launcher != null) { launcher.AllowClose = true; launcher.Dispose(); }
            WinSettings.FlushBroadcasts();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }

        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("開啟桌面顏色整理器", null, delegate { ShowFromTray(); });
            menu.Items.Add("隱藏 / 顯示全部桌面圖示（Ctrl+Alt+H）", null, delegate { ToggleHide(); });
            traySkinItem = new ToolStripMenuItem("工作列背景圖片", null, delegate { chkSkin.Checked = !skin.Enabled; });
            menu.Items.Add(traySkinItem);
            trayStartItem = new ToolStripMenuItem("自訂開始功能表（Windows 鍵）", null, delegate { chkStartEnabled.Checked = !startEnabled; });
            menu.Items.Add(trayStartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("離開", null, delegate { reallyExit = true; Close(); });
            menu.Opening += delegate
            {
                traySkinItem.Checked = skin.Enabled;
                traySkinItem.Enabled = skin.Image != null;
                trayStartItem.Checked = startEnabled;
            };
            tray = new NotifyIcon { Icon = Icon, Text = "桌面顏色整理器", ContextMenuStrip = menu, Visible = !devMode };
            tray.DoubleClick += delegate { ShowFromTray(); };
        }

        public void RenderPage(int i, string path)
        {
            ShowPage(i);
            Application.DoEvents();
            using (Bitmap b = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                DrawToBitmap(b, new Rectangle(Point.Empty, ClientSize));
                b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        public void ForceExit() { reallyExit = true; Close(); }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void DrawBottomLine(Graphics g, Control c)
        {
            using (Pen p = new Pen(Color.FromArgb(226, 229, 234))) g.DrawLine(p, 0, c.Height - 1, c.Width, c.Height - 1);
        }

        Button MakeButton(string text, int x, int y, int w, bool primary)
        {
            Button b = new Button { Text = text, Left = x, Top = y, Width = w, Height = S(34), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(206, 211, 219);
            b.BackColor = primary ? Color.FromArgb(52, 112, 236) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52);
            if (primary) b.Font = new Font(Font, FontStyle.Bold);
            return b;
        }

        Button MakeButton(string text, int w, bool primary, Action onClick)
        {
            Button b = MakeButton(text, 0, 0, w, primary);
            b.Click += delegate { onClick(); };
            return b;
        }

        ComboBox MakeCombo(int width, params string[] items)
        {
            ComboBox c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            c.Items.AddRange(items);
            return c;
        }

        Panel RadioPair(string a, string b, out RadioButton ra, out RadioButton rb)
        {
            Panel p = new Panel { Height = S(30) };
            ra = new RadioButton { Text = a, AutoSize = true, Left = 0, Top = S(4) };
            rb = new RadioButton { Text = b, AutoSize = true, Left = S(90), Top = S(4) };
            p.Controls.Add(ra); p.Controls.Add(rb);
            p.Width = S(180);
            return p;
        }

        Panel NewSettingsPage()
        {
            return new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Visible = false };
        }

        // 套用設定並在狀態列回報；被 Windows 擋下的設定給明確說明
        void Try(Action a, string ok)
        {
            try { a(); status.Text = ok; }
            catch (UnauthorizedAccessException) { status.Text = "這個設定受到 Windows 保護，其他程式無法修改，請改用「開啟 Windows 設定」調整。"; }
            catch (System.Security.SecurityException) { status.Text = "這個設定受到 Windows 保護，其他程式無法修改，請改用「開啟 Windows 設定」調整。"; }
            catch (Exception ex) { status.Text = "設定失敗：" + ex.Message; }
        }

        void ShowPage(int i)
        {
            currentPage = i;
            for (int k = 0; k < pages.Length; k++)
            {
                bool sel = k == i;
                pages[k].Visible = sel;
                tabs[k].BackColor = sel ? Color.FromArgb(232, 240, 254) : Color.White;
                tabs[k].ForeColor = sel ? Color.FromArgb(36, 90, 210) : Color.FromArgb(110, 116, 126);
                tabs[k].FlatAppearance.MouseOverBackColor = sel ? Color.FromArgb(222, 233, 252) : Color.FromArgb(242, 244, 247);
            }
            if (i == 1) UpdatePattern();
            if (i == 2) LoadPersonal();
            if (i == 3) LoadTaskbar();
            if (i == 4) LoadStart();
            status.Text = pageStatus[i] ?? "";
        }

        void SetPageStatus(int page, string text)
        {
            pageStatus[page] = text;
            if (currentPage == page) status.Text = text;
        }

        void Busy(bool on)
        {
            Cursor = on ? Cursors.WaitCursor : Cursors.Default;
            foreach (Button b in arrangeButtons) b.Enabled = !on;
            if (on) { status.Text = "處理中…"; status.Refresh(); }
        }

        // ---------- 一鍵隱藏 ----------

        void ToggleHide()
        {
            try
            {
                using (Desktop d = Desktop.Open())
                {
                    bool hide = !d.IconsHidden;
                    d.IconsHidden = hide;
                    UpdateHideButton(hide);
                    status.Text = hide ? "已隱藏全部桌面圖示（再按一次或 Ctrl+Alt+H 就會顯示）" : "已顯示全部桌面圖示";
                }
            }
            catch (Exception ex) { status.Text = "切換失敗：" + ex.Message; }
        }

        void UpdateHideButton(bool hidden)
        {
            btnHide.Text = hidden ? "顯示全部圖示" : "隱藏全部圖示";
        }

        // ======================= 分頁 1：顏色排列 =======================

        Panel ArrangeBar(Control[] left)
        {
            Panel bar = new Panel { Dock = DockStyle.Top, Height = S(58), BackColor = Color.White };
            bar.Paint += (s, e) => DrawBottomLine(e.Graphics, bar);
            bar.Controls.AddRange(left);
            Button restore = MakeButton("還原排列前位置", 0, S(12), S(140), false);
            Button scan = MakeButton("重新掃描", 0, S(12), S(96), false);
            restore.Click += delegate { Restore(); };
            scan.Click += delegate { Scan(); };
            bar.Controls.Add(restore); bar.Controls.Add(scan);
            bar.Layout += delegate
            {
                scan.Left = bar.ClientSize.Width - S(16) - scan.Width;
                restore.Left = scan.Left - S(8) - restore.Width;
            };
            arrangeButtons.Add(restore); arrangeButtons.Add(scan);
            foreach (Control c in left) if (c is Button) arrangeButtons.Add((Button)c);
            return bar;
        }

        Panel BuildColorPage()
        {
            Panel page = new Panel { Dock = DockStyle.Fill, Visible = false };
            Button apply = MakeButton("套用到桌面", S(16), S(12), S(130), true);
            apply.Click += delegate { ApplyColor(); };
            rbVertical = new RadioButton { Text = "直欄（由上而下）", Left = S(166), Top = S(20), AutoSize = true, Checked = true };
            rbHorizontal = new RadioButton { Text = "橫列（由左而右）", Left = S(316), Top = S(20), AutoSize = true };
            chkGroup = new CheckBox { Text = "每個色系另起一欄 / 一列", Left = S(466), Top = S(20), AutoSize = true, Checked = true };
            colorPreview = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BackColor };
            colorPreview.Resize += delegate { LayoutColorPreview(); };
            page.Controls.Add(colorPreview);
            page.Controls.Add(ArrangeBar(new Control[] { apply, rbVertical, rbHorizontal, chkGroup }));
            return page;
        }

        void Scan()
        {
            Busy(true);
            try
            {
                using (Desktop d = Desktop.Open())
                {
                    d.LoadItems(true);
                    snap = Snapshot.Take(d);
                    UpdateHideButton(d.IconsHidden);
                }
                List<DesktopItem> sorted = ColorLayout.Sort(snap.Items);
                ShowColorPreview(sorted);
                SetPageStatus(0, "桌面上共 " + sorted.Count + " 個圖示 · 下方是依顏色分好的預覽" + Summary(sorted));
                UpdatePattern();
                status.Text = pageStatus[currentPage] ?? "";
            }
            catch (Exception ex) { status.Text = "掃描桌面失敗：" + ex.Message; }
            finally { Busy(false); }
        }

        void BackupIfNeeded(Desktop d)
        {
            // 只在「第一次排列前」備份，重複排列不會蓋掉真正的原始位置
            if (!arrangedThisSession || !File.Exists(Backup.FilePath)) Backup.Save(d.Items);
        }

        void ApplyColor()
        {
            HaltAnimation();
            patternIds.Clear();
            Busy(true);
            try
            {
                List<DesktopItem> sorted;
                bool autoWasOn;
                using (Desktop d = Desktop.Open())
                {
                    d.LoadItems(true);
                    if (d.Items.Count == 0) { SetPageStatus(0, "桌面上沒有圖示。"); return; }
                    BackupIfNeeded(d);
                    autoWasOn = d.AutoArrangeOn;
                    Snapshot s = Snapshot.Take(d);
                    sorted = ColorLayout.Sort(s.Items);
                    d.Position(sorted, ColorLayout.Plan(sorted, s, rbVertical.Checked, chkGroup.Checked));
                    arrangedThisSession = true;
                    snap = s;
                }
                ShowColorPreview(sorted);
                SetPageStatus(0, "已依顏色排列 " + sorted.Count + " 個圖示" +
                    (autoWasOn ? "（已關閉「自動排列圖示」）" : "") + " · 不滿意可按「還原排列前位置」");
            }
            catch (Exception ex) { status.Text = "排列失敗：" + ex.Message; }
            finally { Busy(false); }
        }

        void Restore()
        {
            HaltAnimation();
            patternIds.Clear();
            Busy(true);
            try
            {
                Dictionary<string, POINT> saved = Backup.Load();
                if (saved.Count == 0) { status.Text = "還沒有備份可以還原（第一次排列時才會建立備份）。"; return; }
                using (Desktop d = Desktop.Open())
                {
                    d.LoadItems(false);
                    List<DesktopItem> items = new List<DesktopItem>();
                    List<POINT> pts = new List<POINT>();
                    foreach (DesktopItem it in d.Items)
                    {
                        POINT p;
                        if (saved.TryGetValue(it.ParsingName, out p)) { items.Add(it); pts.Add(p); }
                    }
                    d.Position(items, pts);
                    arrangedThisSession = false;
                    status.Text = "已還原 " + items.Count + " 個圖示的位置。";
                }
            }
            catch (Exception ex) { status.Text = "還原失敗：" + ex.Message; }
            finally { Busy(false); }
        }

        static string Summary(List<DesktopItem> sorted)
        {
            StringBuilder sb = new StringBuilder();
            foreach (IGrouping<int, DesktopItem> g in sorted.GroupBy(x => x.Color.Group))
                sb.Append(" · ").Append(ColorAnalyzer.GroupNames[g.Key].Replace("色系", "")).Append(' ').Append(g.Count());
            return sb.ToString();
        }

        void ShowColorPreview(List<DesktopItem> sorted)
        {
            colorPreview.SuspendLayout();
            List<Control> old = colorPreview.Controls.Cast<Control>().ToList();
            colorPreview.Controls.Clear();
            foreach (Control c in old) c.Dispose();
            foreach (IGrouping<int, DesktopItem> grp in sorted.GroupBy(x => x.Color.Group))
            {
                colorPreview.Controls.Add(new GroupHeader(ColorAnalyzer.GroupNames[grp.Key] + "　" + grp.Count() + " 個", ColorAnalyzer.GroupColors[grp.Key], scale));
                foreach (DesktopItem it in grp)
                {
                    Tile t = new Tile(it, scale) { Font = Font };
                    colorPreview.Controls.Add(t);
                    tip.SetToolTip(t, it.Name);
                }
            }
            colorPreview.ResumeLayout(false);
            LayoutColorPreview();
        }

        // 自己排版：每個色系一個標題，下面的圖示依視窗寬度換行
        void LayoutColorPreview()
        {
            int pad = S(16), gap = S(6), width = colorPreview.ClientSize.Width - pad;
            Point off = colorPreview.AutoScrollPosition;
            int x = pad, y = S(8), rowH = 0;
            foreach (Control c in colorPreview.Controls)
            {
                if (c is GroupHeader)
                {
                    if (rowH > 0) y += rowH;
                    c.Location = new Point(pad + off.X, y + S(8) + off.Y);
                    y += S(8) + c.Height + S(4);
                    x = pad; rowH = 0;
                    continue;
                }
                if (x > pad && x + c.Width > width) { x = pad; y += rowH; }
                c.Location = new Point(x + off.X, y + off.Y);
                x += c.Width + gap;
                rowH = c.Height + gap;
            }
        }

        // ======================= 分頁 2：像素圖案 =======================

        Panel BuildPatternPage()
        {
            Panel page = new Panel { Dock = DockStyle.Fill, Visible = false };
            Button apply = MakeButton("套用到桌面", S(16), S(12), S(130), true);
            apply.Click += delegate { ApplyPattern(); };
            Label lbl = new Label { Text = "圖案", Left = S(164), Top = S(21), AutoSize = true };
            cbSource = new ComboBox { Left = S(204), Top = S(16), Width = S(110), DropDownStyle = ComboBoxStyle.DropDownList };
            cbSource.Items.AddRange(new object[] { "愛心", "星星", "笑臉", "文字", "圖片 / 照片" });
            cbSource.SelectedIndex = 0;
            txtText = new TextBox { Left = S(324), Top = S(16), Width = S(150), Text = "HI", Visible = false };
            btnPickImage = MakeButton("選擇圖片…", S(324), S(12), S(110), false);
            btnPickImage.Visible = false;
            btnPickImage.Click += delegate { PickPatternImage(); };
            chkRemoveBg = new CheckBox { Text = "去除背景（只排出主體）", Left = S(448), Top = S(20), AutoSize = true, Checked = true, Visible = false };
            cbSource.SelectedIndexChanged += delegate { OnSourceChanged(); };
            txtText.TextChanged += delegate { UpdatePattern(); };
            chkRemoveBg.CheckedChanged += delegate { UpdatePattern(); };
            patternPreview = new PatternPreview(scale) { Dock = DockStyle.Fill, BackColor = BackColor, Font = Font };

            // 第二列：動畫
            Panel animBar = new Panel { Dock = DockStyle.Top, Height = S(48), BackColor = Color.FromArgb(250, 251, 252) };
            animBar.Paint += (s, e) => DrawBottomLine(e.Graphics, animBar);
            Label la = new Label { Text = "動畫", Left = S(18), Top = S(15), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            cbAnim = new ComboBox { Left = S(64), Top = S(11), Width = S(130), DropDownStyle = ComboBoxStyle.DropDownList };
            cbAnim.Items.AddRange(PatternAnimator.Modes);
            cbAnim.SelectedIndex = 0;
            Label ls = new Label { Text = "速度", Left = S(212), Top = S(15), AutoSize = true };
            cbAnimSpeed = new ComboBox { Left = S(252), Top = S(11), Width = S(80), DropDownStyle = ComboBoxStyle.DropDownList };
            cbAnimSpeed.Items.AddRange(PatternAnimator.Speeds);
            cbAnimSpeed.SelectedIndex = 1;
            Label hint = new Label
            {
                Text = "桌面上的圖示會隨機跳動或互換位置，但整體形狀不變；選「關閉」就停下並放回原位",
                Left = S(350), Top = S(15), AutoSize = true, ForeColor = Color.FromArgb(120, 126, 136)
            };
            animBar.Controls.AddRange(new Control[] { la, cbAnim, ls, cbAnimSpeed, hint });
            cbAnim.SelectedIndexChanged += delegate { if (!loading) RestartAnimation(); };
            cbAnimSpeed.SelectedIndexChanged += delegate { if (!loading && animator != null) RestartAnimation(); };

            page.Controls.Add(patternPreview);
            page.Controls.Add(animBar);
            page.Controls.Add(ArrangeBar(new Control[] { apply, lbl, cbSource, txtText, btnPickImage, chkRemoveBg }));
            return page;
        }

        // 依目前的選擇開始 / 重新開始 / 停止桌面動畫
        void RestartAnimation()
        {
            StopAnimation();
            int mode = cbAnim.SelectedIndex;
            if (mode <= 0) { SetPageStatus(1, "已停止動畫，圖示都放回原位。"); return; }
            try
            {
                animator = new PatternAnimator(mode, cbAnimSpeed.SelectedIndex, patternIds);
                animator.Failed += msg => BeginInvoke((Action)delegate
                {
                    StopAnimation();
                    loading = true; cbAnim.SelectedIndex = 0; loading = false;
                    SetPageStatus(1, "動畫已停止（桌面有變動）：" + msg);
                });
                SetPageStatus(1, "動畫進行中：" + PatternAnimator.Modes[mode] + "，共 " + animator.Count + " 個圖示" +
                    (patternIds.Count == 0 ? "（還沒套用圖案，所以桌面上全部的圖示一起動）" : "") + " · 選「關閉」就會停下來放回原位");
            }
            catch (Exception ex)
            {
                animator = null;
                SetPageStatus(1, "無法開始動畫：" + ex.Message);
            }
        }

        void StopAnimation()
        {
            if (animator == null) return;
            try { animator.Dispose(); } catch { }
            animator = null;
        }

        // 要重新排列桌面前：先停動畫、把選單設回「關閉」
        void HaltAnimation()
        {
            StopAnimation();
            if (cbAnim != null && cbAnim.SelectedIndex != 0) { loading = true; cbAnim.SelectedIndex = 0; loading = false; }
        }

        PatternSource GetPatternSource()
        {
            switch (cbSource.SelectedIndex)
            {
                case 0: return Shapes.Heart();
                case 1: return Shapes.Star();
                case 2: return Shapes.Smiley();
                case 3: return snap == null ? null : Shapes.Text(txtText.Text, snap.Spacing.X, snap.Spacing.Y);
                default: return userImage == null ? null : new PatternSource { Image = userImage, Colorful = true, OwnsImage = false };
            }
        }

        void OnSourceChanged()
        {
            int i = cbSource.SelectedIndex;
            txtText.Visible = i == 3;
            btnPickImage.Visible = chkRemoveBg.Visible = i == 4;
            if (i == 4 && userImage == null) PickPatternImage();
            else UpdatePattern();
            if (i == 3) txtText.Focus();
        }

        static Bitmap LoadImageFile(string path, int maxSide)
        {
            using (Image img = Image.FromFile(path))
            {
                double k = Math.Min(1.0, maxSide / (double)Math.Max(img.Width, img.Height));
                Bitmap b = new Bitmap(Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k)));
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, b.Width, b.Height);
                }
                return b;
            }
        }

        static string PickImageFile(IWin32Window owner, string title)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = title;
                dlg.Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有檔案|*.*";
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
            }
        }

        void PickPatternImage()
        {
            string path = PickImageFile(this, "選擇要用圖示排出來的圖片");
            if (path != null)
            {
                try
                {
                    Bitmap b = LoadImageFile(path, 1600);
                    if (userImage != null) userImage.Dispose();
                    userImage = b;
                }
                catch (Exception ex) { MessageBox.Show(this, "無法開啟這張圖片：" + ex.Message, Text); }
            }
            UpdatePattern();
        }

        void UpdatePattern()
        {
            if (snap == null) return;
            try
            {
                using (PatternSource src = GetPatternSource())
                {
                    PatternPlan p = src == null ? null : Pattern.Plan(snap, src, chkRemoveBg.Checked);
                    patternPreview.Snap = snap;
                    patternPreview.Plan = p;
                    patternPreview.Invalidate();
                    SetPageStatus(1, src == null
                        ? (cbSource.SelectedIndex == 4 ? "請按「選擇圖片…」挑一張圖片或照片" : "請輸入要排出來的文字")
                        : PatternSummary(p));
                }
            }
            catch (Exception ex) { SetPageStatus(1, "預覽失敗：" + ex.Message); }
        }

        static string PatternSummary(PatternPlan p)
        {
            if (p == null || p.Used == 0) return "圖案太小或是空的，換一個試試看";
            int left = p.Items.Count - p.Used;
            return "圖案大小 " + p.Cols + "×" + p.Rows + " 格，用了 " + p.Used + " 個圖示" +
                (left > 0 ? "，其餘 " + left + " 個放在右下角" : "") + " · 桌面圖示越多，圖案越精細";
        }

        void ApplyPattern()
        {
            bool animate = cbAnim.SelectedIndex > 0;
            StopAnimation(); // 先放回原位再重新排
            bool applied = false;
            Busy(true);
            try
            {
                using (PatternSource src = GetPatternSource())
                {
                    if (src == null) { status.Text = cbSource.SelectedIndex == 4 ? "請先選擇一張圖片。" : "請先輸入文字。"; return; }
                    using (Desktop d = Desktop.Open())
                    {
                        d.LoadItems(true);
                        if (d.Items.Count == 0) { status.Text = "桌面上沒有圖示。"; return; }
                        Snapshot s = Snapshot.Take(d);
                        PatternPlan plan = Pattern.Plan(s, src, chkRemoveBg.Checked);
                        if (plan.Used == 0) { status.Text = "圖案太小或是空的，換一個試試看。"; return; }
                        BackupIfNeeded(d);
                        bool autoWasOn = d.AutoArrangeOn;
                        d.Position(plan.Items, plan.Cells.Select(c => s.ToPoint(c)).ToList());
                        arrangedThisSession = true;
                        snap = s;
                        patternPreview.Snap = s;
                        patternPreview.Plan = plan;
                        patternPreview.Invalidate();
                        patternIds = plan.Items.Take(plan.Used).Select(i => i.ParsingName).ToList();
                        applied = true;
                        SetPageStatus(1, "已在桌面排出圖案（" + plan.Cols + "×" + plan.Rows + " 格，" + plan.Used + " 個圖示）" +
                            (autoWasOn ? "，已關閉「自動排列圖示」" : "") + " · 可按「還原排列前位置」復原 · 下方「動畫」可以讓圖示動起來");
                    }
                }
            }
            catch (Exception ex) { status.Text = "排列失敗：" + ex.Message; }
            finally { Busy(false); }
            if (animate && applied) RestartAnimation();
            else if (animate) HaltAnimation();
        }

        // ======================= 分頁 3：個人化色彩 =======================

        Panel BuildPersonalPage()
        {
            Panel page = NewSettingsPage();
            PageBuilder b = new PageBuilder(page, scale, Font);

            b.Section("主題模式");
            b.Row("Windows 模式", RadioPair("淺色", "深色", out rbSysLight, out rbSysDark));
            b.Row("應用程式模式", RadioPair("淺色", "深色", out rbAppLight, out rbAppDark));
            chkTransparency = new CheckBox { Text = "開啟透明效果（視窗、工作列的毛玻璃質感）", AutoSize = true };
            b.Row("透明效果", chkTransparency);

            b.Section("強調色");
            chkAutoAccent = new CheckBox { Text = "依照桌布自動挑選強調色", AutoSize = true };
            b.Row("自動", chkAutoAccent);
            accentSwatches = new SwatchPanel(WindowsAccents, scale, 12);
            b.Row("顏色", accentSwatches);
            lblAccent = new Label { AutoSize = true, ForeColor = Color.FromArgb(84, 90, 100) };
            b.Row("", MakeButton("自訂顏色…", S(110), false, PickCustomAccent),
                      MakeButton("用桌面圖示的主色", S(150), false, AccentFromIcons), lblAccent);
            chkAccentStart = new CheckBox { Text = "在「開始」和工作列上顯示", AutoSize = true };
            chkAccentTitle = new CheckBox { Text = "在標題列和視窗框線上顯示", AutoSize = true };
            b.Row("顯示強調色", chkAccentStart, chkAccentTitle);
            b.Note("「開始」和工作列上的強調色，只有在 Windows 模式為「深色」時才看得到。部分地方要重新啟動檔案總管（或登出再登入）才會完全更新。");
            b.Row("", MakeButton("開啟 Windows 色彩設定", S(190), false, () => WinSettings.OpenSettings("ms-settings:colors")),
                      MakeButton("重新啟動檔案總管", S(150), false, RestartExplorer));
            b.End();

            rbSysLight.CheckedChanged += delegate { if (!loading) Try(() => WinSettings.SystemLight = rbSysLight.Checked, rbSysLight.Checked ? "Windows 已切換為淺色模式" : "Windows 已切換為深色模式"); };
            rbAppLight.CheckedChanged += delegate { if (!loading) Try(() => WinSettings.AppsLight = rbAppLight.Checked, rbAppLight.Checked ? "應用程式已切換為淺色模式" : "應用程式已切換為深色模式"); };
            chkTransparency.CheckedChanged += delegate { if (!loading) Try(() => WinSettings.Transparency = chkTransparency.Checked, chkTransparency.Checked ? "已開啟透明效果" : "已關閉透明效果"); };
            chkAutoAccent.CheckedChanged += delegate { if (!loading) Try(() => WinSettings.AutoAccent = chkAutoAccent.Checked, chkAutoAccent.Checked ? "強調色會依照桌布自動挑選" : "已改為手動選擇強調色"); };
            chkAccentStart.CheckedChanged += delegate { if (!loading) Try(() => WinSettings.AccentOnStart = chkAccentStart.Checked, "已更新「開始」和工作列的強調色顯示"); };
            chkAccentTitle.CheckedChanged += delegate { if (!loading) Try(() => WinSettings.AccentOnTitle = chkAccentTitle.Checked, "已更新標題列的強調色顯示"); };
            accentSwatches.Picked += c => SetAccent(c);
            return page;
        }

        void LoadPersonal()
        {
            loading = true;
            try
            {
                rbSysLight.Checked = WinSettings.SystemLight; rbSysDark.Checked = !rbSysLight.Checked;
                rbAppLight.Checked = WinSettings.AppsLight; rbAppDark.Checked = !rbAppLight.Checked;
                chkTransparency.Checked = WinSettings.Transparency;
                chkAutoAccent.Checked = WinSettings.AutoAccent;
                chkAccentStart.Checked = WinSettings.AccentOnStart;
                chkAccentTitle.Checked = WinSettings.AccentOnTitle;
                ShowAccent(WinSettings.Accent);
            }
            catch (Exception ex) { status.Text = "讀取設定失敗：" + ex.Message; }
            finally { loading = false; }
        }

        void ShowAccent(Color c)
        {
            accentSwatches.SelectColor(c);
            lblAccent.Text = "目前：#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        void SetAccent(Color c)
        {
            Try(() =>
            {
                if (WinSettings.AutoAccent) { WinSettings.AutoAccent = false; loading = true; chkAutoAccent.Checked = false; loading = false; }
                WinSettings.SetAccent(c);
                ShowAccent(c);
            }, "已套用強調色 #" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"));
        }

        void PickCustomAccent()
        {
            using (ColorDialog dlg = new ColorDialog { FullOpen = true, Color = WinSettings.Accent })
                if (dlg.ShowDialog(this) == DialogResult.OK) SetAccent(dlg.Color);
        }

        // 取桌面上最多圖示的那個色系，當作強調色
        void AccentFromIcons()
        {
            if (snap == null) { status.Text = "還沒掃描桌面，請先到「顏色排列」按「重新掃描」。"; return; }
            IGrouping<int, DesktopItem> top = snap.Items.Where(i => !i.Color.Neutral).GroupBy(i => i.Color.Group)
                .OrderByDescending(g => g.Count()).FirstOrDefault();
            if (top == null) { status.Text = "桌面圖示大多是黑白灰，挑不出主色。"; return; }
            double x = 0, y = 0, s = 0, v = 0;
            foreach (DesktopItem it in top)
            {
                double rad = it.Color.Hue * Math.PI / 180;
                x += Math.Cos(rad); y += Math.Sin(rad); s += it.Color.Sat; v += it.Color.Val;
            }
            int n = top.Count();
            double h = Math.Atan2(y, x) * 180 / Math.PI;
            if (h < 0) h += 360;
            SetAccent(ColorAnalyzer.FromHsv(h, Math.Max(0.55, Math.Min(0.9, s / n)), Math.Max(0.55, Math.Min(0.85, v / n))));
        }

        void RestartExplorer()
        {
            if (MessageBox.Show(this, "重新啟動檔案總管時，桌面和工作列會消失幾秒鐘，已開啟的檔案總管資料夾視窗也會被關閉。\n\n要繼續嗎？",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            try { WinSettings.RestartExplorer(); status.Text = "已重新啟動檔案總管。"; }
            catch (Exception ex) { status.Text = "重新啟動失敗：" + ex.Message; }
            finally { Cursor = Cursors.Default; }
        }

        // ======================= 分頁 4：工作列 =======================

        Panel BuildTaskbarPage()
        {
            Panel page = NewSettingsPage();
            PageBuilder b = new PageBuilder(page, scale, Font);

            b.Section("工作列背景圖片");
            lblSkinFile = new Label { AutoSize = true, ForeColor = Color.FromArgb(84, 90, 100) };
            b.Row("圖片", MakeButton("選擇圖片…", S(110), false, PickSkinImage), MakeButton("框選位置…", S(110), false, CropSkin),
                MakeButton("移除圖片", S(96), false, RemoveSkinImage), lblSkinFile);
            chkSkin = new CheckBox { Text = "在工作列上顯示背景圖片", AutoSize = true };
            b.Row("啟用", chkSkin);
            trkOpacity = new TrackBar { Minimum = 5, Maximum = 90, TickFrequency = 5, SmallChange = 1, LargeChange = 5, Width = S(280) };
            lblOpacity = new Label { AutoSize = true, ForeColor = Color.FromArgb(84, 90, 100) };
            b.Row("圖片濃度", trkOpacity, lblOpacity);
            cbFit = MakeCombo(S(140), "填滿", "延展", "並排");
            b.Row("顯示方式", cbFit);
            trkPos = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 10, Width = S(280) };
            b.Row("圖片位置", trkPos, new Label { Text = "填滿而且沒有框選時，選擇顯示照片的上段或下段", AutoSize = true, ForeColor = Color.FromArgb(120, 126, 136) });
            chkAutoStart = new CheckBox { Text = "開機時自動在背景執行（讓背景圖片一直都在）", AutoSize = true };
            b.Row("背景執行", chkAutoStart);
            b.Note("圖片會以半透明的方式疊在工作列上，滑鼠點擊會直接穿透到工作列，不影響使用；全螢幕遊戲或影片時會自動隱藏。" +
                   "打開 Windows 原本的開始功能表或搜尋時，Windows 會把工作列移到比所有程式都高的一層，那段時間圖片會被蓋住（這是 Windows 的限制）。" +
                   "本程式需要在背景執行（關閉視窗時會縮到右下角通知區域）。");

            b.Section("工作列項目");
            cbAlign = MakeCombo(S(140), "置中", "靠左");
            b.Row("對齊方式", cbAlign);
            cbSearch = MakeCombo(S(200), "隱藏", "只顯示搜尋圖示", "搜尋方塊", "搜尋圖示和標籤");
            b.Row("搜尋", cbSearch);
            chkTaskView = new CheckBox { Text = "工作檢視", AutoSize = true };
            chkWidgets = new CheckBox { Text = "小工具", AutoSize = true };
            lblWidgetsNote = new Label { AutoSize = true, ForeColor = Color.FromArgb(120, 126, 136) };
            b.Row("按鈕", chkTaskView, chkWidgets, lblWidgetsNote);
            chkAutoHide = new CheckBox { Text = "自動隱藏工作列", AutoSize = true };
            chkSeconds = new CheckBox { Text = "時鐘顯示秒數", AutoSize = true };
            b.Row("其他", chkAutoHide, chkSeconds);
            btnApplyTaskbar = MakeButton("立即套用（重新啟動檔案總管）", S(250), true, ApplyTaskbarItems);
            btnApplyTaskbar.Visible = false;
            b.Row("", btnApplyTaskbar, MakeButton("開啟 Windows 工作列設定", S(200), false, () => WinSettings.OpenSettings("ms-settings:taskbar")));
            b.Note("這台電腦的 Windows 版本，工作列要重新啟動檔案總管才會讀取新設定：改完後按「立即套用」，桌面和工作列會消失一兩秒再回來，" +
                   "已開啟的檔案總管資料夾視窗會被關閉。「自動隱藏工作列」會立刻生效，不需要套用。");
            b.End();

            chkSkin.CheckedChanged += delegate
            {
                if (loading) return;
                if (chkSkin.Checked && skin.Image == null) { loading = true; chkSkin.Checked = false; loading = false; PickSkinImage(); return; }
                skin.Enabled = chkSkin.Checked;
                AppSettings.Set("SkinEnabled", skin.Enabled);
                skin.Apply();
                status.Text = skin.Enabled ? "已在工作列顯示背景圖片" : "已關閉工作列背景圖片";
            };
            trkOpacity.ValueChanged += delegate
            {
                lblOpacity.Text = trkOpacity.Value + "%";
                if (loading) return;
                skin.OpacityPercent = trkOpacity.Value;
                skin.Apply();
                SaveLater("SkinOpacity", trkOpacity.Value);
            };
            cbFit.SelectedIndexChanged += delegate { if (loading) return; skin.Fit = cbFit.SelectedIndex; AppSettings.Set("SkinFit", skin.Fit); skin.Apply(); };
            trkPos.ValueChanged += delegate
            {
                if (loading) return;
                skin.Position = trkPos.Value;
                skin.Crop = RectangleF.Empty; // 用滑桿調位置就不用框選的範圍
                AppSettings.Set("SkinCrop", "");
                skin.Apply();
                SaveLater("SkinPos", skin.Position);
            };
            chkAutoStart.CheckedChanged += delegate { if (!loading) Try(() => WinSettings.AutoStart = chkAutoStart.Checked, chkAutoStart.Checked ? "已設定開機時自動在背景執行" : "已取消開機自動執行"); };
            cbAlign.SelectedIndexChanged += delegate { if (!loading) ChangeTaskbarItem(() => WinSettings.TaskbarLeft = cbAlign.SelectedIndex == 1, () => WinSettings.TaskbarLeft == (cbAlign.SelectedIndex == 1), "對齊方式"); };
            cbSearch.SelectedIndexChanged += delegate { if (!loading) ChangeTaskbarItem(() => WinSettings.SearchMode = cbSearch.SelectedIndex, () => WinSettings.SearchMode == cbSearch.SelectedIndex, "搜尋"); };
            chkTaskView.CheckedChanged += delegate { if (!loading) ChangeTaskbarItem(() => WinSettings.TaskView = chkTaskView.Checked, () => WinSettings.TaskView == chkTaskView.Checked, "工作檢視"); };
            chkWidgets.CheckedChanged += delegate { if (!loading) ChangeTaskbarItem(() => WinSettings.Widgets = chkWidgets.Checked, () => WinSettings.Widgets == chkWidgets.Checked, "小工具"); };
            chkSeconds.CheckedChanged += delegate { if (!loading) ChangeTaskbarItem(() => WinSettings.ClockSeconds = chkSeconds.Checked, () => WinSettings.ClockSeconds == chkSeconds.Checked, "時鐘秒數"); };
            chkAutoHide.CheckedChanged += delegate
            {
                if (loading) return;
                Try(() => WinSettings.TaskbarAutoHide = chkAutoHide.Checked, chkAutoHide.Checked ? "工作列會自動隱藏" : "工作列不再自動隱藏");
                loading = true; chkAutoHide.Checked = WinSettings.TaskbarAutoHide; loading = false;
            };
            return page;
        }

        // 寫入設定 → 讀回來確認真的寫進去了 → 提示要「立即套用」
        void ChangeTaskbarItem(Action write, Func<bool> verify, string name)
        {
            bool ok = false;
            try { write(); ok = verify(); }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            catch (Exception ex) { status.Text = "設定「" + name + "」失敗：" + ex.Message; LoadTaskbar(); return; }
            if (!ok)
            {
                status.Text = "「" + name + "」被 Windows 保護，其他程式無法修改，請按「開啟 Windows 工作列設定」調整。";
                LoadTaskbar(); // 把勾選狀態改回真正的值
                return;
            }
            taskbarPending = true;
            btnApplyTaskbar.Visible = true;
            status.Text = "已儲存「" + name + "」。按「立即套用」重新啟動檔案總管後，工作列就會換成新的樣子。";
        }

        void ApplyTaskbarItems()
        {
            btnApplyTaskbar.Enabled = false;
            Cursor = Cursors.WaitCursor;
            status.Text = "正在重新啟動檔案總管…";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { WinSettings.RestartExplorer(); } catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke((Action)delegate
                {
                    Cursor = Cursors.Default;
                    btnApplyTaskbar.Enabled = true;
                    if (error != null) { status.Text = "重新啟動檔案總管失敗：" + error; return; }
                    taskbarPending = false;
                    btnApplyTaskbar.Visible = false;
                    LoadTaskbar();
                    status.Text = "已套用，工作列已更新。";
                }); }
                catch (InvalidOperationException) { } // 視窗已經關了
            });
        }

        void InitSkin()
        {
            skin.OpacityPercent = Math.Max(5, Math.Min(90, AppSettings.GetInt("SkinOpacity", 35)));
            skin.Fit = Math.Max(0, Math.Min(2, AppSettings.GetInt("SkinFit", 0)));
            skin.Position = Math.Max(0, Math.Min(100, AppSettings.GetInt("SkinPos", 50)));
            skin.Crop = CropRect.FromSetting(AppSettings.Get("SkinCrop", ""));
            string path = AppSettings.Get("SkinImage", "");
            if (path.Length > 0 && File.Exists(path))
            {
                try { skin.Image = LoadImageFile(path, 3000); } catch { }
            }
            skin.Enabled = !devMode && AppSettings.GetBool("SkinEnabled", false) && skin.Image != null;
            skin.Apply();
        }

        void LoadTaskbar()
        {
            loading = true;
            try
            {
                string path = AppSettings.Get("SkinImage", "");
                lblSkinFile.Text = skin.Image == null ? "（尚未選擇）" : AppSettings.Get("SkinImageName", Path.GetFileName(path)) + (skin.Crop.IsEmpty ? "" : "（已框選）");
                chkSkin.Checked = skin.Enabled;
                trkOpacity.Value = skin.OpacityPercent;
                lblOpacity.Text = skin.OpacityPercent + "%";
                cbFit.SelectedIndex = skin.Fit;
                trkPos.Value = skin.Position;
                chkAutoStart.Checked = WinSettings.AutoStart;
                cbAlign.SelectedIndex = WinSettings.TaskbarLeft ? 1 : 0;
                cbSearch.SelectedIndex = Math.Max(0, Math.Min(3, WinSettings.SearchMode));
                chkTaskView.Checked = WinSettings.TaskView;
                chkWidgets.Checked = WinSettings.Widgets;
                bool ucpd = WinSettings.UcpdActive;
                chkWidgets.Enabled = !ucpd;
                lblWidgetsNote.Text = ucpd ? "（「小工具」受 Windows 保護，請到 Windows 設定切換）" : "";
                chkAutoHide.Checked = WinSettings.TaskbarAutoHide;
                chkSeconds.Checked = WinSettings.ClockSeconds;
                btnApplyTaskbar.Visible = taskbarPending;
            }
            catch (Exception ex) { status.Text = "讀取設定失敗：" + ex.Message; }
            finally { loading = false; }
        }

        // 把使用者選的圖片複製一份到程式資料夾（原檔搬走或刪掉也不影響）。先複製成功才刪舊的
        static string CopyIntoAppDir(string src, string prefix)
        {
            Directory.CreateDirectory(AppSettings.Dir);
            string full = Path.GetFullPath(src);
            if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(AppSettings.Dir), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(full).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return full;
            string dest = Path.Combine(AppSettings.Dir, prefix + DateTime.Now.Ticks + Path.GetExtension(src).ToLowerInvariant());
            File.Copy(src, dest, true);
            foreach (string old in Directory.GetFiles(AppSettings.Dir, prefix + "*"))
                if (!string.Equals(old, dest, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(old); } catch { } }
            return dest;
        }

        void PickSkinImage()
        {
            string src = PickImageFile(this, "選擇工作列背景圖片");
            if (src == null) return;
            try
            {
                Bitmap b = LoadImageFile(src, 3000);
                string dest = CopyIntoAppDir(src, "taskbar_bg");
                skin.Image = b;
                skin.Crop = RectangleF.Empty;
                skin.Enabled = true;
                AppSettings.Set("SkinImage", dest);
                AppSettings.Set("SkinImageName", Path.GetFileName(src));
                AppSettings.Set("SkinCrop", "");
                AppSettings.Set("SkinEnabled", true);
                skin.Apply();
                LoadTaskbar();
                status.Text = "已把「" + Path.GetFileName(src) + "」設為工作列背景。可以按「框選位置…」選要顯示照片的哪一段，用「圖片濃度」調整透明度。";
            }
            catch (Exception ex) { MessageBox.Show(this, "無法使用這張圖片：" + ex.Message, Text); }
        }

        void CropSkin()
        {
            if (skin.Image == null) { status.Text = "請先選擇一張工作列背景圖片。"; return; }
            RECT r;
            IntPtr bar = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            double aspect = bar != IntPtr.Zero && Native.GetWindowRect(bar, out r) && r.Bottom > r.Top ? (r.Right - r.Left) / (double)(r.Bottom - r.Top) : 2560 / 48.0;
            using (CropDialog dlg = new CropDialog(skin.Image, aspect, skin.Crop, "框選工作列要顯示的範圍", scale))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                skin.Crop = dlg.Result;
                skin.Fit = 0;
                AppSettings.Set("SkinCrop", CropRect.ToSetting(skin.Crop));
                AppSettings.Set("SkinFit", 0);
                skin.Apply();
                LoadTaskbar();
                status.Text = "已更新工作列照片的顯示範圍。";
            }
        }

        void RemoveSkinImage()
        {
            skin.Enabled = false;
            skin.Apply();
            skin.Image = null;
            skin.Crop = RectangleF.Empty;
            AppSettings.Set("SkinEnabled", false);
            AppSettings.Set("SkinImage", "");
            AppSettings.Set("SkinCrop", "");
            LoadTaskbar();
            status.Text = "已移除工作列背景圖片。";
        }

        // 拖曳滑桿時不要每一格都寫檔：停下來 0.4 秒後再存
        readonly Dictionary<string, object> pendingSaves = new Dictionary<string, object>();
        System.Windows.Forms.Timer saveTimer;
        void SaveLater(string key, object value)
        {
            pendingSaves[key] = value;
            if (saveTimer == null)
            {
                saveTimer = new System.Windows.Forms.Timer { Interval = 400 };
                saveTimer.Tick += delegate { saveTimer.Stop(); FlushSaves(); };
            }
            saveTimer.Stop();
            saveTimer.Start();
        }

        void FlushSaves()
        {
            foreach (KeyValuePair<string, object> kv in pendingSaves.ToList()) AppSettings.Set(kv.Key, kv.Value);
            pendingSaves.Clear();
        }

        // ======================= 分頁 5：開始功能表 =======================

        Panel BuildStartPage()
        {
            Panel page = NewSettingsPage();
            PageBuilder b = new PageBuilder(page, scale, Font);

            b.Section("自訂開始功能表");
            chkStartEnabled = new CheckBox { Text = "按 Windows 鍵時，開啟可以放照片的自訂開始功能表", AutoSize = true };
            b.Row("啟用", chkStartEnabled);
            chkStartButton = new CheckBox { Text = "點工作列上的開始按鈕、按 Ctrl+Esc 時也改開自訂的", AutoSize = true };
            b.Row("開始按鈕", chkStartButton);
            b.Note("外觀和操作盡量比照 Windows 11 原本的開始功能表：搜尋、已釘選、建議、全部（類別 / 格線 / 清單）。Win+E、Win+D 等組合鍵照常使用。" +
                   "Windows 不允許其他程式改變原本開始功能表的外觀，所以是用一個一模一樣的來代替；原本的被打開時（例如點開始按鈕）會立刻換成自訂的，可能會閃一下。");

            b.Section("外觀");
            lblStartFile = new Label { AutoSize = true, ForeColor = Color.FromArgb(84, 90, 100) };
            b.Row("背景照片", MakeButton("選擇照片…", S(110), false, PickStartImage), MakeButton("框選位置…", S(110), false, CropStart),
                MakeButton("用工作列的照片", S(140), false, UseTaskbarImageForStart), MakeButton("移除", S(70), false, RemoveStartImage), lblStartFile);
            trkStartStrength = new TrackBar { Minimum = 10, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 10, Width = S(280) };
            lblStartStrength = new Label { AutoSize = true, ForeColor = Color.FromArgb(84, 90, 100) };
            b.Row("照片濃度", trkStartStrength, lblStartStrength);
            trkStartBlur = new TrackBar { Minimum = 0, Maximum = 10, TickFrequency = 1, SmallChange = 1, LargeChange = 2, Width = S(280) };
            lblStartBlur = new Label { AutoSize = true, ForeColor = Color.FromArgb(84, 90, 100) };
            b.Row("模糊", trkStartBlur, lblStartBlur);
            cbStartTone = MakeCombo(S(140), "深色", "淺色");
            b.Row("底色", cbStartTone);
            cbStartSize = MakeCombo(S(140), "標準", "大");
            b.Row("大小", cbStartSize);
            chkStartAnimate = new CheckBox { Text = "開啟、關閉和切換畫面時的動畫（滑動、淡入淡出）", AutoSize = true };
            b.Row("動畫效果", chkStartAnimate);
            chkStartRecent = new CheckBox { Text = "在「建議」顯示最近開啟的檔案", AutoSize = true };
            b.Row("建議", chkStartRecent);
            txtStartName = new TextBox { Width = S(200) };
            b.Row("顯示名稱", txtStartName, new Label { Text = "顯示在左下角；留空就用 Windows 帳戶名稱", AutoSize = true, ForeColor = Color.FromArgb(120, 126, 136) });
            b.Row("", MakeButton("打開看看", S(120), true, PreviewStart));
            b.Note("沒有放照片時，背景會像原本的開始功能表一樣是半透明的毛玻璃。在應用程式上按右鍵可以「釘選到開始」和調整順序；上方的搜尋框可以直接打字找程式，按 Enter 開啟。");
            b.End();

            chkStartEnabled.CheckedChanged += delegate
            {
                if (loading) return;
                SetStartEnabled(chkStartEnabled.Checked);
                status.Text = startEnabled
                    ? (winHook != null && winHook.Installed ? "已啟用：按 Windows 鍵就會開啟自訂開始功能表" : "無法攔截 Windows 鍵，請重新啟動程式再試一次")
                    : "已停用自訂開始功能表，Windows 鍵恢復原本的開始功能表";
            };
            chkStartButton.CheckedChanged += delegate
            {
                if (loading) return;
                winHook.ReplaceRealStart = chkStartButton.Checked;
                AppSettings.Set("StartReplaceButton", chkStartButton.Checked);
            };
            trkStartStrength.ValueChanged += delegate
            {
                lblStartStrength.Text = trkStartStrength.Value + "%";
                if (loading) return;
                launcher.Style.Strength = trkStartStrength.Value;
                ApplyStartStyleLater();
                SaveLater("StartStrength", trkStartStrength.Value);
            };
            trkStartBlur.ValueChanged += delegate
            {
                lblStartBlur.Text = trkStartBlur.Value == 0 ? "不模糊" : trkStartBlur.Value.ToString();
                if (loading) return;
                launcher.Style.Blur = trkStartBlur.Value;
                ApplyStartStyleLater();
                SaveLater("StartBlur", trkStartBlur.Value);
            };
            cbStartTone.SelectedIndexChanged += delegate
            {
                if (loading) return;
                launcher.Style.Dark = cbStartTone.SelectedIndex == 0;
                AppSettings.Set("StartDark", launcher.Style.Dark);
                launcher.ApplyStyle();
            };
            cbStartSize.SelectedIndexChanged += delegate
            {
                if (loading) return;
                launcher.Style.Large = cbStartSize.SelectedIndex == 1;
                AppSettings.Set("StartLarge", launcher.Style.Large);
                launcher.ApplyStyle();
            };
            chkStartAnimate.CheckedChanged += delegate
            {
                if (loading) return;
                launcher.Style.Animate = chkStartAnimate.Checked;
                AppSettings.Set("StartAnimate", chkStartAnimate.Checked);
            };
            chkStartRecent.CheckedChanged += delegate
            {
                if (loading) return;
                launcher.Style.ShowRecent = chkStartRecent.Checked;
                AppSettings.Set("StartRecent", chkStartRecent.Checked);
                launcher.RefreshData();
                launcher.ApplyStyle();
            };
            txtStartName.TextChanged += delegate
            {
                if (loading) return;
                AppSettings.Set("StartUserName", txtStartName.Text.Trim());
                launcher.RefreshUser();
            };
            return page;
        }

        // 滑桿拖曳時，停下來一下才重畫背景（重畫模糊照片比較花時間）
        System.Windows.Forms.Timer styleTimer;
        void ApplyStartStyleLater()
        {
            if (styleTimer == null)
            {
                styleTimer = new System.Windows.Forms.Timer { Interval = 150 };
                styleTimer.Tick += delegate { styleTimer.Stop(); launcher.ApplyStyle(); };
            }
            styleTimer.Stop();
            styleTimer.Start();
        }

        void InitStart()
        {
            launcher = new StartPanel(scale);
            StartStyle st = launcher.Style;
            st.Strength = Math.Max(10, Math.Min(100, AppSettings.GetInt("StartStrength", 75)));
            st.Blur = Math.Max(0, Math.Min(10, AppSettings.GetInt("StartBlur", 3)));
            st.Dark = AppSettings.GetBool("StartDark", true);
            st.Large = AppSettings.GetBool("StartLarge", false);
            st.ShowRecent = AppSettings.GetBool("StartRecent", true);
            st.Animate = AppSettings.GetBool("StartAnimate", true);
            st.Crop = CropRect.FromSetting(AppSettings.Get("StartCrop", ""));
            string path = AppSettings.Get("StartImage", "");
            if (path.Length > 0 && File.Exists(path))
            {
                try { st.Photo = LoadImageFile(path, 2000); } catch { }
            }
            launcher.ApplyStyle();
            // 這兩個回呼是在鍵盤掛勾的執行緒被呼叫，轉回畫面執行緒
            winHook = new WinKeyHook(
                delegate { if (launcher.IsHandleCreated) launcher.BeginInvoke((Action)launcher.Toggle); },
                delegate { if (launcher.IsHandleCreated) launcher.BeginInvoke((Action)launcher.ShowFromRealStart); });
            winHook.ReplaceRealStart = AppSettings.GetBool("StartReplaceButton", true);
            if (devMode) startEnabled = false;
            else SetStartEnabled(AppSettings.GetBool("StartEnabled", false));
        }

        void SetStartEnabled(bool on)
        {
            startEnabled = on;
            AppSettings.Set("StartEnabled", on);
            if (on)
            {
                launcher.Preload(); // 先在背景載入應用程式清單，第一次按 Windows 鍵就能立刻開
                winHook.Start();
            }
            else
            {
                winHook.Stop();
                launcher.HideLauncher();
            }
        }

        void LoadStart()
        {
            loading = true;
            try
            {
                StartStyle st = launcher.Style;
                chkStartEnabled.Checked = startEnabled;
                chkStartButton.Checked = winHook.ReplaceRealStart;
                lblStartFile.Text = st.Photo == null ? "（尚未選擇）" : AppSettings.Get("StartImageName", "") + (st.Crop.IsEmpty ? "" : "（已框選）");
                trkStartStrength.Value = st.Strength;
                lblStartStrength.Text = st.Strength + "%";
                trkStartBlur.Value = st.Blur;
                lblStartBlur.Text = st.Blur == 0 ? "不模糊" : st.Blur.ToString();
                cbStartTone.SelectedIndex = st.Dark ? 0 : 1;
                cbStartSize.SelectedIndex = st.Large ? 1 : 0;
                chkStartAnimate.Checked = st.Animate;
                chkStartRecent.Checked = st.ShowRecent;
                txtStartName.Text = AppSettings.Get("StartUserName", "");
            }
            finally { loading = false; }
        }

        void SetStartImage(string src, string displayName, bool askCrop)
        {
            Bitmap b = LoadImageFile(src, 2000);
            string dest = CopyIntoAppDir(src, "start_bg");
            if (launcher.Style.Photo != null) launcher.Style.Photo.Dispose();
            launcher.Style.Photo = b;
            launcher.Style.Crop = RectangleF.Empty;
            AppSettings.Set("StartImage", dest);
            AppSettings.Set("StartImageName", displayName);
            AppSettings.Set("StartCrop", "");
            launcher.ApplyStyle();
            LoadStart();
            status.Text = "已把「" + displayName + "」設為開始功能表的背景，可以按「打開看看」預覽。";
            if (askCrop) CropStart();
        }

        void PickStartImage()
        {
            string src = PickImageFile(this, "選擇開始功能表的背景照片");
            if (src == null) return;
            try { SetStartImage(src, Path.GetFileName(src), true); }
            catch (Exception ex) { MessageBox.Show(this, "無法使用這張照片：" + ex.Message, Text); }
        }

        void UseTaskbarImageForStart()
        {
            string path = AppSettings.Get("SkinImage", "");
            if (path.Length == 0 || !File.Exists(path)) { status.Text = "工作列還沒有設定照片，請先到「工作列」分頁選一張，或直接按「選擇照片…」。"; return; }
            try { SetStartImage(path, AppSettings.Get("SkinImageName", Path.GetFileName(path)), true); }
            catch (Exception ex) { MessageBox.Show(this, "無法使用這張照片：" + ex.Message, Text); }
        }

        void CropStart()
        {
            StartStyle st = launcher.Style;
            if (st.Photo == null) { status.Text = "請先選擇開始功能表的背景照片。"; return; }
            using (CropDialog dlg = new CropDialog(st.Photo, launcher.ClientSize.Width / (double)launcher.ClientSize.Height, st.Crop, "框選開始功能表要顯示的範圍", scale))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                st.Crop = dlg.Result;
                AppSettings.Set("StartCrop", CropRect.ToSetting(st.Crop));
                launcher.ApplyStyle();
                LoadStart();
                status.Text = "已更新開始功能表照片的顯示範圍，可以按「打開看看」預覽。";
            }
        }

        void RemoveStartImage()
        {
            if (launcher.Style.Photo != null) launcher.Style.Photo.Dispose();
            launcher.Style.Photo = null;
            launcher.Style.Crop = RectangleF.Empty;
            AppSettings.Set("StartImage", "");
            AppSettings.Set("StartCrop", "");
            launcher.ApplyStyle();
            LoadStart();
            status.Text = "已移除開始功能表的背景照片（改回毛玻璃背景）。";
        }

        void PreviewStart()
        {
            launcher.Preload();
            launcher.ShowLauncher();
        }

        public Bitmap RenderLauncher()
        {
            launcher.LoadNow();
            launcher.ApplyStyle();
            Bitmap b = new Bitmap(launcher.Width, launcher.Height);
            launcher.DrawToBitmap(b, new Rectangle(Point.Empty, launcher.Size));
            return b;
        }

        public void SaveLauncherFrame(string path) { launcher.SaveAnimationFrame(path); }

        public void DebugStartAnimation(int mode)
        {
            ShowPage(1);
            cbAnimSpeed.SelectedIndex = 2;
            cbAnim.SelectedIndex = mode; // 和使用者從選單選一樣，會觸發 RestartAnimation
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "dcs_debuganim.txt"), "status: " + status.Text + "\r\npatternIds: " + patternIds.Count, Encoding.UTF8);
        }

        public void DebugStopAndExit()
        {
            HaltAnimation();
            ForceExit();
        }

        public string LauncherInfo { get { return "apps=" + launcher.AppCount + "  " + launcher.CategorySummary; } }

        // ======================= 分頁 6：滑鼠游標 =======================

        Panel BuildCursorPage()
        {
            Panel page = NewSettingsPage();
            PageBuilder b = new PageBuilder(page, scale, Font);

            b.Section("Windows 內建游標");
            cbScheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(320) };
            try
            {
                foreach (CursorScheme s in CursorStyler.SystemSchemes()) cbScheme.Items.Add(s);
                string cur = CursorStyler.CurrentSchemeName;
                foreach (CursorScheme s in cbScheme.Items) if (s.Key == cur) cbScheme.SelectedItem = s;
                if (cbScheme.SelectedIndex < 0 && cbScheme.Items.Count > 0) cbScheme.SelectedIndex = 0;
            }
            catch { }
            b.Row("樣式", cbScheme, MakeButton("套用", S(80), true, ApplyCursorScheme));

            b.Section("自訂顏色游標");
            cursorSwatches = new SwatchPanel(CursorColors, scale, 12);
            cursorSwatches.SelectColor(cursorStyle.Fill);
            b.Row("填色", cursorSwatches);
            chkRainbow = new CheckBox { Text = "彩虹漸層", AutoSize = true };
            b.Row("", MakeButton("自訂顏色…", S(110), false, PickCursorColor), chkRainbow);
            cbOutline = MakeCombo(S(140), "白色外框", "黑色外框");
            cbOutline.SelectedIndex = 0;
            b.Row("外框", cbOutline);
            cbCursorSize = MakeCombo(S(140), "標準", "大", "特大");
            cbCursorSize.SelectedIndex = 0;
            b.Row("大小", cbCursorSize);
            cursorPreview = new CursorPreview(scale) { Width = S(460), Height = S(100), Style = cursorStyle };
            b.Row("預覽", cursorPreview);
            b.Row("", MakeButton("套用自訂游標", S(150), true, ApplyCustomCursor));
            b.Note("會換掉一般箭頭、連結手指和文字游標，其他游標（忙碌、調整大小等）使用 Windows 預設樣式。");

            b.Section("還原");
            b.Row("", MakeButton("還原成原本的游標", S(160), false, RestoreCursor),
                      MakeButton("Windows 預設游標", S(150), false, ApplyDefaultCursor));
            b.Note("第一次變更游標時，會自動備份你原本的游標設定，隨時可以按「還原成原本的游標」。");
            b.End();

            cursorSwatches.Picked += c => { cursorStyle.Fill = c; chkRainbow.Checked = false; cursorPreview.Invalidate(); };
            chkRainbow.CheckedChanged += delegate { cursorStyle.Rainbow = chkRainbow.Checked; cursorPreview.Invalidate(); };
            cbOutline.SelectedIndexChanged += delegate { cursorStyle.DarkOutline = cbOutline.SelectedIndex == 1; cursorPreview.Invalidate(); };
            cbCursorSize.SelectedIndexChanged += delegate { cursorStyle.Size = cbCursorSize.SelectedIndex; cursorPreview.Invalidate(); };
            return page;
        }

        void PickCursorColor()
        {
            using (ColorDialog dlg = new ColorDialog { FullOpen = true, Color = cursorStyle.Fill })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    cursorStyle.Fill = dlg.Color;
                    cursorSwatches.SelectColor(dlg.Color);
                    chkRainbow.Checked = false;
                    cursorPreview.Invalidate();
                }
        }

        void ApplyCursorScheme()
        {
            CursorScheme s = cbScheme.SelectedItem as CursorScheme;
            if (s == null) return;
            Try(() => CursorStyler.ApplyScheme(s), "已套用游標樣式「" + s.Display + "」");
        }

        void ApplyCustomCursor()
        {
            Try(() => CursorStyler.ApplyCustom(cursorStyle), "已套用自訂游標" + (cursorStyle.Rainbow ? "（彩虹漸層）" : ""));
        }

        void RestoreCursor()
        {
            if (!CursorStyler.HasBackup) { status.Text = "還沒有變更過游標，不需要還原。"; return; }
            Try(CursorStyler.RestoreBackup, "已還原成原本的游標");
        }

        void ApplyDefaultCursor()
        {
            CursorScheme aero = CursorStyler.SystemSchemes().FirstOrDefault(s => s.Key == "Windows Aero");
            if (aero == null) { status.Text = "找不到 Windows 預設游標配置。"; return; }
            Try(() => CursorStyler.ApplyScheme(aero), "已套用 Windows 預設游標");
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDPIAware(); // 桌面座標是實體像素，必須 DPI aware 才不會被縮放
            string cmd = args.Length > 0 ? args[0] : "";
            if (cmd == "--toggle-icons")
            {
                using (Desktop d = Desktop.Open()) d.IconsHidden = !d.IconsHidden;
                return;
            }
            if (cmd == "--scan" && args.Length >= 2) { ScanToFile(args[1]); return; }
            if (cmd == "--selftest" && args.Length >= 2) { SelfTest(args[1]); return; }
            if (cmd == "--render" && args.Length >= 2) { RenderPages(args[1]); return; }
            if (cmd == "--rendertest" && args.Length >= 2)
            {
                // 讓桌面圖示層自己畫一張圖：靜止時一張、波浪中一張，比較畫面有沒有真的動
                StringBuilder rb = new StringBuilder();
                IntPtr lv;
                using (Desktop d0 = Desktop.Open()) lv = d0.ListView;
                RECT lr;
                Native.GetWindowRect(lv, out lr);
                Rectangle area = lr.ToRectangle();
                Func<Bitmap> grab = () =>
                {
                    Bitmap b = new Bitmap(area.Width, area.Height);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        IntPtr hdc = g.GetHdc();
                        Native.PrintWindow(lv, hdc, 2 /*PW_RENDERFULLCONTENT*/);
                        g.ReleaseHdc(hdc);
                    }
                    return b;
                };
                using (Bitmap rest = grab())
                {
                    Bitmap moving;
                    using (PatternAnimator a = new PatternAnimator(3, 2, null))
                    {
                        System.Threading.Thread.Sleep(600);
                        moving = grab();
                    }
                    int diff = 0;
                    for (int y = 0; y < rest.Height; y += 4)
                        for (int x = 0; x < rest.Width; x += 4)
                            if (rest.GetPixel(x, y).ToArgb() != moving.GetPixel(x, y).ToArgb()) diff++;
                    rb.AppendLine("listview " + area + "  sampled pixels that differ: " + diff);
                    rest.Save(args[1] + ".rest.png");
                    moving.Save(args[1] + ".moving.png");
                    moving.Dispose();
                }
                File.WriteAllText(args[1], rb.ToString(), Encoding.UTF8);
                return;
            }
            if (cmd == "--probe" && args.Length >= 2)
            {
                // 只量測：3 秒內每個桌面圖示最多移動多遠（不移動任何東西）
                StringBuilder pb = new StringBuilder();
                using (Desktop probe = Desktop.Open())
                {
                    probe.LoadItems(false);
                    int[] mv = new int[probe.Items.Count];
                    System.Diagnostics.Stopwatch psw = System.Diagnostics.Stopwatch.StartNew();
                    while (psw.ElapsedMilliseconds < 3000)
                    {
                        for (int k = 0; k < probe.Items.Count; k++)
                        {
                            POINT q;
                            probe.View.GetItemPosition(probe.Items[k].Pidl, out q);
                            mv[k] = Math.Max(mv[k], Math.Abs(q.Y - probe.Items[k].Pos.Y) + Math.Abs(q.X - probe.Items[k].Pos.X));
                        }
                        System.Threading.Thread.Sleep(10);
                    }
                    pb.AppendLine("icons that moved: " + mv.Count(v => v > 3) + " / " + mv.Length + ", biggest move " + (mv.Length > 0 ? mv.Max() : 0) + "px");
                }
                File.WriteAllText(args[1], pb.ToString(), Encoding.UTF8);
                return;
            }
            if (cmd == "--debuganim" && args.Length >= 2)
            {
                // 開發用：照使用者的操作方式（切到像素圖案分頁、從選單選動畫）開始動畫，6 秒後自己停下來並結束
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm df = new MainForm(false, true);
                df.Shown += delegate
                {
                    System.Windows.Forms.Timer t1 = new System.Windows.Forms.Timer { Interval = 2500 };
                    t1.Tick += delegate { t1.Stop(); df.DebugStartAnimation(int.Parse(args[1])); };
                    t1.Start();
                    System.Windows.Forms.Timer t2 = new System.Windows.Forms.Timer { Interval = 9000 };
                    t2.Tick += delegate { t2.Stop(); df.DebugStopAndExit(); };
                    t2.Start();
                };
                Application.Run(df);
                return;
            }
            if (cmd == "--animtest" && args.Length >= 2)
            {
                StringBuilder sb = new StringBuilder();
                using (Desktop probe = Desktop.Open())
                {
                    probe.LoadItems(false);
                    DesktopItem first = probe.Items[0];
                    POINT start = first.Pos;
                    int minY = int.MaxValue, maxY = int.MinValue, changes = 0, lastY = start.Y;
                    int[] maxMove = new int[probe.Items.Count];
                    uint fl = probe.Flags;
                    sb.AppendLine("desktop flags before: 0x" + fl.ToString("X") + " snap=" + ((fl & Desktop.FWF_SNAPTOGRID) != 0) + " autoArrange=" + ((fl & Desktop.FWF_AUTOARRANGE) != 0));
                    using (PatternAnimator a = new PatternAnimator(args.Length >= 3 ? int.Parse(args[2]) : 3, 2, null))
                    {
                        sb.AppendLine("mode " + (args.Length >= 3 ? args[2] : "3") + ": animating " + a.Count + " icons");
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < 2000)
                        {
                            for (int k = 0; k < probe.Items.Count; k++) { POINT q; probe.View.GetItemPosition(probe.Items[k].Pidl, out q); maxMove[k] = Math.Max(maxMove[k], Math.Abs(q.Y - probe.Items[k].Pos.Y) + Math.Abs(q.X - probe.Items[k].Pos.X)); }
                            POINT p;
                            probe.View.GetItemPosition(first.Pidl, out p);
                            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                            if (p.Y != lastY) { changes++; lastY = p.Y; }
                            System.Threading.Thread.Sleep(5);
                        }
                    }
                    sb.AppendLine("icons that moved: " + maxMove.Count(v => v > 3) + " / " + maxMove.Length + ", biggest move " + maxMove.Max() + "px");
                    POINT end;
                    probe.View.GetItemPosition(first.Pidl, out end);
                    sb.AppendLine("first icon y range " + minY + ".." + maxY + " (amplitude " + (maxY - minY) + "px), position changed " + changes + " times in 2s (~" + (changes / 2) + " fps)");
                    sb.AppendLine("restored: " + (end.X == start.X && end.Y == start.Y) + " (" + end.X + "," + end.Y + ")");
                }
                File.WriteAllText(args[1], sb.ToString(), Encoding.UTF8);
                return;
            }
            if (cmd == "--frame" && args.Length >= 2)
            {
                MainForm f = new MainForm(false, true);
                f.SaveLauncherFrame(args[1]);
                f.ForceExit();
                return;
            }
            if (cmd == "--movetest" && args.Length >= 2) { File.WriteAllText(args[1], MoveTest(), Encoding.UTF8); return; }
            if (cmd == "--bench" && args.Length >= 2) { using (StartPanel p = new StartPanel(1f)) { p.AllowClose = true; File.WriteAllText(args[1], p.Benchmark(), Encoding.UTF8); } return; }

            bool firstInstance;
            using (Mutex mutex = new Mutex(true, "DesktopColorSorter_SingleInstance", out firstInstance))
            {
                if (!firstInstance)
                {
                    // 已經在執行（可能在通知區域）：叫它出來
                    Native.PostMessage((IntPtr)0xFFFF, Native.RegisterWindowMessage("DesktopColorSorter_Show"), IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(cmd == "--tray", false));
            }
        }

        // 診斷用：把第一個圖示往上移 20 像素再放回去，確認兩種移動方式哪一種有效
        static string MoveTest()
        {
            StringBuilder sb = new StringBuilder();
            using (Desktop d = Desktop.Open())
            {
                d.LoadItems(false);
                if (d.Items.Count == 0) return "no icons";
                DesktopItem it = d.Items[0];
                POINT p0 = it.Pos;
                uint flags = d.Flags;
                sb.AppendLine("icon=" + it.Name + " pos=" + p0.X + "," + p0.Y + " flags=0x" + flags.ToString("X"));
                d.SetFlags(Desktop.FWF_AUTOARRANGE | Desktop.FWF_SNAPTOGRID, 0);
                foreach (uint f in new[] { 0x80u | 0x80000000u, 0x80u })
                {
                    POINT moved;
                    d.View.SelectAndPositionItems(1, new[] { it.Pidl }, new[] { new POINT(p0.X, p0.Y - 20) }, f);
                    System.Threading.Thread.Sleep(150);
                    d.View.GetItemPosition(it.Pidl, out moved);
                    sb.AppendLine("flags 0x" + f.ToString("X") + " -> " + moved.X + "," + moved.Y + (moved.Y == p0.Y - 20 ? "  (moved)" : "  (NOT moved)"));
                    d.View.SelectAndPositionItems(1, new[] { it.Pidl }, new[] { p0 }, 0x80u);
                    System.Threading.Thread.Sleep(150);
                }
                POINT back;
                d.View.GetItemPosition(it.Pidl, out back);
                sb.AppendLine("restored=" + back.X + "," + back.Y);

                // 量測：一次移動全部圖示（波浪動畫每一格要做的事）要多久
                IntPtr[] pidls = d.Items.Select(x => x.Pidl).ToArray();
                POINT[] home = d.Items.Select(x => x.Pos).ToArray();
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                double worst = 0;
                for (int i = 0; i < 20; i++)
                {
                    double t0 = sw.Elapsed.TotalMilliseconds;
                    int dy = i % 2 == 0 ? -2 : 2;
                    d.MoveRaw(pidls, home.Select(p => new POINT(p.X, p.Y + dy)).ToArray());
                    worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds - t0);
                }
                double avg = sw.Elapsed.TotalMilliseconds / 20;
                d.MoveRaw(pidls, home);
                sb.AppendLine("move all " + pidls.Length + " icons: avg=" + avg.ToString("0.0") + "ms worst=" + worst.ToString("0.0") + "ms");
                uint on = flags & (Desktop.FWF_AUTOARRANGE | Desktop.FWF_SNAPTOGRID);
                if (on != 0) d.SetFlags(on, on);
            }
            return sb.ToString();
        }

        // 診斷用：只讀取、不移動任何圖示
        static void ScanToFile(string path)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                using (Desktop d = Desktop.Open())
                {
                    d.LoadItems(true);
                    Snapshot s = Snapshot.Take(d);
                    sb.AppendLine("spacing=" + s.Spacing.X + "x" + s.Spacing.Y + " autoArrange=" + d.AutoArrangeOn + " hidden=" + d.IconsHidden +
                        " area=" + s.Area + " origin=" + s.Origin.X + "," + s.Origin.Y + " grid=" + s.GridCols + "x" + s.GridRows);
                    foreach (DesktopItem it in ColorLayout.Sort(s.Items))
                        sb.AppendLine(string.Format("{0,5},{1,5}  {2,-6} hue={3,5:0} L={4:0.00}  {5}",
                            it.Pos.X, it.Pos.Y, ColorAnalyzer.GroupNames[it.Color.Group], it.Color.Hue, it.Color.Light, it.Name));
                    DumpPlan(sb, s, "heart", Shapes.Heart());
                    DumpPlan(sb, s, "text HI", Shapes.Text("HI", s.Spacing.X, s.Spacing.Y));
                }
            }
            catch (Exception ex) { sb.AppendLine("ERROR: " + ex); }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        static void DumpPlan(StringBuilder sb, Snapshot s, string title, PatternSource src)
        {
            using (src)
            {
                PatternPlan p = Pattern.Plan(s, src, true);
                sb.AppendLine("--- " + title + ": " + p.Cols + "x" + p.Rows + " used=" + p.Used + " total=" + p.Items.Count);
                char[,] grid = new char[s.GridCols, s.GridRows];
                for (int x = 0; x < s.GridCols; x++) for (int y = 0; y < s.GridRows; y++) grid[x, y] = '.';
                for (int i = 0; i < p.Cells.Count; i++)
                    if (p.Cells[i].X < s.GridCols) grid[p.Cells[i].X, p.Cells[i].Y] = i < p.Used ? '#' : 'o';
                for (int y = 0; y < s.GridRows; y++)
                {
                    for (int x = 0; x < s.GridCols; x++) sb.Append(grid[x, y]);
                    sb.AppendLine();
                }
            }
        }

        // 開發用：把每個分頁畫成圖片檢查版面（視窗放在螢幕外，不改變任何設定）
        static void RenderPages(string dir)
        {
            Application.EnableVisualStyles();
            MainForm f = new MainForm(false, true) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
            f.Shown += delegate
            {
                Application.DoEvents();
                for (int i = 0; i < MainForm.TabNames.Length; i++) f.RenderPage(i, Path.Combine(dir, "page" + i + ".png"));
                using (Bitmap b = f.RenderLauncher()) b.Save(Path.Combine(dir, "launcher.png"), System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllText(Path.Combine(dir, "launcher.txt"), f.LauncherInfo, Encoding.UTF8);
                f.ForceExit();
            };
            Application.Run(f);
        }

        // 自我測試：全部只讀取或在暫存資料夾操作，不改變任何系統設定
        static void SelfTest(string path)
        {
            StringBuilder sb = new StringBuilder();
            Action<string, Func<string>> t = (name, f) =>
            {
                try { sb.AppendLine(name + ": " + f()); } catch (Exception ex) { sb.AppendLine(name + ": ERROR " + ex.GetType().Name + " " + ex.Message); }
            };
            t("taskbar overlay", TaskbarSkin.SelfTest);
            t("cursor files", CursorArt.SelfTest);
            t("cursor schemes", () => string.Join(" | ", CursorStyler.SystemSchemes().Select(s => s.Display).ToArray()) + "  current=" + CursorStyler.CurrentSchemeName);
            t("theme", () => "systemLight=" + WinSettings.SystemLight + " appsLight=" + WinSettings.AppsLight + " transparency=" + WinSettings.Transparency +
                " accent=" + ColorTranslator.ToHtml(WinSettings.Accent) + " autoAccent=" + WinSettings.AutoAccent + " onStart=" + WinSettings.AccentOnStart + " onTitle=" + WinSettings.AccentOnTitle);
            t("taskbar", () => "left=" + WinSettings.TaskbarLeft + " search=" + WinSettings.SearchMode + " taskView=" + WinSettings.TaskView +
                " widgets=" + WinSettings.Widgets + " autoHide=" + WinSettings.TaskbarAutoHide + " seconds=" + WinSettings.ClockSeconds + " autoStart=" + WinSettings.AutoStart);
            t("app icon", () => { using (Icon i = AppIcon.Create()) return i.Width + "x" + i.Height; });
            t("start apps", () =>
            {
                List<AppEntry> apps = AppCatalog.LoadApps(48);
                return apps.Count + " apps, icons=" + apps.Count(a => a.Icon != null) + ", sample=" + string.Join(" / ", apps.Take(5).Select(a => a.Name).ToArray());
            });
            t("start buttons", () => string.Join(" ", WinKeyHook.FindStartButtons().Select(r => r.ToString()).ToArray()));
            t("start recent", () => AppCatalog.LoadRecent(6, 48).Count + " recent files");
            t("user name", () => UserInfo.DisplayName().Length > 0 ? "ok" : "empty");
            t("fullscreen check (current fg)", () => "fg class=" + Native.ClassName(Native.GetForegroundWindow()));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}
