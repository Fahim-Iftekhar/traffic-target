using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TrafficTarget
{
    class Palette
    {
        public bool Dark;
        public Color Page, Card, Card2, Border, Text, Muted, Input, InputAuto, Track;
        public Color Header, HeaderDark, HeaderText, HeaderMuted;
        public Color Green, Blue, Amber, Teal, Purple, Red, Download, Upload;

        public static Palette MakeLight()
        {
            var p = new Palette();
            p.Dark = false;
            p.Page = Color.FromArgb(243, 245, 247);
            p.Card = Color.White;
            p.Card2 = Color.FromArgb(248, 249, 250);
            p.Border = Color.FromArgb(214, 219, 224);
            p.Text = Color.FromArgb(32, 36, 40);
            p.Muted = Color.FromArgb(104, 112, 120);
            p.Input = Color.White;
            p.InputAuto = Color.FromArgb(226, 239, 252);
            p.Track = Color.FromArgb(226, 230, 234);
            p.Header = Color.FromArgb(33, 115, 70);
            p.HeaderDark = Color.FromArgb(24, 88, 53);
            p.HeaderText = Color.White;
            p.HeaderMuted = Color.FromArgb(214, 236, 222);
            p.Green = Color.FromArgb(30, 142, 78);
            p.Blue = Color.FromArgb(37, 99, 170);
            p.Amber = Color.FromArgb(214, 140, 0);
            p.Teal = Color.FromArgb(0, 128, 128);
            p.Purple = Color.FromArgb(112, 72, 170);
            p.Red = Color.FromArgb(196, 64, 40);
            p.Download = Color.FromArgb(37, 99, 170);
            p.Upload = Color.FromArgb(0, 160, 145);
            return p;
        }

        public static Palette MakeDark()
        {
            var p = new Palette();
            p.Dark = true;
            p.Page = Color.FromArgb(30, 31, 34);
            p.Card = Color.FromArgb(42, 44, 48);
            p.Card2 = Color.FromArgb(52, 55, 60);
            p.Border = Color.FromArgb(64, 68, 74);
            p.Text = Color.FromArgb(232, 234, 237);
            p.Muted = Color.FromArgb(156, 162, 168);
            p.Input = Color.FromArgb(48, 51, 55);
            p.InputAuto = Color.FromArgb(30, 54, 78);
            p.Track = Color.FromArgb(62, 66, 72);
            p.Header = Color.FromArgb(27, 94, 58);
            p.HeaderDark = Color.FromArgb(19, 70, 42);
            p.HeaderText = Color.White;
            p.HeaderMuted = Color.FromArgb(190, 225, 205);
            p.Green = Color.FromArgb(61, 190, 120);
            p.Blue = Color.FromArgb(106, 166, 240);
            p.Amber = Color.FromArgb(240, 176, 60);
            p.Teal = Color.FromArgb(64, 196, 180);
            p.Purple = Color.FromArgb(170, 140, 230);
            p.Red = Color.FromArgb(240, 110, 90);
            p.Download = Color.FromArgb(106, 166, 240);
            p.Upload = Color.FromArgb(64, 196, 180);
            return p;
        }
    }

    // Current colour palette (Light / Dark / follow Windows) and theming of standard controls.
    static class Theme
    {
        public static Palette P = Palette.MakeLight();
        public static string Mode = "System";

        public static bool SystemIsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return (int)v == 0;
                    }
                }
            }
            catch { }
            return false;
        }

        // Returns true when the palette changed.
        public static bool Set(string mode)
        {
            Mode = mode;
            bool dark = mode == "Dark" || (mode == "System" && SystemIsDark());
            if (dark == P.Dark) return false;
            P = dark ? Palette.MakeDark() : Palette.MakeLight();
            return true;
        }

        // Tags: "keep" = leave this control and its children alone, "card" = card background, "muted" = secondary text.
        public static void Apply(Control c)
        {
            string tag = c.Tag as string;
            if (tag == "keep") return;
            var p = P;
            if (c is ThemedControl)
            {
                c.Invalidate();
                return;
            }
            if (c is NumericUpDown || c is TextBox || c is ComboBox)
            {
                c.BackColor = p.Input;
                c.ForeColor = p.Text;
                return;
            }
            if (c is LinkLabel)
            {
                var l = (LinkLabel)c;
                l.LinkColor = p.Blue;
                l.ActiveLinkColor = p.Blue;
                l.VisitedLinkColor = p.Blue;
                l.DisabledLinkColor = p.Muted;
                l.BackColor = Color.Transparent;
                return;
            }
            if (c is Label)
            {
                c.ForeColor = tag == "muted" ? p.Muted : p.Text;
                c.BackColor = Color.Transparent;
                return;
            }
            if (c is CheckBox || c is RadioButton)
            {
                c.ForeColor = p.Text;
                c.BackColor = Color.Transparent;
                return;
            }
            if (c is Button)
            {
                var b = (Button)c;
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = p.Card2;
                b.ForeColor = p.Text;
                b.FlatAppearance.BorderColor = p.Border;
                b.FlatAppearance.MouseOverBackColor = p.Track;
                b.FlatAppearance.MouseDownBackColor = p.Border;
                return;
            }
            c.BackColor = tag == "card" ? p.Card : p.Page;
            c.ForeColor = p.Text;
            foreach (Control ch in c.Controls) Apply(ch);
        }
    }

    // Fonts, DPI scaling, number formatting and small control factories.
    static class UI
    {
        public static float Dpi = 1f;
        public static Font Body, BodyBold, Small, SmallBold, Input, Clock, ClockDate, TileCaption, TileValue, TileValueSmall, TileUnit, TileSub, Section, MiniBig;

        public static void Init()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Dpi = g.DpiX / 96f;
            }
            catch { Dpi = 1f; }
            if (Dpi < 1f) Dpi = 1f;
            string f = "Segoe UI";
            Body = new Font(f, 9.75f);
            BodyBold = new Font(f, 9.75f, FontStyle.Bold);
            Small = new Font(f, 8.5f);
            SmallBold = new Font(f, 8.5f, FontStyle.Bold);
            Input = new Font(f, 13f);
            Clock = new Font(f, 22f, FontStyle.Bold);
            ClockDate = new Font(f, 10.5f);
            TileCaption = new Font(f, 8.25f, FontStyle.Bold);
            TileValue = new Font(f, 21f, FontStyle.Bold);
            TileValueSmall = new Font(f, 15f, FontStyle.Bold);
            TileUnit = new Font(f, 10.5f);
            TileSub = new Font(f, 8.5f);
            Section = new Font(f, 11f, FontStyle.Bold);
            MiniBig = new Font(f, 20f, FontStyle.Bold);
        }

        public static int Px(double v) { return (int)Math.Round(v * Dpi); }

        public static string Gen(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "-";
            double r = Math.Round(v, 2);
            if (Math.Abs(r - Math.Round(r)) < 1e-9) return Math.Round(r).ToString("0", CultureInfo.CurrentCulture);
            return r.ToString("0.##", CultureInfo.CurrentCulture);
        }

        public static string F2(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "-";
            return v.ToString("0.00", CultureInfo.CurrentCulture);
        }

        public static string F1(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "-";
            return v.ToString("0.0", CultureInfo.CurrentCulture);
        }

        // Value + unit, switching GB to TB from 1024 GB.
        public static void SizeParts(double gb, out string value, out string unit)
        {
            if (Math.Abs(gb) >= 1024) { value = F2(gb / 1024); unit = "TB"; }
            else { value = F2(gb); unit = "GB"; }
        }

        public static string SizeText(double gb)
        {
            string v, u;
            SizeParts(gb, out v, out u);
            return v + " " + u;
        }

        public static string Speed(double bytesPerSec)
        {
            double mb = bytesPerSec / 1048576.0;
            if (mb >= 1) return F2(mb) + " MB/s";
            return F1(bytesPerSec / 1024.0) + " KB/s";
        }

        public static Label MakeLabel(string text, Font font, bool muted)
        {
            var l = new Label();
            l.Text = text;
            l.Font = font;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            if (muted) l.Tag = "muted";
            return l;
        }

        public static NumericUpDown MakeNumber(double value, double max)
        {
            var n = new NumericUpDown();
            n.Font = Input;
            n.DecimalPlaces = 2;
            n.Minimum = 0;
            n.Maximum = (decimal)max;
            n.Increment = 1;
            n.Width = Px(130);
            n.TextAlign = HorizontalAlignment.Right;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Value = Clamp(value, 0, max);
            return n;
        }

        public static decimal Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            if (v < min) v = min;
            if (v > max) v = max;
            return (decimal)Math.Round(v, 2);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
