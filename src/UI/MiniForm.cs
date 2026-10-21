using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TrafficTarget
{
    // Small borderless always-on-top widget: required speed, what's left, live speed and a progress bar.
    class MiniForm : Form
    {
        readonly MainForm main;
        readonly AppSettings s;
        string big = "", line = "", live = "";
        double fraction;
        int state;
        bool dragging;
        Point dragFrom;

        public MiniForm(MainForm main, AppSettings settings)
        {
            this.main = main;
            s = settings;
            Text = "Traffic Target - mini view";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Size = new Size(UI.Px(256), UI.Px(92));
            Cursor = Cursors.SizeAll;

            var r = new Rectangle(s.MiniX, s.MiniY, Width, Height);
            bool visible = s.MiniX >= 0 && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r) &&
                Rectangle.Intersect(sc.WorkingArea, r).Width > Width / 2 && Rectangle.Intersect(sc.WorkingArea, r).Height > Height / 2);
            if (visible) Location = new Point(s.MiniX, s.MiniY);
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - UI.Px(16), wa.Bottom - Height - UI.Px(16));
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Traffic Target", null, delegate { main.ShowFromTray(); });
            menu.Items.Add("Close mini view", null, delegate { Close(); });
            ContextMenuStrip = menu;
            new ToolTip().SetToolTip(this, "Drag to move - double-click to open Traffic Target - right-click for options");
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;   // WS_EX_TOOLWINDOW: keep out of Alt+Tab
                return cp;
            }
        }

        public void SetData(string bigText, string lineText, string liveText, double frac, int paceState)
        {
            if (bigText == big && lineText == line && liveText == live && Math.Abs(frac - fraction) < 0.0005 && paceState == state) return;
            big = bigText;
            line = lineText;
            live = liveText;
            fraction = frac;
            state = paceState;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.Clear(p.Card);
            using (var pen = new Pen(p.Header, UI.Px(2))) g.DrawRectangle(pen, UI.Px(1), UI.Px(1), Width - UI.Px(2), Height - UI.Px(2));
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int x = UI.Px(14);
            TextRenderer.DrawText(g, big == "Done" ? "TODAY" : "NEED", UI.TileCaption, new Point(x, UI.Px(9)), p.Muted, flags);
            Size ls = TextRenderer.MeasureText(g, live, UI.Small, Size.Empty, flags);
            TextRenderer.DrawText(g, live, UI.Small, new Point(Width - ls.Width - UI.Px(14), UI.Px(9)), p.Muted, flags);
            TextRenderer.DrawText(g, big, UI.MiniBig, new Point(x - UI.Px(1), UI.Px(23)), p.Text, flags);
            TextRenderer.DrawText(g, line, UI.Small, new Rectangle(x, UI.Px(60), Width - x * 2, UI.Px(16)), p.Muted, flags | TextFormatFlags.EndEllipsis);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = UI.Px(5);
            var track = new RectangleF(x, Height - UI.Px(13), Width - x * 2, h);
            using (var path = UI.Round(track, h / 2))
            using (var b = new SolidBrush(p.Track)) g.FillPath(b, path);
            double f = Math.Max(0, Math.Min(1, fraction));
            if (f > 0)
            {
                var fill = new RectangleF(track.X, track.Y, Math.Max(h, (float)(track.Width * f)), h);
                using (var path = UI.Round(fill, h / 2))
                using (var b = new SolidBrush(state == 2 ? p.Amber : p.Green)) g.FillPath(b, path);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragFrom = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Location = new Point(Left + e.X - dragFrom.X, Top + e.Y - dragFrom.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            s.MiniX = Left;
            s.MiniY = Top;
            s.Save();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            main.ShowFromTray();
        }
    }
}
