using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TrafficTarget
{
    // User settings, stored as key=value lines in %AppData%\TrafficTarget\settings.ini
    class AppSettings
    {
        public static bool ReadOnly;               // screenshot/demo mode: never write settings or registry

        // Targets
        public double DayRequiredGB = 1024;
        public bool FollowYearPlan = false;
        public double YearTargetTB = 120;
        public double YearCompletedManualTB = 102.5;
        public bool YearCompletedAuto = true;
        public double DayCompletedManualGB = 0;

        // Traffic source
        public string Source = "TrafficMonitor";   // TrafficMonitor | Builtin | Manual
        public string CountMode = "Total";         // Total | Download | Upload
        public string Adapters = "Internet";       // Internet | All   (built-in monitor only)
        public string TrafficMonitorFile = "";

        // Behaviour
        public string Theme = "System";            // System | Light | Dark
        public bool NotifyDone = true;
        public bool NotifyBehind = true;
        public bool StartWithWindows = false;
        public bool CloseToTray = true;
        public bool TopMost = false;
        public bool TrayHintShown = false;

        // Window state
        public int LastView = 0;                   // 0 Today, 1 Year, 2 History
        public int HistoryDays = 30;               // 14 | 30 | 90 | 0 (= this year)
        public bool MiniOpen = false;
        public int MiniX = -1, MiniY = -1;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrafficTarget"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static string DefaultTmFile()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrafficMonitor", "history_traffic.dat");
        }

        public string TmFilePath
        {
            get { return string.IsNullOrEmpty(TrafficMonitorFile) ? DefaultTmFile() : TrafficMonitorFile; }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            if (!File.Exists(FilePath))
            {
                s.Source = File.Exists(DefaultTmFile()) ? "TrafficMonitor" : "Builtin";
                return s;
            }
            try
            {
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    int i = raw.IndexOf('=');
                    if (i <= 0) continue;
                    string k = raw.Substring(0, i).Trim();
                    string v = raw.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "DayRequiredGB": s.DayRequiredGB = D(v, s.DayRequiredGB); break;
                        case "FollowYearPlan": s.FollowYearPlan = B(v, s.FollowYearPlan); break;
                        case "YearTargetTB": s.YearTargetTB = D(v, s.YearTargetTB); break;
                        case "YearCompletedManualTB": s.YearCompletedManualTB = D(v, s.YearCompletedManualTB); break;
                        case "YearCompletedAuto": s.YearCompletedAuto = B(v, s.YearCompletedAuto); break;
                        case "DayCompletedManualGB": s.DayCompletedManualGB = D(v, s.DayCompletedManualGB); break;
                        case "Source": if (v == "TrafficMonitor" || v == "Builtin" || v == "Manual") s.Source = v; break;
                        case "CountMode": if (v == "Total" || v == "Download" || v == "Upload") s.CountMode = v; break;
                        case "Adapters": if (v == "Internet" || v == "All") s.Adapters = v; break;
                        case "TrafficMonitorFile": s.TrafficMonitorFile = v; break;
                        case "Theme": if (v == "System" || v == "Light" || v == "Dark") s.Theme = v; break;
                        case "NotifyDone": s.NotifyDone = B(v, s.NotifyDone); break;
                        case "NotifyBehind": s.NotifyBehind = B(v, s.NotifyBehind); break;
                        case "StartWithWindows": s.StartWithWindows = B(v, s.StartWithWindows); break;
                        case "CloseToTray": s.CloseToTray = B(v, s.CloseToTray); break;
                        case "TopMost": s.TopMost = B(v, s.TopMost); break;
                        case "TrayHintShown": s.TrayHintShown = B(v, s.TrayHintShown); break;
                        case "LastView": s.LastView = Math.Max(0, Math.Min(2, (int)D(v, 0))); break;
                        case "LastTab": s.LastView = Math.Max(0, Math.Min(2, (int)D(v, 0))); break;   // v1 name
                        case "HistoryDays": s.HistoryDays = (int)D(v, 30); break;
                        case "MiniOpen": s.MiniOpen = B(v, s.MiniOpen); break;
                        case "MiniX": s.MiniX = (int)D(v, -1); break;
                        case "MiniY": s.MiniY = (int)D(v, -1); break;
                    }
                }
            }
            catch { }
            if (s.HistoryDays != 14 && s.HistoryDays != 30 && s.HistoryDays != 90 && s.HistoryDays != 0) s.HistoryDays = 30;
            return s;
        }

        static double D(string v, double def)
        {
            double r;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        static bool B(string v, bool def)
        {
            bool r;
            return bool.TryParse(v, out r) ? r : def;
        }

        public void Save()
        {
            if (ReadOnly) return;
            try
            {
                Directory.CreateDirectory(Dir);
                var ci = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine("DayRequiredGB=" + DayRequiredGB.ToString("R", ci));
                sb.AppendLine("FollowYearPlan=" + FollowYearPlan);
                sb.AppendLine("YearTargetTB=" + YearTargetTB.ToString("R", ci));
                sb.AppendLine("YearCompletedManualTB=" + YearCompletedManualTB.ToString("R", ci));
                sb.AppendLine("YearCompletedAuto=" + YearCompletedAuto);
                sb.AppendLine("DayCompletedManualGB=" + DayCompletedManualGB.ToString("R", ci));
                sb.AppendLine("Source=" + Source);
                sb.AppendLine("CountMode=" + CountMode);
                sb.AppendLine("Adapters=" + Adapters);
                sb.AppendLine("TrafficMonitorFile=" + TrafficMonitorFile);
                sb.AppendLine("Theme=" + Theme);
                sb.AppendLine("NotifyDone=" + NotifyDone);
                sb.AppendLine("NotifyBehind=" + NotifyBehind);
                sb.AppendLine("StartWithWindows=" + StartWithWindows);
                sb.AppendLine("CloseToTray=" + CloseToTray);
                sb.AppendLine("TopMost=" + TopMost);
                sb.AppendLine("TrayHintShown=" + TrayHintShown);
                sb.AppendLine("LastView=" + LastView);
                sb.AppendLine("HistoryDays=" + HistoryDays);
                sb.AppendLine("MiniOpen=" + MiniOpen);
                sb.AppendLine("MiniX=" + MiniX);
                sb.AppendLine("MiniY=" + MiniY);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }

        public static void ApplyStartup(bool on)
        {
            if (ReadOnly) return;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("TrafficTarget", "\"" + Application.ExecutablePath + "\" /tray");
                    else k.DeleteValue("TrafficTarget", false);
                }
            }
            catch { }
        }
    }
}
