// Traffic Target - daily and yearly data-traffic target tracker for Windows.
// Builds with the C# 5 compiler that ships with Windows (.NET Framework 4.x): see build.bat.
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Traffic Target")]
[assembly: System.Reflection.AssemblyProduct("Traffic Target")]
[assembly: System.Reflection.AssemblyDescription("Daily and yearly data-traffic target tracker")]
[assembly: System.Reflection.AssemblyCompany("Md. Fahim Iftekhar")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 Md. Fahim Iftekhar - MIT License")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.0.0.0")]

namespace TrafficTarget
{
    static class AppInfo
    {
        public const string Name = "Traffic Target";
        public const string Version = "2.0.0";
        public const string Author = "Md. Fahim Iftekhar";
        public const string RepoUrl = "https://github.com/Fahim-Iftekhar/traffic-target";
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // TrafficTarget.exe /screenshots "<folder>"  -> saves PNG screenshots of every view using demo data, then exits.
            int si = Array.FindIndex(args, a => string.Equals(a, "/screenshots", StringComparison.OrdinalIgnoreCase));
            if (si >= 0)
            {
                string dir = si + 1 < args.Length ? args[si + 1] : Path.Combine(Environment.CurrentDirectory, "screenshots");
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                UI.Init();
                AppSettings.ReadOnly = true;
                Directory.CreateDirectory(dir);
                Application.Run(new MainForm(false, dir));
                return;
            }

            bool created;
            using (var mutex = new System.Threading.Mutex(true, "TrafficTarget_SingleInstance_v1", out created))
            {
                if (!created)
                {
                    // Already running: ask the running copy to show itself.
                    Native.PostMessage((IntPtr)Native.HWND_BROADCAST, Native.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { Report(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Report(e.ExceptionObject as Exception); };
                UI.Init();
                bool tray = args.Any(a => string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));
                Application.Run(new MainForm(tray));
                GC.KeepAlive(mutex);
            }
        }

        static void Report(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(AppSettings.Dir);
                File.AppendAllText(Path.Combine(AppSettings.Dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            try
            {
                MessageBox.Show("Something went wrong:\n\n" + ex.Message + "\n\nDetails were saved to " +
                    Path.Combine(AppSettings.Dir, "error.log"), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }

    static class Native
    {
        public const int HWND_BROADCAST = 0xffff;
        public static readonly int WM_SHOWME = RegisterWindowMessage("TrafficTarget_ShowMe_v1");

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

        // Window bounds without the invisible resize border (for screenshots).
        public static System.Drawing.Rectangle VisibleBounds(Form f)
        {
            try
            {
                RECT r;
                if (DwmGetWindowAttribute(f.Handle, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0)
                    return System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
            catch { }
            return f.Bounds;
        }

        // Dark title bar on Windows 10 (20H1+) and Windows 11.
        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref v, 4);
            }
            catch { }
        }
    }
}
