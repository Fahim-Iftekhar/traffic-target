using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TrafficTarget
{
    // Base for themed dialogs: auto-sized vertical layout and dark title bar.
    class ThemedDialog : Form
    {
        protected readonly FlowLayoutPanel Root;

        public ThemedDialog(string title)
        {
            Text = title;
            AutoScaleMode = AutoScaleMode.None;
            Font = UI.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Root = new FlowLayoutPanel();
            Root.FlowDirection = FlowDirection.TopDown;
            Root.WrapContents = false;
            Root.AutoSize = true;
            Root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Root.Padding = new Padding(UI.Px(20), UI.Px(14), UI.Px(20), UI.Px(16));
            Root.Location = new Point(0, 0);
            Controls.Add(Root);
        }

        protected override void OnLoad(EventArgs e)
        {
            Theme.Apply(this);
            base.OnLoad(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkTitleBar(Handle, Theme.P.Dark);
        }

        protected Label Title(string text)
        {
            var l = UI.MakeLabel(text, UI.Section, false);
            l.Margin = new Padding(0, UI.Px(10), 0, UI.Px(6));
            return l;
        }

        protected Label Note(string text)
        {
            var l = UI.MakeLabel(text, UI.Small, true);
            l.Margin = new Padding(UI.Px(22), 0, 0, UI.Px(6));
            l.MaximumSize = new Size(UI.Px(560), 0);
            return l;
        }

        protected RadioButton Radio(string text, bool on)
        {
            var r = new RadioButton();
            r.Text = text;
            r.AutoSize = true;
            r.Checked = on;
            r.Margin = new Padding(0, UI.Px(4), 0, UI.Px(2));
            return r;
        }

        protected CheckBox Check(string text, bool on)
        {
            var c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Checked = on;
            c.Margin = new Padding(0, UI.Px(3), 0, UI.Px(3));
            return c;
        }

        protected FlowLayoutPanel SubRow(int indent)
        {
            var r = new FlowLayoutPanel();
            r.FlowDirection = FlowDirection.LeftToRight;
            r.WrapContents = false;
            r.AutoSize = true;
            r.Margin = new Padding(UI.Px(indent), 0, 0, UI.Px(6));
            return r;
        }

        protected Button SmallButton(string text)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Margin = new Padding(UI.Px(6), 0, 0, 0);
            b.Cursor = Cursors.Hand;
            return b;
        }

        protected Button DialogButton(string text)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.MinimumSize = new Size(UI.Px(92), UI.Px(32));
            b.Margin = new Padding(UI.Px(8), 0, 0, 0);
            b.Cursor = Cursors.Hand;
            return b;
        }

        protected ComboBox Combo(int width, string[] items, int selected)
        {
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.Width = UI.Px(width);
            c.Items.AddRange(items);
            c.SelectedIndex = selected;
            return c;
        }
    }

    class SettingsForm : ThemedDialog
    {
        readonly AppSettings s;
        readonly TrafficEngine engine;
        RadioButton rbTm, rbOwn, rbManual;
        TextBox txtTm;
        ComboBox cbCount, cbAdapters, cbTheme;
        CheckBox chkStartup, chkTray, chkNotifyDone, chkNotifyBehind;

        public SettingsForm(AppSettings settings, TrafficEngine eng) : base("Traffic Target - Settings")
        {
            s = settings;
            engine = eng;

            // --- source
            Root.Controls.Add(Title("Where \"Completed\" comes from"));
            rbTm = Radio("TrafficMonitor  (reads its daily history file, plus live counting between its saves)", s.Source == "TrafficMonitor");
            Root.Controls.Add(rbTm);
            var tmRow = SubRow(22);
            txtTm = new TextBox();
            txtTm.Width = UI.Px(400);
            txtTm.BorderStyle = BorderStyle.FixedSingle;
            txtTm.Text = AppSettings.ReadOnly ? @"%AppData%\TrafficMonitor\history_traffic.dat" : s.TmFilePath;
            tmRow.Controls.Add(txtTm);
            var browse = SmallButton("Browse...");
            browse.Click += delegate
            {
                using (var d = new OpenFileDialog())
                {
                    d.Title = "Select TrafficMonitor's history_traffic.dat";
                    d.Filter = "TrafficMonitor history (history_traffic.dat)|history_traffic.dat|All files (*.*)|*.*";
                    try { d.InitialDirectory = Path.GetDirectoryName(txtTm.Text); } catch { }
                    if (d.ShowDialog(this) == DialogResult.OK) txtTm.Text = d.FileName;
                }
            };
            tmRow.Controls.Add(browse);
            var def = SmallButton("Default");
            def.Click += delegate { txtTm.Text = AppSettings.DefaultTmFile(); };
            tmRow.Controls.Add(def);
            Root.Controls.Add(tmRow);

            rbOwn = Radio("Built-in traffic monitor  (counts by itself while the app runs - turn on \"Start with Windows\")", s.Source == "Builtin");
            Root.Controls.Add(rbOwn);
            var ownRow = SubRow(22);
            var al = UI.MakeLabel("Adapters:", UI.Body, false);
            al.Margin = new Padding(0, UI.Px(5), UI.Px(6), 0);
            ownRow.Controls.Add(al);
            cbAdapters = Combo(340, new string[] { "Internet-connected adapters only (recommended)", "All active adapters (incl. virtual ones)" }, s.Adapters == "All" ? 1 : 0);
            ownRow.Controls.Add(cbAdapters);
            var import = SmallButton("Import TrafficMonitor history");
            import.Click += delegate
            {
                string old = s.TrafficMonitorFile;
                try
                {
                    s.TrafficMonitorFile = NormalizedTmPath();
                    int n = engine.ImportFromTrafficMonitor();
                    MessageBox.Show(this, n + " day(s) imported into the built-in monitor's history.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not import: " + ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                s.TrafficMonitorFile = old;
            };
            ownRow.Controls.Add(import);
            Root.Controls.Add(ownRow);

            rbManual = Radio("Manual  (I type the completed GB / TB myself)", s.Source == "Manual");
            Root.Controls.Add(rbManual);

            Root.Controls.Add(Title("What counts as traffic"));
            var countRow = SubRow(0);
            cbCount = Combo(360, new string[] { "Total (upload + download) - same as TrafficMonitor", "Download only", "Upload only" },
                s.CountMode == "Download" ? 1 : s.CountMode == "Upload" ? 2 : 0);
            countRow.Controls.Add(cbCount);
            Root.Controls.Add(countRow);

            // --- appearance & notifications
            Root.Controls.Add(Title("Appearance"));
            var themeRow = SubRow(0);
            var tl = UI.MakeLabel("Theme:", UI.Body, false);
            tl.Margin = new Padding(0, UI.Px(5), UI.Px(6), 0);
            themeRow.Controls.Add(tl);
            cbTheme = Combo(200, new string[] { "Follow Windows", "Light", "Dark" }, s.Theme == "Light" ? 1 : s.Theme == "Dark" ? 2 : 0);
            themeRow.Controls.Add(cbTheme);
            Root.Controls.Add(themeRow);

            Root.Controls.Add(Title("Notifications"));
            chkNotifyDone = Check("Tell me when today's target is reached", s.NotifyDone);
            chkNotifyBehind = Check("Warn me (at most hourly) when my current speed will not reach today's target", s.NotifyBehind);
            Root.Controls.Add(chkNotifyDone);
            Root.Controls.Add(chkNotifyBehind);

            Root.Controls.Add(Title("Window"));
            chkStartup = Check("Start with Windows (opens quietly in the tray)", s.StartWithWindows);
            chkTray = Check("Closing the window keeps the app running in the tray", s.CloseToTray);
            Root.Controls.Add(chkStartup);
            Root.Controls.Add(chkTray);

            var info = UI.MakeLabel("Settings and history are stored in %AppData%\\TrafficTarget", UI.Small, true);
            info.Margin = new Padding(0, UI.Px(12), 0, 0);
            Root.Controls.Add(info);

            var buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Margin = new Padding(0, UI.Px(14), 0, 0);
            buttons.Anchor = AnchorStyles.Right;
            var cancel = DialogButton("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            var ok = DialogButton("Save");
            ok.Click += delegate { Commit(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            Root.Controls.Add(buttons);

            AcceptButton = ok;
            CancelButton = cancel;
            ActiveControl = ok;
        }

        string NormalizedTmPath()
        {
            string p = Environment.ExpandEnvironmentVariables(txtTm.Text.Trim().Trim('"'));
            if (p.Length == 0 || string.Equals(p, AppSettings.DefaultTmFile(), StringComparison.OrdinalIgnoreCase)) return "";
            return p;
        }

        void Commit()
        {
            s.Source = rbTm.Checked ? "TrafficMonitor" : rbOwn.Checked ? "Builtin" : "Manual";
            s.TrafficMonitorFile = NormalizedTmPath();
            s.Adapters = cbAdapters.SelectedIndex == 1 ? "All" : "Internet";
            s.CountMode = cbCount.SelectedIndex == 1 ? "Download" : cbCount.SelectedIndex == 2 ? "Upload" : "Total";
            s.Theme = cbTheme.SelectedIndex == 1 ? "Light" : cbTheme.SelectedIndex == 2 ? "Dark" : "System";
            s.NotifyDone = chkNotifyDone.Checked;
            s.NotifyBehind = chkNotifyBehind.Checked;
            s.StartWithWindows = chkStartup.Checked;
            s.CloseToTray = chkTray.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    class AboutForm : ThemedDialog
    {
        public AboutForm() : base("About Traffic Target")
        {
            var name = UI.MakeLabel(AppInfo.Name, UI.Clock, false);
            name.Margin = new Padding(0, UI.Px(4), 0, 0);
            Root.Controls.Add(name);
            var ver = UI.MakeLabel("Version " + AppInfo.Version, UI.Body, true);
            ver.Margin = new Padding(UI.Px(2), 0, 0, UI.Px(12));
            Root.Controls.Add(ver);

            var desc = UI.MakeLabel("Tracks a daily GB target and a yearly TB target against your real network traffic. " +
                "It tells you the speed you need until midnight, forecasts whether you'll make it, and keeps a daily history.", UI.Body, false);
            desc.MaximumSize = new Size(UI.Px(420), 0);
            desc.Margin = new Padding(0, 0, 0, UI.Px(12));
            Root.Controls.Add(desc);

            var kt = UI.MakeLabel("Keyboard shortcuts", UI.BodyBold, false);
            kt.Margin = new Padding(0, 0, 0, UI.Px(4));
            Root.Controls.Add(kt);
            var keys = new TableLayoutPanel();
            keys.ColumnCount = 2;
            keys.AutoSize = true;
            keys.Margin = new Padding(0, 0, 0, UI.Px(12));
            string[,] rows = {
                { "Ctrl+1 / 2 / 3", "Today / Year / History" },
                { "Ctrl+M", "Mini view" },
                { "Ctrl+T", "Keep on top" },
                { "Ctrl+E", "Export history (CSV)" },
                { "Ctrl+,", "Settings" },
                { "F5", "Reload traffic data" },
                { "F1", "About" } };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                var k = UI.MakeLabel(rows[i, 0], UI.SmallBold, false);
                k.Margin = new Padding(0, UI.Px(1), UI.Px(18), UI.Px(1));
                var d = UI.MakeLabel(rows[i, 1], UI.Small, true);
                d.Margin = new Padding(0, UI.Px(1), 0, UI.Px(1));
                keys.Controls.Add(k, 0, i);
                keys.Controls.Add(d, 1, i);
            }
            Root.Controls.Add(keys);

            var by = UI.MakeLabel("Made by " + AppInfo.Author + "  -  MIT License", UI.Body, false);
            Root.Controls.Add(by);

            var link = new LinkLabel();
            link.Text = AppInfo.RepoUrl;
            link.AutoSize = true;
            link.Margin = new Padding(0, UI.Px(4), 0, 0);
            link.LinkClicked += delegate { Open(AppInfo.RepoUrl); };
            Root.Controls.Add(link);

            var data = new LinkLabel();
            data.Text = "Open data folder";
            data.AutoSize = true;
            data.Margin = new Padding(0, UI.Px(4), 0, 0);
            data.LinkClicked += delegate
            {
                try { Directory.CreateDirectory(AppSettings.Dir); } catch { }
                Open(AppSettings.Dir);
            };
            Root.Controls.Add(data);

            var close = DialogButton("Close");
            close.DialogResult = DialogResult.OK;
            close.Anchor = AnchorStyles.Right;
            close.Margin = new Padding(0, UI.Px(16), 0, 0);
            Root.Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }

        static void Open(string target)
        {
            try { Process.Start(target); } catch { }
        }
    }
}
