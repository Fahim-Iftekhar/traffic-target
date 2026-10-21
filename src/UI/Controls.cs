using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace TrafficTarget
{
    // Base for owner-drawn controls that follow the current Theme.
    class ThemedControl : Control
    {
        public ThemedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }
    }

    // A result card: caption, big value + unit, one line of detail.
    class Tile : ThemedControl
    {
        public string Caption = "";
        public string Value = "";
        public string Unit = "";
        public string Sub = "";
        public Func<Palette, Color> AccentOf;
        public bool Compact;

        public Tile(string caption, Func<Palette, Color> accentOf) : this(caption, accentOf, 212, 96) { }

        public Tile(string caption, Func<Palette, Color> accentOf, int width, int height)
        {
            Caption = caption;
            AccentOf = accentOf;
            Size = new Size(UI.Px(width), UI.Px(height));
            Margin = new Padding(0, 0, UI.Px(12), UI.Px(12));
        }

        public void Set(string value, string unit, string sub)
        {
            if (value == Value && unit == Unit && sub == Sub) return;
            Value = value;
            Unit = unit;
            Sub = sub;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.Clear(p.Card);
            using (var b = new SolidBrush(AccentOf(p))) g.FillRectangle(b, 0, 0, UI.Px(5), Height);
            using (var pen = new Pen(p.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            int x = UI.Px(16);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, Caption.ToUpperInvariant(), UI.TileCaption, new Point(x, UI.Px(10)), p.Muted, flags);
            Font vf = Compact ? UI.TileValueSmall : UI.TileValue;
            Size vs = TextRenderer.MeasureText(g, Value, vf, Size.Empty, flags);
            int vy = Compact ? UI.Px(27) : UI.Px(28);
            TextRenderer.DrawText(g, Value, vf, new Point(x, vy), p.Text, flags);
            if (!string.IsNullOrEmpty(Unit))
            {
                Size us = TextRenderer.MeasureText(g, Unit, UI.TileUnit, Size.Empty, flags);
                TextRenderer.DrawText(g, Unit, UI.TileUnit, new Point(x + vs.Width + UI.Px(5), vy + vs.Height - us.Height - UI.Px(3)), p.Muted, flags);
            }
            var subRect = new Rectangle(x, Height - UI.Px(24), Width - x - UI.Px(8), UI.Px(18));
            TextRenderer.DrawText(g, Sub, UI.TileSub, subRect, p.Muted, flags | TextFormatFlags.EndEllipsis);
        }
    }

    // Underlined navigation tab (Today / Year / History).
    class NavTab : ThemedControl
    {
        bool selected, hot;

        public NavTab(string text)
        {
            Text = text;
            Font = UI.BodyBold;
            Cursor = Cursors.Hand;
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + UI.Px(30), UI.Px(40));
            Margin = new Padding(0);
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            e.Graphics.Clear(p.Page);
            Color c = selected || hot ? p.Text : p.Muted;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (selected)
                using (var b = new SolidBrush(p.Green)) e.Graphics.FillRectangle(b, UI.Px(8), Height - UI.Px(3), Width - UI.Px(16), UI.Px(3));
        }
    }

    // Small pill toggle (history range).
    class Chip : ThemedControl
    {
        bool selected, hot;
        public object Value;

        public Chip(string text, object value)
        {
            Text = text;
            Value = value;
            Font = UI.Small;
            Cursor = Cursors.Hand;
            Size = new Size(TextRenderer.MeasureText(text, UI.SmallBold).Width + UI.Px(26), UI.Px(28));
            Margin = new Padding(0, 0, UI.Px(6), 0);
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.Clear(p.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = UI.Round(r, Height / 2f))
            {
                if (selected)
                {
                    using (var b = new SolidBrush(p.Green)) g.FillPath(b, path);
                }
                else
                {
                    using (var b = new SolidBrush(hot ? p.Track : p.Card)) g.FillPath(b, path);
                    using (var pen = new Pen(p.Border)) g.DrawPath(pen, path);
                }
            }
            TextRenderer.DrawText(g, Text, selected ? UI.SmallBold : UI.Small, ClientRectangle, selected ? Color.White : p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // Label + percentage above a rounded progress bar.
    class ProgressLine : ThemedControl
    {
        public string LeftText = "", RightText = "";
        public double Fraction;
        public bool Warn;

        public void Set(string left, string right, double fraction, bool warn)
        {
            if (left == LeftText && right == RightText && Math.Abs(fraction - Fraction) < 0.0005 && warn == Warn) return;
            LeftText = left;
            RightText = right;
            Fraction = fraction;
            Warn = warn;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.Clear(p.Page);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, LeftText, UI.BodyBold, new Point(0, 0), p.Text, flags);
            Size rs = TextRenderer.MeasureText(g, RightText, UI.BodyBold, Size.Empty, flags);
            TextRenderer.DrawText(g, RightText, UI.BodyBold, new Point(Width - rs.Width - 1, 0), Warn ? p.Amber : p.Green, flags);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = UI.Px(10);
            var track = new RectangleF(0, Height - h - 1, Width - 1, h);
            using (var path = UI.Round(track, h / 2))
            using (var b = new SolidBrush(p.Track)) g.FillPath(b, path);
            double f = Math.Max(0, Math.Min(1, Fraction));
            if (f > 0)
            {
                var fill = new RectangleF(0, track.Y, Math.Max(h, (float)(track.Width * f)), h);
                using (var path = UI.Round(fill, h / 2))
                using (var b = new SolidBrush(Warn ? p.Amber : p.Green)) g.FillPath(b, path);
            }
        }
    }

    // One-line forecast with a coloured status dot. State: 0 neutral, 1 on track, 2 behind, 3 done.
    class PaceLine : ThemedControl
    {
        public string Message = "";
        public int State;

        public void Set(string message, int state)
        {
            if (message == Message && state == State) return;
            Message = message;
            State = state;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.Clear(p.Card);
            using (var pen = new Pen(p.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            Color c = State == 1 || State == 3 ? p.Green : State == 2 ? p.Amber : p.Muted;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = UI.Px(10);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, UI.Px(14), (Height - d) / 2, d, d);
            var r = new Rectangle(UI.Px(34), 0, Width - UI.Px(44), Height);
            TextRenderer.DrawText(g, Message, UI.Body, r, p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    class DayPoint
    {
        public DateTime Date;
        public double DownGB;
        public double UpGB;
        public double TotalGB { get { return DownGB + UpGB; } }
    }

    // Interactive daily traffic bar chart (stacked download/upload, target lines, hover tooltip, click to select).
    class HistoryChart : ThemedControl
    {
        List<DayPoint> pts = new List<DayPoint>();
        double target, plan;
        int hover = -1, mouseY;
        Rectangle plot;
        float slot;

        public int Selected = -1;
        public string EmptyText = "No traffic history yet";
        public event EventHandler PointChanged;

        public HistoryChart()
        {
            Cursor = Cursors.Hand;
        }

        public List<DayPoint> Points { get { return pts; } }
        public int Hover { get { return hover; } }

        public void SetData(List<DayPoint> points, double targetGB, double planGB)
        {
            if (points.Count != pts.Count) Selected = -1;
            pts = points;
            target = targetGB;
            plan = planGB;
            if (hover >= pts.Count) hover = -1;
            Invalidate();
        }

        static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double n = raw / mag;
            double st = n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10;
            return st * mag;
        }

        int Y(double v, double top)
        {
            return plot.Bottom - (int)Math.Round(v / top * plot.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            g.Clear(p.Card);
            using (var pen = new Pen(p.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            int lx = UI.Px(14), ly = UI.Px(10);
            lx = Legend(g, lx, ly, p.Download, "Download", false);
            lx = Legend(g, lx, ly, p.Upload, "Upload", false);
            lx = Legend(g, lx, ly, p.Amber, "Day target", true);
            if (plan > 0) Legend(g, lx, ly, p.Purple, "Today's share of year plan", true);

            if (pts.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, UI.Body, ClientRectangle, p.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            double max = 0;
            foreach (var d in pts) max = Math.Max(max, d.TotalGB);
            max = Math.Max(max, Math.Max(target, plan));
            if (max <= 0) max = 1;
            double step = NiceStep(max / 4);
            double top = Math.Ceiling(max * 1.05 / step) * step;

            plot = new Rectangle(UI.Px(62), UI.Px(36), Width - UI.Px(62) - UI.Px(16), Height - UI.Px(36) - UI.Px(30));
            if (plot.Width < 20 || plot.Height < 20) return;

            using (var gp = new Pen(p.Track))
            {
                for (double v = 0; v <= top + step * 0.01; v += step)
                {
                    int y = Y(v, top);
                    g.DrawLine(gp, plot.Left, y, plot.Right, y);
                    string lbl = v >= 1024 ? UI.Gen(v / 1024) + " TB" : UI.Gen(v) + " GB";
                    Size sz = TextRenderer.MeasureText(g, lbl, UI.Small, Size.Empty, flags);
                    TextRenderer.DrawText(g, lbl, UI.Small, new Point(plot.Left - UI.Px(6) - sz.Width, y - sz.Height / 2), p.Muted, flags);
                }
            }

            slot = (float)plot.Width / pts.Count;
            float bw = Math.Max(1f, slot * 0.7f);

            int hl = hover >= 0 ? hover : Selected;
            if (hl >= 0 && hl < pts.Count)
                using (var hb = new SolidBrush(Color.FromArgb(p.Dark ? 40 : 22, p.Text)))
                    g.FillRectangle(hb, plot.Left + hl * slot, plot.Top, slot, plot.Height);

            g.SmoothingMode = SmoothingMode.None;
            using (var bd = new SolidBrush(p.Download))
            using (var bu = new SolidBrush(p.Upload))
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    float x = plot.Left + i * slot + (slot - bw) / 2;
                    int y0 = plot.Bottom;
                    int yd = Y(pts[i].DownGB, top);
                    int yt = Y(pts[i].TotalGB, top);
                    if (y0 - yd > 0) g.FillRectangle(bd, x, yd, bw, y0 - yd);
                    if (yd - yt > 0) g.FillRectangle(bu, x, yt, bw, yd - yt);
                }
            }
            if (Selected >= 0 && Selected < pts.Count)
                using (var sp = new Pen(p.Text))
                    g.DrawRectangle(sp, plot.Left + Selected * slot, plot.Top, Math.Max(1f, slot - 1), plot.Height);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            DashLine(g, target, top, p.Amber);
            DashLine(g, plan, top, p.Purple);
            using (var ap = new Pen(p.Border)) g.DrawLine(ap, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

            int every = Math.Max(1, (int)Math.Ceiling(UI.Px(56) / slot));
            for (int i = pts.Count - 1; i >= 0; i -= every)
            {
                string t = (i == pts.Count - 1 && pts[i].Date == DateTime.Today) ? "Today" : pts[i].Date.ToString("d MMM", CultureInfo.InvariantCulture);
                Size sz = TextRenderer.MeasureText(g, t, UI.Small, Size.Empty, flags);
                int cx = (int)(plot.Left + i * slot + slot / 2);
                int x = Math.Max(plot.Left - UI.Px(12), Math.Min(cx - sz.Width / 2, Width - sz.Width - UI.Px(4)));
                TextRenderer.DrawText(g, t, UI.Small, new Point(x, plot.Bottom + UI.Px(7)), p.Muted, flags);
            }

            if (hover >= 0 && hover < pts.Count) DrawTip(g, hover);
        }

        void DashLine(Graphics g, double v, double top, Color c)
        {
            if (v <= 0) return;
            int y = Y(v, top);
            using (var pen = new Pen(c, UI.Px(2)))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, plot.Left, y, plot.Right, y);
            }
        }

        int Legend(Graphics g, int x, int y, Color c, string text, bool dashed)
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            g.SmoothingMode = SmoothingMode.None;
            if (dashed)
            {
                using (var pen = new Pen(c, UI.Px(2)))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, x, y + UI.Px(7), x + UI.Px(18), y + UI.Px(7));
                }
                x += UI.Px(23);
            }
            else
            {
                using (var b = new SolidBrush(c)) g.FillRectangle(b, x, y + UI.Px(2), UI.Px(10), UI.Px(10));
                x += UI.Px(15);
            }
            Size sz = TextRenderer.MeasureText(g, text, UI.Small, Size.Empty, flags);
            TextRenderer.DrawText(g, text, UI.Small, new Point(x, y), Theme.P.Muted, flags);
            return x + sz.Width + UI.Px(16);
        }

        void DrawTip(Graphics g, int i)
        {
            var d = pts[i];
            var p = Theme.P;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var lines = new List<string>();
            lines.Add(d.Date.ToString("ddd, d MMM yyyy", CultureInfo.InvariantCulture));
            lines.Add("Total:  " + UI.SizeText(d.TotalGB));
            lines.Add("Download:  " + UI.SizeText(d.DownGB));
            lines.Add("Upload:  " + UI.SizeText(d.UpGB));
            if (target > 0) lines.Add("Day target:  " + UI.F1(d.TotalGB / target * 100) + "%");
            int pad = UI.Px(9), lh = UI.Px(18), w = 0;
            for (int k = 0; k < lines.Count; k++)
                w = Math.Max(w, TextRenderer.MeasureText(g, lines[k], k == 0 ? UI.SmallBold : UI.Small, Size.Empty, flags).Width);
            w += pad * 2;
            int h = lh * lines.Count + pad * 2 - UI.Px(2);
            int cx = (int)(plot.Left + i * slot + slot / 2);
            int x = cx + UI.Px(14);
            if (x + w > Width - UI.Px(4)) x = cx - UI.Px(14) - w;
            if (x < UI.Px(4)) x = UI.Px(4);
            int y = Math.Max(plot.Top, Math.Min(mouseY - h / 2, plot.Bottom - h));
            var r = new RectangleF(x, y, w, h);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UI.Round(r, UI.Px(6)))
            {
                using (var b = new SolidBrush(p.Card2)) g.FillPath(b, path);
                using (var pen = new Pen(p.Border)) g.DrawPath(pen, path);
            }
            for (int k = 0; k < lines.Count; k++)
                TextRenderer.DrawText(g, lines[k], k == 0 ? UI.SmallBold : UI.Small, new Point(x + pad, y + pad + k * lh), k == 0 ? p.Text : p.Muted, flags);
        }

        int HitIndex(int x)
        {
            if (pts.Count == 0 || slot <= 0 || x < plot.Left || x >= plot.Right) return -1;
            int i = (int)((x - plot.Left) / slot);
            return Math.Max(0, Math.Min(pts.Count - 1, i));
        }

        void Raise()
        {
            if (PointChanged != null) PointChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouseY = e.Y;
            int idx = HitIndex(e.X);
            if (idx != hover)
            {
                hover = idx;
                Raise();
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            Invalidate();
            Raise();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (hover < 0) return;
            Selected = Selected == hover ? -1 : hover;
            Invalidate();
            Raise();
        }
    }

    // Draws the tray icon: a progress ring for today's target, a tick when done.
    static class TrayIcons
    {
        public static Icon Render(double fraction, bool done)
        {
            int sz = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    var r = new RectangleF(0.5f, 0.5f, sz - 1.5f, sz - 1.5f);
                    using (var b = new SolidBrush(Color.FromArgb(33, 115, 70))) g.FillEllipse(b, r);
                    float w = Math.Max(2f, sz * 0.15f);
                    var ar = RectangleF.Inflate(r, -w * 0.85f, -w * 0.85f);
                    if (done)
                    {
                        using (var pen = new Pen(Color.White, w))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            g.DrawLines(pen, new PointF[] {
                                new PointF(sz * 0.28f, sz * 0.52f), new PointF(sz * 0.44f, sz * 0.68f), new PointF(sz * 0.73f, sz * 0.34f) });
                        }
                    }
                    else
                    {
                        using (var track = new Pen(Color.FromArgb(95, 255, 255, 255), w)) g.DrawEllipse(track, ar);
                        double f = Math.Max(0, Math.Min(1, fraction));
                        if (f > 0.01)
                            using (var pen = new Pen(Color.White, w)) g.DrawArc(pen, ar, -90, (float)(360 * f));
                    }
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (var tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
                }
                finally
                {
                    Native.DestroyIcon(h);
                }
            }
        }
    }
}
