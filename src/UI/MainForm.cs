using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TrafficTarget
{
    class MainForm : Form
    {
        const double GB = 1073741824.0;
        const int TilesWidth = 3 * 224 - 12;   // three tiles per row

        readonly AppSettings s;
        readonly TrafficEngine engine;
        readonly Timer timer = new Timer();
        bool startHidden, exiting, updating, firstRefresh = true, miniPending;

        // window chrome
        Panel header, nav, footer, content;
        Label lblTime, lblDate, lblInfo, stStatus, stLive;
        Button btnMini, btnPin, btnSettings, btnAbout;
        NavTab[] tabs;
        Control[] views;
        int view = -1;
        NotifyIcon tray;
        Icon trayIcon;
        int trayPct = -1;
        bool trayDone;
        ToolStripMenuItem miMini;
        MiniForm mini;

        // Today
        NumericUpDown numDayGB, numDoneManual;
        Label lblDoneManual;
        CheckBox chkFollow;
        LinkLabel lnkPlan;
        ProgressLine progDay;
        PaceLine paceDay;
        Tile tSpeed, tNow, tPerMin, tRemain, tDone, tTimeLeft;

        // Year
        NumericUpDown numTarget, numCompleted;
        CheckBox chkAuto;
        ProgressLine progYear;
        PaceLine paceYear;
        Tile tPerDay, tPerWeek, tDaysLeft, tRequired, tCompleted, tToday;

        // History
        HistoryChart chart;
        List<Chip> chips = new List<Chip>();
        Label lblDetail;
        Tile hTotal, hAvg, hBest, hHit;
        DateTime chartBuiltAt = DateTime.MinValue;

        // state shared between views
        double dayTargetGB, planShareGB;
        int avgVersion = -1;
        DateTime avgDate = DateTime.MinValue;
        double avg7 = -1;
        DateTime notifiedDoneFor = DateTime.MinValue, lastBehindNotice = DateTime.MinValue;
        int dayState;

        readonly string shotDir;

        public MainForm(bool hidden) : this(hidden, null) { }

        public MainForm(bool hidden, string screenshotDir)
        {
            startHidden = hidden;
            shotDir = screenshotDir;
            s = AppSettings.Load();
            engine = new TrafficEngine(s);
            if (shotDir != null)
            {
                // demo data and fixed, readable settings for the screenshots
                engine.Demo = true;
                s.Source = "Builtin";
                s.CountMode = "Total";
                s.DayRequiredGB = 1024;
                s.FollowYearPlan = false;
                s.YearTargetTB = 120;
                s.YearCompletedAuto = true;
                s.NotifyDone = true;
                s.NotifyBehind = true;
                s.MiniOpen = false;
                s.TopMost = false;
                s.Theme = "System";
                s.HistoryDays = 30;
                s.LastView = 0;
            }
            Theme.Set(s.Theme);

            Text = AppInfo.Name;
            AutoScaleMode = AutoScaleMode.None;
            Font = UI.Body;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(UI.Px(760), UI.Px(610));
            MinimumSize = new Size(UI.Px(560), UI.Px(480));
            KeyPreview = true;
            TopMost = s.TopMost;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildHeader();
            BuildNav();
            BuildFooter();
            content = new Panel();
            content.Dock = DockStyle.Fill;
            views = new Control[] { BuildToday(), BuildYear(), BuildHistory() };
            foreach (var v in views)
            {
                v.Dock = DockStyle.Fill;
                v.Visible = false;
                content.Controls.Add(v);
            }
            Controls.Add(content);
            Controls.Add(nav);
            Controls.Add(header);
            Controls.Add(footer);
            BuildTray();

            ApplyTheme();
            ShowView(s.LastView);

            timer.Interval = 1000;
            timer.Tick += delegate { Tick(); };
            engine.Tick();
            RefreshAll();
            RefreshHistory(true);
            timer.Start();
            miniPending = s.MiniOpen;

            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.SessionEnding += OnSessionEnding;
            SystemEvents.UserPreferenceChanged += OnUserPreference;

            if (shotDir != null)
            {
                StartPosition = FormStartPosition.Manual;
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Left + UI.Px(60), wa.Top + UI.Px(40));
                TopMost = true;
                RunScreenshots();
            }
        }

        // ======================================================================= screenshot mode
        class Shot
        {
            public string Name;
            public Action Prepare;
            public Func<Form> Target;
            public Shot(string name, Action prepare, Func<Form> target) { Name = name; Prepare = prepare; Target = target; }
        }

        Form shotDialog;

        void SetThemeNow(string mode)
        {
            Theme.P = mode == "Dark" ? Palette.MakeDark() : Palette.MakeLight();
            ApplyTheme();
            Refresh();
        }

        void OpenShotDialog(Form f)
        {
            CloseShotDialog();
            shotDialog = f;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(Left + UI.Px(70), Top + UI.Px(50));
            f.TopMost = true;
            f.Show();
        }

        void CloseShotDialog()
        {
            if (shotDialog != null)
            {
                shotDialog.Close();
                shotDialog.Dispose();
                shotDialog = null;
            }
        }

        void RunScreenshots()
        {
            var steps = new List<Shot>();
            foreach (var mode in new string[] { "light", "dark" })
            {
                string m = mode;
                string pal = m == "dark" ? "Dark" : "Light";
                steps.Add(new Shot("today-" + m, delegate { CloseShotDialog(); SetThemeNow(pal); ShowView(0); }, () => this));
                steps.Add(new Shot("year-" + m, delegate { ShowView(1); }, () => this));
                steps.Add(new Shot("history-" + m, delegate
                {
                    ShowView(2);
                    if (chart.Points.Count > 4) chart.Selected = chart.Points.Count - 4;
                    UpdateDetail();
                    chart.Invalidate();
                }, () => this));
                steps.Add(new Shot("mini-" + m, delegate
                {
                    ShowMini();
                    mini.Location = new Point(Right - mini.Width - UI.Px(30), Top + UI.Px(120));
                    mini.Refresh();
                }, () => mini));
                steps.Add(new Shot("settings-" + m, delegate
                {
                    if (mini != null) mini.Close();
                    OpenShotDialog(new SettingsForm(s, engine));
                }, () => shotDialog));
                steps.Add(new Shot("about-" + m, delegate { OpenShotDialog(new AboutForm()); }, () => shotDialog));
            }

            int idx = 0;
            bool prepared = false;
            var t = new Timer();
            t.Interval = 1500;
            t.Tick += delegate
            {
                if (idx >= steps.Count)
                {
                    t.Stop();
                    CloseShotDialog();
                    ExitApp();
                    return;
                }
                var st = steps[idx];
                if (!prepared)
                {
                    try { st.Prepare(); } catch { }
                    prepared = true;
                    return;
                }
                try
                {
                    var f = st.Target();
                    if (f != null)
                    {
                        f.Refresh();
                        Rectangle r = Native.VisibleBounds(f);
                        using (var bmp = new Bitmap(r.Width, r.Height))
                        {
                            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                            bmp.Save(Path.Combine(shotDir, st.Name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText(Path.Combine(shotDir, "errors.txt"), st.Name + ": " + ex + Environment.NewLine); } catch { }
                }
                prepared = false;
                idx++;
            };
            t.Start();
        }

        // ======================================================================= layout
        void BuildHeader()
        {
            header = new Panel();
            header.Tag = "keep";
            header.Dock = DockStyle.Top;
            header.Height = UI.Px(78);
            header.Padding = new Padding(UI.Px(20), UI.Px(8), UI.Px(14), UI.Px(8));

            var left = new FlowLayoutPanel();
            left.FlowDirection = FlowDirection.TopDown;
            left.WrapContents = false;
            left.AutoSize = true;
            left.Dock = DockStyle.Left;
            left.BackColor = Color.Transparent;
            lblTime = UI.MakeLabel("", UI.Clock, false);
            lblTime.Margin = new Padding(0);
            lblDate = UI.MakeLabel("", UI.ClockDate, false);
            lblDate.Margin = new Padding(UI.Px(3), 0, 0, 0);
            left.Controls.Add(lblTime);
            left.Controls.Add(lblDate);

            var right = new FlowLayoutPanel();
            right.FlowDirection = FlowDirection.RightToLeft;
            right.WrapContents = false;
            right.AutoSize = true;
            right.Dock = DockStyle.Right;
            right.BackColor = Color.Transparent;
            right.Padding = new Padding(0, UI.Px(14), 0, 0);
            btnAbout = HeaderButton("?", "About Traffic Target (F1)");
            btnAbout.Click += delegate { ShowAbout(); };
            btnSettings = HeaderButton("Settings", "Settings (Ctrl+,)");
            btnSettings.Click += delegate { OpenSettings(); };
            btnPin = HeaderButton("Pin", "Keep this window on top (Ctrl+T)");
            btnPin.Click += delegate { TogglePin(); };
            btnMini = HeaderButton("Mini view", "Small always-on-top widget (Ctrl+M)");
            btnMini.Click += delegate { ToggleMini(); };
            right.Controls.Add(btnAbout);
            right.Controls.Add(btnSettings);
            right.Controls.Add(btnPin);
            right.Controls.Add(btnMini);

            header.Controls.Add(right);
            header.Controls.Add(left);
        }

        readonly ToolTip tips = new ToolTip();

        Button HeaderButton(string text, string tip)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.Font = UI.Body;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(UI.Px(8), UI.Px(3), UI.Px(8), UI.Px(3));
            b.Margin = new Padding(UI.Px(6), 0, 0, 0);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.TabStop = false;
            tips.SetToolTip(b, tip);
            return b;
        }

        void BuildNav()
        {
            nav = new Panel();
            nav.Dock = DockStyle.Top;
            nav.Height = UI.Px(42);
            nav.Padding = new Padding(UI.Px(10), 0, UI.Px(16), 0);
            nav.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.P.Border)) e.Graphics.DrawLine(pen, 0, nav.Height - 1, nav.Width, nav.Height - 1);
            };
            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Left;
            flow.AutoSize = true;
            flow.WrapContents = false;
            flow.Padding = new Padding(0, UI.Px(1), 0, 0);
            string[] names = { "Today", "Year", "History" };
            tabs = new NavTab[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                tabs[i] = new NavTab(names[i]);
                tabs[i].Click += delegate { ShowView(idx); };
                tips.SetToolTip(tabs[i], names[i] + " (Ctrl+" + (i + 1) + ")");
                flow.Controls.Add(tabs[i]);
            }
            lblInfo = UI.MakeLabel("", UI.Body, true);
            lblInfo.AutoSize = false;
            lblInfo.Dock = DockStyle.Fill;
            lblInfo.TextAlign = ContentAlignment.MiddleRight;
            nav.Controls.Add(lblInfo);
            nav.Controls.Add(flow);
        }

        void BuildFooter()
        {
            footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = UI.Px(28);
            footer.Padding = new Padding(UI.Px(12), 0, UI.Px(12), 0);
            footer.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.P.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            stLive = UI.MakeLabel("", UI.Small, true);
            stLive.Dock = DockStyle.Right;
            stLive.AutoSize = true;
            stLive.Padding = new Padding(0, UI.Px(6), 0, 0);
            stStatus = UI.MakeLabel("", UI.Small, true);
            stStatus.AutoSize = false;
            stStatus.Dock = DockStyle.Fill;
            stStatus.TextAlign = ContentAlignment.MiddleLeft;
            stStatus.AutoEllipsis = true;
            footer.Controls.Add(stStatus);
            footer.Controls.Add(stLive);
        }

        FlowLayoutPanel NewFlowView()
        {
            var flow = new FlowLayoutPanel();
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.Padding = new Padding(UI.Px(18), UI.Px(14), UI.Px(18), UI.Px(10));
            return flow;
        }

        FlowLayoutPanel Row(int maxWidth)
        {
            var r = new FlowLayoutPanel();
            r.FlowDirection = FlowDirection.LeftToRight;
            r.WrapContents = true;
            r.AutoSize = true;
            r.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            r.Margin = new Padding(0, 0, 0, UI.Px(10));
            if (maxWidth > 0) r.MaximumSize = new Size(UI.Px(maxWidth), 0);
            return r;
        }

        Label FieldLabel(string text)
        {
            var l = UI.MakeLabel(text, UI.BodyBold, false);
            l.Margin = new Padding(0, UI.Px(9), UI.Px(8), 0);
            return l;
        }

        T Sized<T>(T c, int width, int height, int bottomMargin) where T : Control
        {
            c.Size = new Size(UI.Px(width), UI.Px(height));
            c.Margin = new Padding(0, 0, 0, UI.Px(bottomMargin));
            return c;
        }

        Control BuildToday()
        {
            var v = NewFlowView();

            var inputs = Row(TilesWidth + 12);
            inputs.Controls.Add(FieldLabel("Required GB for the day"));
            numDayGB = UI.MakeNumber(s.DayRequiredGB, 1000000);
            numDayGB.Margin = new Padding(0, 0, UI.Px(12), 0);
            numDayGB.ValueChanged += delegate
            {
                if (updating || s.FollowYearPlan) return;
                s.DayRequiredGB = (double)numDayGB.Value;
                s.Save();
                RefreshAll();
            };
            inputs.Controls.Add(numDayGB);

            chkFollow = new CheckBox();
            chkFollow.Text = "Follow year plan";
            chkFollow.AutoSize = true;
            chkFollow.Checked = s.FollowYearPlan;
            chkFollow.Margin = new Padding(0, UI.Px(8), UI.Px(12), 0);
            tips.SetToolTip(chkFollow, "Use today's share of the year target as the day target");
            chkFollow.CheckedChanged += delegate
            {
                if (updating) return;
                s.FollowYearPlan = chkFollow.Checked;
                if (!s.FollowYearPlan)
                {
                    updating = true;
                    numDayGB.Value = UI.Clamp(s.DayRequiredGB, 0, 1000000);
                    updating = false;
                }
                s.Save();
                RefreshAll();
            };
            inputs.Controls.Add(chkFollow);

            lnkPlan = new LinkLabel();
            lnkPlan.AutoSize = true;
            lnkPlan.Margin = new Padding(0, UI.Px(9), 0, 0);
            lnkPlan.LinkBehavior = LinkBehavior.HoverUnderline;
            lnkPlan.LinkClicked += delegate
            {
                if (planShareGB > 0) numDayGB.Value = UI.Clamp(planShareGB, 0, 1000000);
            };
            inputs.Controls.Add(lnkPlan);

            lblDoneManual = FieldLabel("Completed today (GB)");
            inputs.Controls.Add(lblDoneManual);
            numDoneManual = UI.MakeNumber(s.DayCompletedManualGB, 1000000);
            numDoneManual.ValueChanged += delegate
            {
                if (updating) return;
                s.DayCompletedManualGB = (double)numDoneManual.Value;
                s.Save();
                RefreshAll();
            };
            inputs.Controls.Add(numDoneManual);
            v.Controls.Add(inputs);

            progDay = Sized(new ProgressLine(), TilesWidth, 40, 10);
            v.Controls.Add(progDay);
            paceDay = Sized(new PaceLine(), TilesWidth, 46, 12);
            v.Controls.Add(paceDay);

            var tiles = Row(TilesWidth + 12);
            tSpeed = new Tile("Required speed", p => p.Green);
            tNow = new Tile("Current speed", p => p.Teal);
            tPerMin = new Tile("Per minute", p => p.Blue);
            tRemain = new Tile("Remaining today", p => p.Amber);
            tDone = new Tile("Completed today", p => p.Green);
            tTimeLeft = new Tile("Time left today", p => p.Purple);
            tiles.Controls.AddRange(new Control[] { tSpeed, tNow, tPerMin, tRemain, tDone, tTimeLeft });
            v.Controls.Add(tiles);
            return v;
        }

        Control BuildYear()
        {
            var v = NewFlowView();

            var inputs = Row(TilesWidth + 12);
            inputs.Controls.Add(FieldLabel("Target (TB)"));
            numTarget = UI.MakeNumber(s.YearTargetTB, 100000);
            numTarget.Margin = new Padding(0, 0, UI.Px(24), 0);
            numTarget.ValueChanged += delegate
            {
                if (updating) return;
                s.YearTargetTB = (double)numTarget.Value;
                s.Save();
                RefreshAll();
            };
            inputs.Controls.Add(numTarget);

            inputs.Controls.Add(FieldLabel("Completed (TB)"));
            numCompleted = UI.MakeNumber(s.YearCompletedManualTB, 100000);
            numCompleted.Margin = new Padding(0, 0, UI.Px(10), 0);
            numCompleted.ValueChanged += delegate
            {
                if (updating || chkAuto.Checked) return;
                s.YearCompletedManualTB = (double)numCompleted.Value;
                s.Save();
                RefreshAll();
            };
            inputs.Controls.Add(numCompleted);

            chkAuto = new CheckBox();
            chkAuto.Text = "Auto";
            chkAuto.AutoSize = true;
            chkAuto.Margin = new Padding(0, UI.Px(8), 0, 0);
            chkAuto.Checked = s.YearCompletedAuto;
            tips.SetToolTip(chkAuto, "Completed (TB) = this year's total from the traffic history");
            chkAuto.CheckedChanged += delegate
            {
                if (updating) return;
                s.YearCompletedAuto = chkAuto.Checked;
                if (!chkAuto.Checked)
                {
                    updating = true;
                    numCompleted.Value = UI.Clamp(s.YearCompletedManualTB, 0, 100000);
                    updating = false;
                }
                s.Save();
                RefreshAll();
            };
            inputs.Controls.Add(chkAuto);
            v.Controls.Add(inputs);

            progYear = Sized(new ProgressLine(), TilesWidth, 40, 10);
            v.Controls.Add(progYear);
            paceYear = Sized(new PaceLine(), TilesWidth, 46, 12);
            v.Controls.Add(paceYear);

            var tiles = Row(TilesWidth + 12);
            tPerDay = new Tile("Per day", p => p.Green);
            tPerWeek = new Tile("Per week", p => p.Blue);
            tDaysLeft = new Tile("Days left this year", p => p.Purple);
            tRequired = new Tile("Required", p => p.Amber);
            tCompleted = new Tile("Completed this year", p => p.Teal);
            tToday = new Tile("Today vs plan", p => p.Green);
            tiles.Controls.AddRange(new Control[] { tPerDay, tPerWeek, tDaysLeft, tRequired, tCompleted, tToday });
            v.Controls.Add(tiles);
            return v;
        }

        Control BuildHistory()
        {
            var t = new TableLayoutPanel();
            t.ColumnCount = 1;
            t.RowCount = 4;
            t.Padding = new Padding(UI.Px(18), UI.Px(12), UI.Px(18), UI.Px(8));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var bar = new Panel();
            bar.Height = UI.Px(34);
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0, 0, 0, UI.Px(8));
            var chipFlow = new FlowLayoutPanel();
            chipFlow.Dock = DockStyle.Left;
            chipFlow.AutoSize = true;
            chipFlow.WrapContents = false;
            int[] ranges = { 14, 30, 90, 0 };
            string[] labels = { "14 days", "30 days", "90 days", "This year" };
            for (int i = 0; i < ranges.Length; i++)
            {
                var c = new Chip(labels[i], ranges[i]);
                c.Click += delegate (object sender, EventArgs e)
                {
                    s.HistoryDays = (int)((Chip)sender).Value;
                    s.Save();
                    chart.Selected = -1;
                    RefreshHistory(true);
                };
                chips.Add(c);
                chipFlow.Controls.Add(c);
            }
            var export = new Button();
            export.Text = "Export CSV...";
            export.AutoSize = true;
            export.Dock = DockStyle.Right;
            export.Cursor = Cursors.Hand;
            tips.SetToolTip(export, "Save the full daily history as a CSV file (Ctrl+E)");
            export.Click += delegate { ExportCsv(); };
            bar.Controls.Add(chipFlow);
            bar.Controls.Add(export);
            t.Controls.Add(bar, 0, 0);

            chart = new HistoryChart();
            chart.Dock = DockStyle.Fill;
            chart.Margin = new Padding(0, 0, 0, UI.Px(6));
            chart.MinimumSize = new Size(0, UI.Px(160));
            chart.PointChanged += delegate { UpdateDetail(); };
            t.Controls.Add(chart, 0, 1);

            lblDetail = UI.MakeLabel("", UI.Body, false);
            lblDetail.AutoSize = false;
            lblDetail.Height = UI.Px(24);
            lblDetail.Dock = DockStyle.Fill;
            lblDetail.TextAlign = ContentAlignment.MiddleLeft;
            lblDetail.AutoEllipsis = true;
            lblDetail.Margin = new Padding(0, 0, 0, UI.Px(6));
            t.Controls.Add(lblDetail, 0, 2);

            var stats = new FlowLayoutPanel();
            stats.AutoSize = true;
            stats.WrapContents = true;
            stats.Dock = DockStyle.Fill;
            stats.Margin = new Padding(0);
            hTotal = new Tile("Total", p => p.Blue, 164, 80);
            hAvg = new Tile("Daily average", p => p.Teal, 164, 80);
            hBest = new Tile("Best day", p => p.Green, 164, 80);
            hHit = new Tile("Days on target", p => p.Amber, 164, 80);
            foreach (var tile in new Tile[] { hTotal, hAvg, hBest, hHit })
            {
                tile.Compact = true;
                tile.Margin = new Padding(0, 0, UI.Px(10), UI.Px(4));
                stats.Controls.Add(tile);
            }
            t.Controls.Add(stats, 0, 3);
            return t;
        }

        void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = Icon ?? SystemIcons.Application;
            tray.Text = AppInfo.Name;
            tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Traffic Target", null, delegate { ShowFromTray(); });
            miMini = new ToolStripMenuItem("Mini view", null, delegate { ToggleMini(); });
            menu.Items.Add(miMini);
            menu.Items.Add("Settings", null, delegate { ShowFromTray(); OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowFromTray(); };
            tray.MouseClick += delegate (object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowFromTray(); };
            tray.BalloonTipClicked += delegate { ShowFromTray(); };
        }

        // ======================================================================= theme
        void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.Page;
            Theme.Apply(content);
            Theme.Apply(nav);
            Theme.Apply(footer);
            header.BackColor = p.Header;
            lblTime.ForeColor = p.HeaderText;
            lblDate.ForeColor = p.HeaderMuted;
            foreach (var b in new Button[] { btnMini, btnPin, btnSettings, btnAbout })
            {
                b.ForeColor = p.HeaderText;
                b.FlatAppearance.BorderColor = Color.FromArgb(150, 205, 175);
                b.FlatAppearance.MouseOverBackColor = p.HeaderDark;
                b.FlatAppearance.MouseDownBackColor = p.HeaderDark;
            }
            UpdateHeaderButtons();
            nav.Invalidate();
            footer.Invalidate();
            if (IsHandleCreated) Native.SetDarkTitleBar(Handle, p.Dark);
            if (mini != null) mini.Invalidate();
            Invalidate(true);
        }

        void UpdateHeaderButtons()
        {
            var p = Theme.P;
            btnPin.BackColor = s.TopMost ? p.HeaderDark : p.Header;
            btnPin.Text = s.TopMost ? "Pinned" : "Pin";
            bool miniOn = mini != null && mini.Visible;
            btnMini.BackColor = miniOn ? p.HeaderDark : p.Header;
            btnSettings.BackColor = p.Header;
            btnAbout.BackColor = p.Header;
            if (miMini != null) miMini.Checked = miniOn;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkTitleBar(Handle, Theme.P.Dark);
        }

        void OnUserPreference(object sender, UserPreferenceChangedEventArgs e)
        {
            if (s.Theme != "System" || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate { if (Theme.Set("System")) ApplyTheme(); });
            }
            catch { }
        }

        // ======================================================================= behaviour
        void ShowView(int i)
        {
            if (i < 0 || i >= views.Length) i = 0;
            view = i;
            for (int k = 0; k < views.Length; k++)
            {
                views[k].Visible = k == i;
                tabs[k].Selected = k == i;
            }
            if (s.LastView != i)
            {
                s.LastView = i;
                s.Save();
            }
            if (i == 2) RefreshHistory(true);
        }

        void Tick()
        {
            engine.Tick();
            RefreshAll();
            if (miniPending)
            {
                miniPending = false;
                ShowMini();
            }
        }

        void RefreshAll()
        {
            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            lblTime.Text = now.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture);
            lblDate.Text = now.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);

            bool manual = s.Source == "Manual";
            double doneToday = manual ? s.DayCompletedManualGB : engine.Pick(engine.Today) / GB;

            // ---- Year plan (Excel "Year" sheet)
            bool auto = s.YearCompletedAuto && !manual;
            double beforeTB = engine.Pick(engine.YearBeforeToday) / GB / 1024.0;
            double doneTB = auto ? beforeTB + doneToday / 1024.0 : s.YearCompletedManualTB;
            var yp = YearPlan.Calc(s.YearTargetTB, doneTB, today);
            double startTB = auto ? beforeTB : s.YearCompletedManualTB;
            planShareGB = Math.Max(0, (s.YearTargetTB - startTB) * 1024 / yp.DaysLeft);

            // ---- Day plan (Excel "Day" sheet)
            dayTargetGB = s.FollowYearPlan ? planShareGB : s.DayRequiredGB;
            var dp = DayPlan.Calc(dayTargetGB, doneToday, now);
            double nowBps = engine.Rate(5, 0);
            double paceBps = engine.Rate(600, 0);

            RefreshToday(dp, now, nowBps, paceBps, manual);
            RefreshYear(yp, doneToday, doneTB, auto, manual);

            lblInfo.Text = "Day ends in " + dp.TimeLeftText + "   \u00B7   " + yp.DaysLeft + " days left in " + today.Year;
            stStatus.Text = engine.Status;
            stLive.Text = manual ? "" : "Today:  down " + UI.SizeText(engine.Today.Down / GB) + "   up " + UI.SizeText(engine.Today.Up / GB);

            RefreshHistory(false);
            UpdateTray(dp);
            UpdateMini(dp, nowBps);
            Notify(dp, paceBps, now, manual);
        }

        void RefreshToday(DayPlan dp, DateTime now, double nowBps, double paceBps, bool manual)
        {
            var p = Theme.P;
            lblDoneManual.Visible = manual;
            numDoneManual.Visible = manual;
            lnkPlan.Visible = !s.FollowYearPlan && planShareGB > 0;
            string lt = "Use year plan (" + UI.F2(planShareGB) + " GB)";
            if (lnkPlan.Text != lt) lnkPlan.Text = lt;
            numDayGB.ReadOnly = s.FollowYearPlan;
            numDayGB.Increment = s.FollowYearPlan ? 0 : 1;
            Color nb = s.FollowYearPlan ? p.InputAuto : p.Input;
            if (numDayGB.BackColor != nb) numDayGB.BackColor = nb;
            if (s.FollowYearPlan && !numDayGB.Focused)
            {
                updating = true;
                numDayGB.Value = UI.Clamp(planShareGB, 0, 1000000);
                updating = false;
            }

            // forecast from the 10-minute average speed
            string msg;
            int state;
            double secsLeft = Math.Max(0, (now.Date.AddDays(1) - now).TotalSeconds);
            if (dp.Done)
            {
                msg = "Day target reached - " + UI.SizeText(dp.DoneGB) + " done today. Nice work!";
                state = 3;
            }
            else if (manual)
            {
                msg = "Need " + UI.F2(dp.MBPerSecond) + " MB/s from now until midnight to finish today's target.";
                state = 0;
            }
            else if (engine.SampleSpan < 60)
            {
                msg = "Measuring your speed - the forecast appears after about a minute.";
                state = 0;
            }
            else
            {
                double gbps = paceBps / GB;
                int mins = (int)Math.Round(Math.Min(600, engine.SampleSpan) / 60);
                if (gbps <= 1e-9)
                {
                    msg = "Idle - no traffic in the last " + mins + " min. You need " + UI.F2(dp.MBPerSecond) + " MB/s to finish today.";
                    state = 2;
                }
                else
                {
                    double eta = dp.RemainingGB / gbps;
                    if (eta <= secsLeft)
                    {
                        msg = "On track - at your current " + UI.Speed(paceBps) + " (" + mins + "-min average) you'll finish at " +
                              now.AddSeconds(eta).ToString("h:mm tt", CultureInfo.InvariantCulture) + ".";
                        state = 1;
                    }
                    else
                    {
                        double reach = dp.DoneGB + gbps * secsLeft;
                        msg = "Behind - at your current " + UI.Speed(paceBps) + " you'll reach " + UI.SizeText(reach) + " by midnight (" +
                              UI.SizeText(dp.TargetGB - reach) + " short). Speed up to " + UI.F2(dp.MBPerSecond) + " MB/s.";
                        state = 2;
                    }
                }
            }
            dayState = state;
            paceDay.Set(msg, state);
            progDay.Set(UI.F2(dp.DoneGB) + " of " + UI.Gen(dp.TargetGB) + " GB", UI.F1(dp.Fraction * 100) + "%", dp.Fraction, state == 2);

            string v, u;
            tSpeed.Set(dp.Done ? "Done" : UI.F2(dp.MBPerSecond), dp.Done ? "" : "MB/s", dp.Done ? "Day target reached" : "needed until midnight");
            tNow.Set(UI.F2(nowBps / 1048576.0), "MB/s", manual ? "live from your network adapters" : "10-min average " + UI.Speed(paceBps));
            tPerMin.Set(UI.F2(dp.GBPerMinute), "GB/min", UI.F2(dp.GBPerHour) + " GB per hour");
            UI.SizeParts(Math.Max(0, dp.RemainingGB), out v, out u);
            tRemain.Set(v, u, "of " + UI.Gen(dp.TargetGB) + " GB for today");
            tDone.Set(UI.F2(dp.DoneGB), "GB", manual ? "typed manually" : s.Source == "TrafficMonitor" ? "from TrafficMonitor + live" : "from built-in monitor");
            tTimeLeft.Set(dp.TimeLeftText, "", dp.MinutesLeft + " minutes until 12:00 AM");
        }

        void RefreshYear(YearPlan yp, double doneToday, double doneTB, bool auto, bool manual)
        {
            var p = Theme.P;
            chkAuto.Enabled = !manual;
            if (manual && chkAuto.Checked)
            {
                updating = true;
                chkAuto.Checked = false;
                updating = false;
            }
            else if (!manual && chkAuto.Checked != s.YearCompletedAuto)
            {
                updating = true;
                chkAuto.Checked = s.YearCompletedAuto;
                updating = false;
            }
            numCompleted.ReadOnly = auto;
            numCompleted.Increment = auto ? 0 : 1;
            Color nb = auto ? p.InputAuto : p.Input;
            if (numCompleted.BackColor != nb) numCompleted.BackColor = nb;
            if (auto && !numCompleted.Focused)
            {
                updating = true;
                numCompleted.Value = UI.Clamp(doneTB, 0, 100000);
                updating = false;
            }

            // forecast from the last 7 complete days
            double a7 = Last7DayAverage();
            string msg;
            int state;
            if (yp.Done)
            {
                msg = "Year target reached - " + UI.F2(doneTB) + " TB of " + UI.Gen(yp.TargetTB) + " TB.";
                state = 3;
            }
            else if (a7 < 0)
            {
                msg = "Need " + UI.F2(yp.PerDayGB) + " GB every day for the next " + yp.DaysLeft + " days. A forecast appears once there is traffic history.";
                state = 0;
            }
            else
            {
                double projTB = (doneTB * 1024 - doneToday + Math.Max(doneToday, a7) + a7 * (yp.DaysLeft - 1)) / 1024;
                if (projTB >= yp.TargetTB)
                {
                    msg = "On track - the last 7 days averaged " + UI.F2(a7) + " GB/day (need " + UI.F2(yp.PerDayGB) + "). At that pace: " + UI.F2(projTB) + " TB by 31 Dec.";
                    state = 1;
                }
                else
                {
                    msg = "Behind - the last 7 days averaged " + UI.F2(a7) + " GB/day but you need " + UI.F2(yp.PerDayGB) + ". At that pace: " + UI.F2(projTB) + " TB by 31 Dec.";
                    state = 2;
                }
            }
            paceYear.Set(msg, state);
            progYear.Set(UI.F2(doneTB) + " of " + UI.Gen(yp.TargetTB) + " TB", UI.F1(yp.Fraction * 100) + "%", yp.Fraction, state == 2);

            tPerDay.Set(yp.Done ? "Done" : UI.F2(yp.PerDayGB), yp.Done ? "" : "GB/day", yp.Done ? "Year target reached" : "needed every day");
            tPerWeek.Set(UI.F2(yp.PerWeekGB), "GB/week", "per day x 7");
            tDaysLeft.Set(yp.DaysLeft.ToString(CultureInfo.CurrentCulture), "days", "today to 31 Dec " + DateTime.Today.Year + " (incl. today)");
            tRequired.Set(UI.Gen(Math.Max(0, yp.RequiredTB)), "TB", "= " + UI.Gen(Math.Max(0, yp.RequiredGB)) + " GB still to go");
            tCompleted.Set(UI.F2(doneTB), "TB", (auto ? "auto" : "typed") + " - " + UI.F1(yp.Fraction * 100) + "% of " + UI.Gen(yp.TargetTB) + " TB");
            tToday.Set(UI.F2(doneToday), "GB", planShareGB > 0
                ? "of " + UI.F2(planShareGB) + " GB today's share (" + UI.F1(doneToday / planShareGB * 100) + "%)"
                : "year target reached");
        }

        // Average counted GB per day over the 7 days before today; -1 when there is no history.
        double Last7DayAverage()
        {
            if (s.Source == "Manual") return -1;
            DateTime today = DateTime.Today;
            if (avgVersion == engine.HistoryVersion && avgDate == today) return avg7;
            var snap = engine.Snapshot();
            double sum = 0;
            int found = 0;
            for (int i = 1; i <= 7; i++)
            {
                DayTotals t;
                if (snap.TryGetValue(today.AddDays(-i), out t))
                {
                    sum += engine.Pick(t);
                    found++;
                }
            }
            avg7 = found == 0 ? -1 : sum / 7 / GB;
            avgVersion = engine.HistoryVersion;
            avgDate = today;
            return avg7;
        }

        void RefreshHistory(bool force)
        {
            if (view != 2) return;
            if (!force && (DateTime.Now - chartBuiltAt).TotalSeconds < 5) return;
            chartBuiltAt = DateTime.Now;
            foreach (var c in chips) c.Selected = (int)c.Value == s.HistoryDays;

            var pts = new List<DayPoint>();
            if (s.Source == "Manual")
            {
                chart.EmptyText = "History needs a traffic source.\nChoose TrafficMonitor or the built-in monitor in Settings.";
            }
            else
            {
                chart.EmptyText = "No traffic history yet";
                var snap = engine.Snapshot();
                DateTime today = DateTime.Today;
                DateTime start = s.HistoryDays == 0 ? new DateTime(today.Year, 1, 1) : today.AddDays(-(s.HistoryDays - 1));
                for (DateTime d = start; d <= today; d = d.AddDays(1))
                {
                    var pt = new DayPoint();
                    pt.Date = d;
                    DayTotals t;
                    if (snap.TryGetValue(d, out t))
                    {
                        pt.DownGB = s.CountMode == "Upload" ? 0 : t.Down / GB;
                        pt.UpGB = s.CountMode == "Download" ? 0 : t.Up / GB;
                    }
                    pts.Add(pt);
                }
            }
            chart.SetData(pts, dayTargetGB, planShareGB);

            string v, u;
            if (pts.Count == 0)
            {
                hTotal.Set("-", "", "");
                hAvg.Set("-", "", "");
                hBest.Set("-", "", "");
                hHit.Set("-", "", "");
            }
            else
            {
                double total = pts.Sum(x => x.TotalGB);
                var best = pts.OrderByDescending(x => x.TotalGB).First();
                int hit = dayTargetGB > 0 ? pts.Count(x => x.TotalGB >= dayTargetGB) : 0;
                UI.SizeParts(total, out v, out u);
                hTotal.Set(v, u, "in " + pts.Count + " days");
                UI.SizeParts(total / pts.Count, out v, out u);
                hAvg.Set(v, u, "per calendar day");
                UI.SizeParts(best.TotalGB, out v, out u);
                hBest.Set(v, u, best.Date.ToString("ddd, d MMM", CultureInfo.InvariantCulture));
                hHit.Set(hit + " / " + pts.Count, "", "days at " + UI.Gen(dayTargetGB) + " GB or more");
            }
            UpdateDetail();
        }

        void UpdateDetail()
        {
            var pts = chart.Points;
            if (pts.Count == 0)
            {
                lblDetail.Text = "";
                return;
            }
            int i = chart.Hover >= 0 ? chart.Hover : chart.Selected >= 0 ? chart.Selected : pts.Count - 1;
            var d = pts[i];
            string text = d.Date.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture) + ":   total " + UI.SizeText(d.TotalGB) +
                          "   \u00B7   down " + UI.SizeText(d.DownGB) + "   \u00B7   up " + UI.SizeText(d.UpGB);
            if (dayTargetGB > 0) text += "   \u00B7   " + UI.F1(d.TotalGB / dayTargetGB * 100) + "% of the day target";
            if (chart.Hover < 0 && chart.Selected < 0) text += "   (hover or click a bar)";
            lblDetail.Text = text;
        }

        void ExportCsv()
        {
            if (s.Source == "Manual")
            {
                MessageBox.Show(this, "There is no history to export in manual mode.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var d = new SaveFileDialog())
            {
                d.Title = "Export traffic history";
                d.Filter = "CSV file (*.csv)|*.csv";
                d.FileName = "traffic-history-" + DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var ci = CultureInfo.InvariantCulture;
                    var snap = engine.Snapshot();
                    var sb = new StringBuilder();
                    sb.AppendLine("Date,Download (GB),Upload (GB),Total (GB),Counted (GB)");
                    foreach (var kv in snap.OrderBy(k => k.Key))
                    {
                        sb.AppendLine(kv.Key.ToString("yyyy-MM-dd", ci) + "," +
                            (kv.Value.Down / GB).ToString("0.000", ci) + "," +
                            (kv.Value.Up / GB).ToString("0.000", ci) + "," +
                            ((kv.Value.Down + (double)kv.Value.Up) / GB).ToString("0.000", ci) + "," +
                            (engine.Pick(kv.Value) / GB).ToString("0.000", ci));
                    }
                    File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show(this, snap.Count + " days exported to\n" + d.FileName, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not export: " + ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ---------- tray, notifications, mini view
        void UpdateTray(DayPlan dp)
        {
            int pct = (int)Math.Floor(dp.Fraction * 100);
            if (pct != trayPct || dp.Done != trayDone)
            {
                try
                {
                    var ic = TrayIcons.Render(dp.Fraction, dp.Done);
                    tray.Icon = ic;
                    if (trayIcon != null) trayIcon.Dispose();
                    trayIcon = ic;
                }
                catch { }
                trayPct = pct;
                trayDone = dp.Done;
            }
            string tip = dp.Done ? "Traffic Target - day target reached" : "Need " + UI.F2(dp.MBPerSecond) + " MB/s - " + pct + "% of today done";
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (tray.Text != tip) tray.Text = tip;
        }

        void Notify(DayPlan dp, double paceBps, DateTime now, bool manual)
        {
            if (shotDir != null) return;
            if (firstRefresh)
            {
                // never announce a target that was already reached when the app started
                firstRefresh = false;
                if (dp.Done) notifiedDoneFor = now.Date;
                return;
            }
            if (s.NotifyDone && dp.Done && dp.TargetGB > 0 && notifiedDoneFor != now.Date)
            {
                notifiedDoneFor = now.Date;
                tray.ShowBalloonTip(6000, "Day target reached",
                    UI.SizeText(dp.DoneGB) + " done today - your target was " + UI.Gen(dp.TargetGB) + " GB.", ToolTipIcon.Info);
            }
            if (manual || !s.NotifyBehind || dp.Done || engine.SampleSpan < 600 || now.Hour < 6) return;
            if ((now - lastBehindNotice).TotalMinutes < 60) return;
            double gbps = paceBps / GB;
            double reach = dp.DoneGB + gbps * (now.Date.AddDays(1) - now).TotalSeconds;
            if (reach < dp.TargetGB * 0.97)
            {
                lastBehindNotice = now;
                tray.ShowBalloonTip(8000, "Falling behind today's target",
                    "At " + UI.Speed(paceBps) + " you'll reach about " + UI.SizeText(reach) + " of " + UI.Gen(dp.TargetGB) +
                    " GB by midnight. Needed now: " + UI.F2(dp.MBPerSecond) + " MB/s.", ToolTipIcon.Warning);
            }
        }

        void ToggleMini()
        {
            if (mini != null && mini.Visible) mini.Close();
            else ShowMini();
        }

        void ShowMini()
        {
            if (mini == null || mini.IsDisposed)
            {
                mini = new MiniForm(this, s);
                mini.FormClosed += delegate
                {
                    mini = null;
                    if (!exiting)
                    {
                        s.MiniOpen = false;
                        s.Save();
                    }
                    UpdateHeaderButtons();
                };
            }
            mini.Show();
            s.MiniOpen = true;
            s.Save();
            UpdateHeaderButtons();
            RefreshAll();
        }

        void UpdateMini(DayPlan dp, double nowBps)
        {
            if (mini == null || !mini.Visible) return;
            mini.SetData(
                dp.Done ? "Done" : UI.F2(dp.MBPerSecond) + " MB/s",
                dp.Done ? UI.SizeText(dp.DoneGB) + " today" : UI.SizeText(Math.Max(0, dp.RemainingGB)) + " left  \u00B7  " + dp.TimeLeftText,
                "now " + UI.Speed(nowBps),
                dp.Fraction, dayState);
        }

        void TogglePin()
        {
            s.TopMost = !s.TopMost;
            TopMost = s.TopMost;
            s.Save();
            UpdateHeaderButtons();
        }

        void ShowAbout()
        {
            using (var f = new AboutForm())
            {
                f.TopMost = TopMost;
                f.ShowDialog(this);
            }
        }

        void OpenSettings()
        {
            string oldSource = s.Source, oldAdapters = s.Adapters, oldFile = s.TrafficMonitorFile, oldTheme = s.Theme;
            using (var f = new SettingsForm(s, engine))
            {
                f.TopMost = TopMost;
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            s.Save();
            AppSettings.ApplyStartup(s.StartWithWindows);
            if (oldSource == "Builtin" && s.Source != "Builtin") engine.Save();
            if (oldSource != s.Source || oldAdapters != s.Adapters || oldFile != s.TrafficMonitorFile) engine.Reset();
            if (Theme.Set(s.Theme) || oldTheme != s.Theme) ApplyTheme();
            engine.Tick();
            RefreshAll();
            RefreshHistory(true);
        }

        public void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void ExitApp()
        {
            exiting = true;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.D1: ShowView(0); return true;
                case Keys.Control | Keys.D2: ShowView(1); return true;
                case Keys.Control | Keys.D3: ShowView(2); return true;
                case Keys.Control | Keys.Oemcomma: OpenSettings(); return true;
                case Keys.Control | Keys.M: ToggleMini(); return true;
                case Keys.Control | Keys.T: TogglePin(); return true;
                case Keys.Control | Keys.E: ExportCsv(); return true;
                case Keys.F1: ShowAbout(); return true;
                case Keys.F5:
                    engine.Reset();
                    engine.Tick();
                    RefreshAll();
                    RefreshHistory(true);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_SHOWME) ShowFromTray();
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && s.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!s.TrayHintShown)
                {
                    tray.ShowBalloonTip(5000, "Traffic Target is still running",
                        "It keeps tracking in the tray. Right-click the tray icon and choose Exit to close it.", ToolTipIcon.Info);
                    s.TrayHintShown = true;
                    s.Save();
                }
                return;
            }
            exiting = true;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            engine.Save();
            if (mini != null) mini.Close();
            s.Save();
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.UserPreferenceChanged -= OnUserPreference;
            tray.Visible = false;
            tray.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
            base.OnFormClosed(e);
        }

        void OnPower(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) engine.Save();
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            engine.Save();
            s.Save();
        }
    }
}
