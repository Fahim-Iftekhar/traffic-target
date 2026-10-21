using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace TrafficTarget
{
    class DayTotals
    {
        public long Up;
        public long Down;
        public DayTotals Copy() { var d = new DayTotals(); d.Up = Up; d.Down = Down; return d; }
    }

    // Reads and writes daily traffic history files.
    static class History
    {
        static readonly Regex LineRx = new Regex(@"^\s*(\d{4})/(\d{1,2})/(\d{1,2})\s+(\d+)(?:\s*/\s*(\d+))?", RegexOptions.Compiled);

        // TrafficMonitor format: "yyyy/MM/dd up_KB/down_KB" (older versions: "yyyy/MM/dd total_KB").
        // unitBytes = 1024 for TrafficMonitor, 1 for this app's own file.
        public static Dictionary<DateTime, DayTotals> Read(string path, long unitBytes)
        {
            var map = new Dictionary<DateTime, DayTotals>();
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();
            foreach (var line in text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = LineRx.Match(line);
                if (!m.Success) continue;
                int y, mo, d;
                if (!int.TryParse(m.Groups[1].Value, out y) || !int.TryParse(m.Groups[2].Value, out mo) || !int.TryParse(m.Groups[3].Value, out d)) continue;
                if (y < 1900 || y > 9999 || mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) continue;
                var date = new DateTime(y, mo, d);
                long a, b;
                if (!long.TryParse(m.Groups[4].Value, out a)) continue;
                var t = new DayTotals();
                if (m.Groups[5].Success && long.TryParse(m.Groups[5].Value, out b))
                {
                    t.Up = a * unitBytes;
                    t.Down = b * unitBytes;
                }
                else
                {
                    t.Up = 0;
                    t.Down = a * unitBytes;
                }
                if (!map.ContainsKey(date)) map[date] = t;
            }
            return map;
        }

        public static void Write(string path, Dictionary<DateTime, DayTotals> map)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Traffic Target history. Format: yyyy/MM/dd upload_bytes/download_bytes");
            foreach (var kv in map.OrderByDescending(k => k.Key))
                sb.AppendLine(kv.Key.ToString("yyyy'/'MM'/'dd", ci) + " " + kv.Value.Up.ToString(ci) + "/" + kv.Value.Down.ToString(ci));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            if (File.Exists(path))
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, path);
        }
    }

    // Counts bytes on the PC's network adapters (like TrafficMonitor does).
    class NetCounter
    {
        class Prev { public long Rx; public long Tx; }

        readonly Dictionary<string, Prev> last = new Dictionary<string, Prev>();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double lastT = 0;

        public long Up;     // cumulative since app start
        public long Down;
        public bool InternetOnly;
        public int ActiveAdapters;

        public double Seconds { get { return clock.Elapsed.TotalSeconds; } }

        public void Sample()
        {
            double t = clock.Elapsed.TotalSeconds;
            double dt = Math.Max(0.25, t - lastT);
            lastT = t;
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return; }
            var seen = new HashSet<string>();
            int active = 0;
            foreach (var n in nics)
            {
                try
                {
                    var type = n.NetworkInterfaceType;
                    if (type == NetworkInterfaceType.Loopback || type == NetworkInterfaceType.Tunnel) continue;
                    if (n.OperationalStatus != OperationalStatus.Up) continue;
                    if (InternetOnly && !HasGateway(n)) continue;
                    var st = n.GetIPStatistics();
                    long rx = st.BytesReceived;
                    long tx = st.BytesSent;
                    string id = n.Id;
                    seen.Add(id);
                    active++;
                    Prev p;
                    if (last.TryGetValue(id, out p))
                    {
                        double bps = n.Speed > 0 ? n.Speed : 10e9;
                        double max = bps / 8.0 * dt * 1.5 + 1048576.0;
                        Down += Delta(p.Rx, rx, max);
                        Up += Delta(p.Tx, tx, max);
                        p.Rx = rx;
                        p.Tx = tx;
                    }
                    else
                    {
                        p = new Prev();
                        p.Rx = rx;
                        p.Tx = tx;
                        last[id] = p;
                    }
                }
                catch { }
            }
            foreach (var k in last.Keys.Where(k => !seen.Contains(k)).ToList()) last.Remove(k);
            ActiveAdapters = active;
        }

        static long Delta(long prev, long cur, double maxBytes)
        {
            if (cur >= prev) return cur - prev;
            // 32-bit counter wrap-around (older Windows APIs)
            if (prev <= 0xFFFFFFFFL && cur <= 0xFFFFFFFFL)
            {
                long d = cur + 0x100000000L - prev;
                if (d <= maxBytes) return d;
            }
            return 0; // counter was reset
        }

        static bool HasGateway(NetworkInterface n)
        {
            try
            {
                foreach (var g in n.GetIPProperties().GatewayAddresses)
                {
                    var a = g.Address;
                    if (a == null) continue;
                    if (a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any)) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    // Supplies "completed today", "completed this year", history and live speed from the chosen source.
    class TrafficEngine
    {
        struct Sample { public double T; public long Up; public long Down; }

        readonly AppSettings s;
        readonly NetCounter counter = new NetCounter();
        readonly List<Sample> samples = new List<Sample>();

        // TrafficMonitor mode
        Dictionary<DateTime, DayTotals> tm = new Dictionary<DateTime, DayTotals>();
        DateTime tmMtime = DateTime.MinValue;
        string tmPathLoaded = "";
        DayTotals tmToday = new DayTotals();
        long baseUp, baseDown;
        DateTime baseDate = DateTime.MinValue;
        bool tmFound;

        // Built-in mode
        Dictionary<DateTime, DayTotals> own;
        bool ownDirty;
        DateTime lastOwnSave = DateTime.Now;
        long ownLastUp, ownLastDown;

        public DayTotals Today = new DayTotals();
        public DayTotals YearBeforeToday = new DayTotals();
        public string Status = "";
        public int HistoryVersion;   // changes whenever stored history (not today's live value) changes
        public bool Demo;            // synthetic data for screenshots (never touches real files)
        Dictionary<DateTime, DayTotals> demo;
        DayTotals demoTodayBase;
        long demoUp, demoDown;

        public TrafficEngine(AppSettings settings)
        {
            s = settings;
        }

        static string OwnFile { get { return Path.Combine(AppSettings.Dir, "traffic_history.dat"); } }

        public void Reset()
        {
            tmMtime = DateTime.MinValue;
            tmPathLoaded = "";
            baseDate = DateTime.MinValue;
            ownLastUp = counter.Up;
            ownLastDown = counter.Down;
            HistoryVersion++;
        }

        public double Pick(DayTotals t) { return Pick(t.Up, t.Down); }

        public double Pick(double up, double down)
        {
            if (s.CountMode == "Download") return down;
            if (s.CountMode == "Upload") return up;
            return up + down;
        }

        public void Tick()
        {
            if (Demo)
            {
                DemoTick();
                return;
            }
            counter.InternetOnly = s.Source == "Builtin" && s.Adapters == "Internet";
            counter.Sample();
            RecordSample();
            DateTime today = DateTime.Today;
            if (s.Source == "TrafficMonitor") TickTm(today);
            else if (s.Source == "Builtin") TickOwn(today);
            else Status = "Manual entry - type today's completed GB (and this year's TB).";
        }

        // ---------- live speed
        void RecordSample()
        {
            var x = new Sample();
            x.T = counter.Seconds;
            x.Up = counter.Up;
            x.Down = counter.Down;
            samples.Add(x);
            double cutoff = x.T - 15 * 60;
            int drop = 0;
            while (drop < samples.Count && samples[drop].T < cutoff) drop++;
            if (drop > 0) samples.RemoveRange(0, drop);
        }

        // Seconds of speed history available (max 15 minutes).
        public double SampleSpan
        {
            get { return samples.Count < 2 ? 0 : samples[samples.Count - 1].T - samples[0].T; }
        }

        // Average bytes/second over the last windowSec seconds: kind 0 = counted (per Count mode), 1 = download, 2 = upload.
        public double Rate(double windowSec, int kind)
        {
            if (samples.Count < 2) return 0;
            var last = samples[samples.Count - 1];
            var first = samples[0];
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                if (samples[i].T < last.T - windowSec) break;
                first = samples[i];
            }
            double dt = last.T - first.T;
            if (dt < 0.5) return 0;
            double up = last.Up - first.Up, down = last.Down - first.Down;
            if (kind == 1) return down / dt;
            if (kind == 2) return up / dt;
            return Pick(up, down) / dt;
        }

        // ---------- history snapshot (copy, with today's live value)
        public Dictionary<DateTime, DayTotals> Snapshot()
        {
            var copy = new Dictionary<DateTime, DayTotals>();
            Dictionary<DateTime, DayTotals> src = null;
            if (Demo) src = demo;
            else if (s.Source == "TrafficMonitor") src = tm;
            else if (s.Source == "Builtin") src = own;
            if (src != null) foreach (var kv in src) copy[kv.Key] = kv.Value.Copy();
            if (s.Source != "Manual") copy[DateTime.Today] = Today.Copy();
            return copy;
        }

        // ---------- TrafficMonitor source
        void TickTm(DateTime today)
        {
            string path = s.TmFilePath;
            DateTime mt = DateTime.MinValue;
            try { if (File.Exists(path)) mt = File.GetLastWriteTime(path); }
            catch { }

            bool pathChanged = !string.Equals(path, tmPathLoaded, StringComparison.OrdinalIgnoreCase);
            if (mt == DateTime.MinValue)
            {
                if (tmFound || pathChanged || baseDate != today)
                {
                    tm = new Dictionary<DateTime, DayTotals>();
                    tmToday = new DayTotals();
                    YearBeforeToday = new DayTotals();
                    baseUp = counter.Up;
                    baseDown = counter.Down;
                    baseDate = today;
                    tmPathLoaded = path;
                    tmFound = false;
                    HistoryVersion++;
                }
            }
            else if (mt != tmMtime || pathChanged || baseDate != today)
            {
                try
                {
                    tm = History.Read(path, 1024);
                    tmMtime = mt;
                    tmPathLoaded = path;
                    tmFound = true;
                    DayTotals t;
                    tmToday = tm.TryGetValue(today, out t) ? t.Copy() : new DayTotals();
                    YearBeforeToday = SumYear(tm, today);
                    baseUp = counter.Up;
                    baseDown = counter.Down;
                    baseDate = today;
                    HistoryVersion++;
                }
                catch
                {
                    // file is being written by TrafficMonitor: try again next second
                }
            }

            Today.Up = tmToday.Up + (counter.Up - baseUp);
            Today.Down = tmToday.Down + (counter.Down - baseDown);
            if (!tmFound)
                Status = "TrafficMonitor file not found - counting live since start. See Settings.";
            else
                Status = "TrafficMonitor - saved " + Ago(DateTime.Now - tmMtime) + " - live on " + counter.ActiveAdapters + " adapter(s)";
        }

        // ---------- built-in monitor source
        void TickOwn(DateTime today)
        {
            if (own == null) LoadOwn();
            long dUp = counter.Up - ownLastUp;
            long dDown = counter.Down - ownLastDown;
            ownLastUp = counter.Up;
            ownLastDown = counter.Down;
            DayTotals t;
            if (!own.TryGetValue(today, out t))
            {
                t = new DayTotals();
                own[today] = t;
                ownDirty = true;
                HistoryVersion++;
            }
            if (dUp > 0 || dDown > 0)
            {
                t.Up += Math.Max(0, dUp);
                t.Down += Math.Max(0, dDown);
                ownDirty = true;
            }
            if (ownDirty && (DateTime.Now - lastOwnSave).TotalSeconds >= 30) Save();
            Today = t.Copy();
            YearBeforeToday = SumYear(own, today);
            Status = "Built-in monitor - live on " + counter.ActiveAdapters + " adapter(s)" +
                     (s.Adapters == "Internet" ? " (internet-connected only)" : " (all active)");
        }

        void LoadOwn()
        {
            own = new Dictionary<DateTime, DayTotals>();
            try { if (File.Exists(OwnFile)) own = History.Read(OwnFile, 1); }
            catch { }
            ownLastUp = counter.Up;
            ownLastDown = counter.Down;
            HistoryVersion++;
        }

        public void Save()
        {
            if (own == null || !ownDirty) return;
            try
            {
                History.Write(OwnFile, own);
                ownDirty = false;
            }
            catch { }
            lastOwnSave = DateTime.Now;
        }

        // Copies TrafficMonitor's history into the built-in monitor's history.
        public int ImportFromTrafficMonitor()
        {
            if (own == null) LoadOwn();
            var src = History.Read(s.TmFilePath, 1024);
            int n = 0;
            foreach (var kv in src)
            {
                DayTotals mine;
                if (!own.TryGetValue(kv.Key, out mine))
                {
                    own[kv.Key] = kv.Value.Copy();
                    n++;
                }
                else if ((double)kv.Value.Up + kv.Value.Down > (double)mine.Up + mine.Down)
                {
                    mine.Up = kv.Value.Up;
                    mine.Down = kv.Value.Down;
                    n++;
                }
            }
            ownDirty = true;
            Save();
            HistoryVersion++;
            return n;
        }

        // ---------- demo data (deterministic, for screenshots)
        void DemoTick()
        {
            const double MB = 1048576.0;
            DateTime today = DateTime.Today;
            if (demo == null)
            {
                demo = new Dictionary<DateTime, DayTotals>();
                var rnd = new Random(7);
                for (DateTime d = new DateTime(today.Year, 1, 1); d < today; d = d.AddDays(1))
                {
                    double recent = Math.Max(0, 1 - (today - d).TotalDays / 60.0);
                    double gb = 170 + 120 * Math.Sin(d.DayOfYear / 9.0) + 620 * recent + rnd.NextDouble() * 200;
                    if (d.DayOfWeek == DayOfWeek.Friday || d.DayOfWeek == DayOfWeek.Saturday) gb *= 1.25;
                    if (rnd.NextDouble() < 0.05) gb *= 0.1;
                    var t = new DayTotals();
                    t.Down = (long)(gb * 0.86 * 1073741824.0);
                    t.Up = (long)(gb * 0.14 * 1073741824.0);
                    demo[d] = t;
                }
                double sofar = DateTime.Now.TimeOfDay.TotalHours / 24.0 * 1050 + 40;
                demoTodayBase = new DayTotals();
                demoTodayBase.Down = (long)(sofar * 0.86 * 1073741824.0);
                demoTodayBase.Up = (long)(sofar * 0.14 * 1073741824.0);
                // 15 minutes of speed history at ~13 MB/s
                for (int k = 0; k <= 900; k += 5)
                {
                    var x = new Sample();
                    x.T = k - 900;
                    x.Down = (long)((k - 900) * 11.6 * MB);
                    x.Up = (long)((k - 900) * 1.7 * MB);
                    samples.Add(x);
                }
                HistoryVersion++;
            }
            double now = counter.Seconds;
            double wobble = 1 + 0.08 * Math.Sin(now / 7.0);
            demoDown = (long)(now * 11.6 * MB * wobble);
            demoUp = (long)(now * 1.7 * MB * wobble);
            var y = new Sample();
            y.T = now;
            y.Down = demoDown;
            y.Up = demoUp;
            samples.Add(y);
            Today = new DayTotals();
            Today.Down = demoTodayBase.Down + demoDown;
            Today.Up = demoTodayBase.Up + demoUp;
            demo[today] = Today.Copy();
            YearBeforeToday = SumYear(demo, today);
            Status = "Demo data - built-in monitor - live on 1 adapter(s) (internet-connected only)";
        }

        static DayTotals SumYear(Dictionary<DateTime, DayTotals> map, DateTime today)
        {
            var r = new DayTotals();
            foreach (var kv in map)
            {
                if (kv.Key.Year == today.Year && kv.Key < today)
                {
                    r.Up += kv.Value.Up;
                    r.Down += kv.Value.Down;
                }
            }
            return r;
        }

        static string Ago(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            if (t.TotalSeconds < 60) return (int)t.TotalSeconds + " s ago";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min ago";
            if (t.TotalHours < 48) return (int)t.TotalHours + " h ago";
            return (int)t.TotalDays + " days ago";
        }
    }
}
