// 框選照片要顯示的範圍：框的比例固定成目標（開始功能表 / 工作列）的形狀
//  拖曳框內 = 移動；拖曳四個角 = 縮放；滑鼠滾輪 = 放大縮小；雙擊 = 重設
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace DesktopColorSorter
{
    static class CropRect
    {
        // 以 0~1 的比例存（和圖片實際解析度無關）
        public static string ToSetting(RectangleF r)
        {
            if (r.IsEmpty) return "";
            CultureInfo ci = CultureInfo.InvariantCulture;
            return string.Join(",", new[] { r.X.ToString("0.#####", ci), r.Y.ToString("0.#####", ci), r.Width.ToString("0.#####", ci), r.Height.ToString("0.#####", ci) });
        }

        public static RectangleF FromSetting(string s)
        {
            if (string.IsNullOrEmpty(s)) return RectangleF.Empty;
            string[] p = s.Split(',');
            float x, y, w, h;
            CultureInfo ci = CultureInfo.InvariantCulture;
            if (p.Length != 4 || !float.TryParse(p[0], NumberStyles.Float, ci, out x) || !float.TryParse(p[1], NumberStyles.Float, ci, out y) ||
                !float.TryParse(p[2], NumberStyles.Float, ci, out w) || !float.TryParse(p[3], NumberStyles.Float, ci, out h)) return RectangleF.Empty;
            if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > 1.001f || y + h > 1.001f) return RectangleF.Empty;
            return new RectangleF(x, y, w, h);
        }

        // 把比例框換成圖片上的像素範圍，並在框內置中裁成剛好 aspect（寬/高）的比例；沒有框就是置中填滿
        public static RectangleF SourceRect(Size image, RectangleF crop, double aspect)
        {
            RectangleF r = crop.IsEmpty
                ? new RectangleF(0, 0, image.Width, image.Height)
                : new RectangleF(crop.X * image.Width, crop.Y * image.Height, crop.Width * image.Width, crop.Height * image.Height);
            if (aspect <= 0 || r.Width <= 0 || r.Height <= 0) return r;
            if (r.Width / r.Height > aspect)
            {
                float w = (float)(r.Height * aspect);
                return new RectangleF(r.X + (r.Width - w) / 2, r.Y, w, r.Height);
            }
            float h = (float)(r.Width / aspect);
            return new RectangleF(r.X, r.Y + (r.Height - h) / 2, r.Width, h);
        }
    }

    sealed class CropDialog : Form
    {
        readonly Bitmap image;
        readonly double aspect; // 目標的寬 / 高
        readonly float scale;
        readonly CropCanvas canvas;
        readonly CropPreview preview;
        RectangleF crop { get; set; } // 圖片像素座標

        public RectangleF Result { get { return new RectangleF(crop.X / image.Width, crop.Y / image.Height, crop.Width / image.Width, crop.Height / image.Height); } }

        int S(float v) { return (int)Math.Round(v * scale); }

        public CropDialog(Bitmap image, double aspect, RectangleF initial, string title, float scale)
        {
            this.image = image; this.aspect = aspect; this.scale = scale;
            AutoScaleMode = AutoScaleMode.None;
            Text = title;
            Font = new Font("Microsoft JhengHei UI", 9.5f);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(940), S(640));
            MinimumSize = new Size(S(640), S(460));
            BackColor = Color.FromArgb(32, 34, 40);
            ShowInTaskbar = false;

            bool wide = aspect > 2;
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = S(56), BackColor = Color.FromArgb(40, 42, 50) };
            Label hint = new Label
            {
                Text = "拖曳框內移動 · 拖曳四個角縮放 · 滑鼠滾輪放大縮小 · 雙擊重設", AutoSize = true, ForeColor = Color.FromArgb(190, 194, 204),
                Left = S(16), Top = S(19)
            };
            Button ok = DialogButton("確定", true);
            Button cancel = DialogButton("取消", false);
            Button reset = DialogButton("重設", false);
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            reset.Click += delegate { crop = FullRect(); Changed(); };
            bottom.Controls.AddRange(new Control[] { hint, ok, cancel, reset });
            bottom.Layout += delegate
            {
                ok.Left = bottom.ClientSize.Width - S(16) - ok.Width;
                cancel.Left = ok.Left - S(8) - cancel.Width;
                reset.Left = cancel.Left - S(8) - reset.Width;
                ok.Top = cancel.Top = reset.Top = (bottom.ClientSize.Height - ok.Height) / 2;
            };
            AcceptButton = ok;
            CancelButton = cancel;

            preview = new CropPreview(this) { BackColor = BackColor };
            if (wide) { preview.Dock = DockStyle.Bottom; preview.Height = S(90); }
            else { preview.Dock = DockStyle.Right; preview.Width = S(260); }
            canvas = new CropCanvas(this) { Dock = DockStyle.Fill, BackColor = BackColor };

            Controls.Add(canvas);
            Controls.Add(preview);
            Controls.Add(bottom);

            crop = initial.IsEmpty ? FullRect() : FitAspect(new RectangleF(initial.X * image.Width, initial.Y * image.Height, initial.Width * image.Width, initial.Height * image.Height));
        }

        Button DialogButton(string text, bool primary)
        {
            Button b = new Button { Text = text, Width = S(96), Height = S(34), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 94, 106);
            b.BackColor = primary ? Color.FromArgb(52, 112, 236) : Color.FromArgb(54, 57, 66);
            b.ForeColor = Color.White;
            return b;
        }

        // 圖片上能放下的最大框（置中）
        RectangleF FullRect()
        {
            float w = image.Width, h = (float)(w / aspect);
            if (h > image.Height) { h = image.Height; w = (float)(h * aspect); }
            return new RectangleF((image.Width - w) / 2, (image.Height - h) / 2, w, h);
        }

        // 舊的框比例不對（例如換了開始功能表大小）時，以中心為準調成正確比例並限制在圖片內
        RectangleF FitAspect(RectangleF r)
        {
            PointF c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            float w = (float)Math.Sqrt(r.Width * r.Height * aspect), h = (float)(w / aspect);
            RectangleF full = FullRect();
            if (w > full.Width) { w = full.Width; h = full.Height; }
            return Clamp(new RectangleF(c.X - w / 2, c.Y - h / 2, w, h));
        }

        RectangleF Clamp(RectangleF r)
        {
            r.X = Math.Max(0, Math.Min(r.X, image.Width - r.Width));
            r.Y = Math.Max(0, Math.Min(r.Y, image.Height - r.Height));
            return r;
        }

        float MinWidth { get { return Math.Max(16f, FullRect().Width * 0.08f); } }

        void Changed()
        {
            canvas.Invalidate();
            preview.Invalidate();
        }

        // ---------- 畫面 ----------

        sealed class CropPreview : Control
        {
            readonly CropDialog d;
            public CropPreview(CropDialog d)
            {
                this.d = d;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                int pad = d.S(16), cap = d.S(24);
                Rectangle area = new Rectangle(pad, pad + cap, Width - 2 * pad, Height - 2 * pad - cap);
                TextRenderer.DrawText(g, "預覽", d.Font, new Rectangle(pad, pad, Width, cap), Color.FromArgb(190, 194, 204), TextFormatFlags.Left | TextFormatFlags.Top);
                if (area.Width <= 0 || area.Height <= 0) return;
                float w = area.Width, h = (float)(w / d.aspect);
                if (h > area.Height) { h = area.Height; w = (float)(h * d.aspect); }
                RectangleF dest = new RectangleF(area.X + (area.Width - w) / 2, area.Y, w, h);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear; // 拖曳時每次移動都要重畫，用比較快的縮放
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(d.image, dest, d.crop, GraphicsUnit.Pixel);
                using (Pen p = new Pen(Color.FromArgb(120, 255, 255, 255))) g.DrawRectangle(p, dest.X, dest.Y, dest.Width, dest.Height);
            }
        }

        sealed class CropCanvas : Control
        {
            readonly CropDialog d;
            enum Drag { None, Move, Corner }
            Drag drag;
            int corner;               // 0 左上 1 右上 2 右下 3 左下
            PointF anchor, grabOffset;

            public CropCanvas(CropDialog d)
            {
                this.d = d;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            // 圖片在畫布上的位置與縮放
            RectangleF Display
            {
                get
                {
                    float pad = d.S(20), aw = Width - 2 * pad, ah = Height - 2 * pad;
                    if (aw <= 0 || ah <= 0) return RectangleF.Empty;
                    float k = Math.Min(aw / d.image.Width, ah / d.image.Height);
                    float w = d.image.Width * k, h = d.image.Height * k;
                    return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
                }
            }

            float K { get { RectangleF disp = Display; return disp.Width / d.image.Width; } }

            Bitmap shown;
            Bitmap ShownImage(RectangleF disp)
            {
                int w = Math.Max(1, (int)Math.Round(disp.Width)), h = Math.Max(1, (int)Math.Round(disp.Height));
                if (shown == null || shown.Width != w || shown.Height != h)
                {
                    if (shown != null) shown.Dispose();
                    shown = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(shown))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(d.image, new Rectangle(0, 0, w, h));
                    }
                }
                return shown;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && shown != null) { shown.Dispose(); shown = null; }
                base.Dispose(disposing);
            }

            RectangleF ToScreen(RectangleF r)
            {
                RectangleF disp = Display;
                float k = K;
                return new RectangleF(disp.X + r.X * k, disp.Y + r.Y * k, r.Width * k, r.Height * k);
            }

            PointF ToImage(Point p)
            {
                RectangleF disp = Display;
                float k = K;
                return k <= 0 ? PointF.Empty : new PointF((p.X - disp.X) / k, (p.Y - disp.Y) / k);
            }

            PointF[] Corners(RectangleF r)
            {
                return new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) };
            }

            int CornerAt(Point p)
            {
                PointF[] cs = Corners(ToScreen(d.crop));
                float tol = d.S(12);
                for (int i = 0; i < 4; i++)
                    if (Math.Abs(p.X - cs[i].X) <= tol && Math.Abs(p.Y - cs[i].Y) <= tol) return i;
                return -1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                RectangleF disp = Display;
                if (disp.IsEmpty) return;
                // 縮小後的照片只做一次（視窗大小變了才重做），拖曳框框時直接貼上，才不會卡
                Bitmap img = ShownImage(disp);
                g.DrawImage(img, new Rectangle((int)Math.Round(disp.X), (int)Math.Round(disp.Y), img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);

                RectangleF c = ToScreen(d.crop);
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                {
                    g.FillRectangle(shade, disp.X, disp.Y, disp.Width, c.Y - disp.Y);
                    g.FillRectangle(shade, disp.X, c.Bottom, disp.Width, disp.Bottom - c.Bottom);
                    g.FillRectangle(shade, disp.X, c.Y, c.X - disp.X, c.Height);
                    g.FillRectangle(shade, c.Right, c.Y, disp.Right - c.Right, c.Height);
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen thirds = new Pen(Color.FromArgb(90, 255, 255, 255)))
                    for (int i = 1; i <= 2; i++)
                    {
                        g.DrawLine(thirds, c.X + c.Width * i / 3, c.Y, c.X + c.Width * i / 3, c.Bottom);
                        g.DrawLine(thirds, c.X, c.Y + c.Height * i / 3, c.Right, c.Y + c.Height * i / 3);
                    }
                using (Pen border = new Pen(Color.White, Math.Max(1.5f, d.S(2)))) g.DrawRectangle(border, c.X, c.Y, c.Width, c.Height);
                float hs = d.S(10);
                using (SolidBrush hb = new SolidBrush(Color.White))
                using (Pen ho = new Pen(Color.FromArgb(52, 112, 236), Math.Max(1f, d.S(1.5f))))
                    foreach (PointF p in Corners(c))
                    {
                        g.FillRectangle(hb, p.X - hs / 2, p.Y - hs / 2, hs, hs);
                        g.DrawRectangle(ho, p.X - hs / 2, p.Y - hs / 2, hs, hs);
                    }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                Focus();
                int ci = CornerAt(e.Location);
                if (ci >= 0)
                {
                    drag = Drag.Corner;
                    corner = ci;
                    anchor = Corners(d.crop)[(ci + 2) % 4]; // 對角固定不動
                    return;
                }
                PointF ip = ToImage(e.Location);
                if (d.crop.Contains(ip))
                {
                    drag = Drag.Move;
                    grabOffset = new PointF(ip.X - d.crop.X, ip.Y - d.crop.Y);
                }
                else
                {
                    // 點在框外：把框移到那裡（以點的位置為中心）
                    d.crop = d.Clamp(new RectangleF(ip.X - d.crop.Width / 2, ip.Y - d.crop.Height / 2, d.crop.Width, d.crop.Height));
                    drag = Drag.Move;
                    grabOffset = new PointF(d.crop.Width / 2, d.crop.Height / 2);
                    d.Changed();
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (drag == Drag.None)
                {
                    int ci = CornerAt(e.Location);
                    Cursor = ci == 0 || ci == 2 ? Cursors.SizeNWSE : ci == 1 || ci == 3 ? Cursors.SizeNESW
                        : d.crop.Contains(ToImage(e.Location)) ? Cursors.SizeAll : Cursors.Default;
                    return;
                }
                PointF ip = ToImage(e.Location);
                if (drag == Drag.Move)
                {
                    d.crop = d.Clamp(new RectangleF(ip.X - grabOffset.X, ip.Y - grabOffset.Y, d.crop.Width, d.crop.Height));
                }
                else
                {
                    bool right = corner == 1 || corner == 2, down = corner == 2 || corner == 3;
                    float dx = Math.Abs(ip.X - anchor.X), dy = Math.Abs(ip.Y - anchor.Y);
                    float w = (float)Math.Max(dx, dy * d.aspect);
                    float maxW = right ? d.image.Width - anchor.X : anchor.X;
                    float maxH = down ? d.image.Height - anchor.Y : anchor.Y;
                    w = (float)Math.Min(w, Math.Min(maxW, maxH * d.aspect));
                    w = Math.Max(w, Math.Min(d.MinWidth, (float)Math.Min(maxW, maxH * d.aspect)));
                    float h = (float)(w / d.aspect);
                    d.crop = new RectangleF(right ? anchor.X : anchor.X - w, down ? anchor.Y : anchor.Y - h, w, h);
                }
                d.Changed();
            }

            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = Drag.None; }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                d.crop = d.FullRect();
                d.Changed();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                RectangleF full = d.FullRect(), c = d.crop;
                float f = e.Delta > 0 ? 0.9f : 1.1f;
                float w = Math.Max(d.MinWidth, Math.Min(full.Width, c.Width * f)), h = (float)(w / d.aspect);
                PointF center = new PointF(c.X + c.Width / 2, c.Y + c.Height / 2);
                d.crop = d.Clamp(new RectangleF(center.X - w / 2, center.Y - h / 2, w, h));
                d.Changed();
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); } // 讓滾輪事件送到畫布
        }
    }
}
