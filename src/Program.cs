using System;
using System.Windows.Forms;

namespace LDElitechReader
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && Array.Exists(args, a => a == "--selftest"))
            {
                SelfTest.Run();
                return;
            }
            if (args != null && Array.Exists(args, a => a == "--idle-test"))
            {
                IdleTest.Run();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "LD Elitech Reader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
        }
    }

    internal static class SelfTest
    {
        public static void Run()
        {
            var d = new DeviceData {
                Model = "RC-5+", Serial = "EFI198E00979", Firmware = "V1.4",
                SensorType = "Internal", StartMode = "Timed Start",
                Interval = TimeSpan.FromSeconds(10), StartDelay = "0m",
                RepeatStart = "Disable", TimeZone = "UTC +07:00",
                StopMode = "Software", TripNumber = "004300",
                TripDescription = "QA004300", CalibCertificate = "QA004300",
                Capacity = 32000, HighLimit = 8.0, LowLimit = 2.0
            };
            DateTime t = new DateTime(2026, 9, 17, 8, 15, 0);
            for (int i = 0; i < 878; i++)
            {
                double v = i < 80 ? 18.5 - i * 0.15 : (i < 600 ? 5.0 + Math.Sin(i / 9.0) : 5.0 + (i - 600) * 0.065);
                d.Records.Add(new LogRecord { No = i + 1, Time = t.AddSeconds(i * 10), Temperature = Math.Round(v, 1) });
            }
            d.StartTime = d.FirstReading;
            d.StopTime = d.LastReading;
            ElitechPdfExporter.Write("selftest_rc5.pdf", d);
        }
    }

    internal static class IdleTest
    {
        public static void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm();
            int heartbeats = 0;
            var heartbeat = new Timer { Interval = 1000 };
            heartbeat.Tick += (s, e) => heartbeats++;
            var stop = new Timer { Interval = 70000 };
            stop.Tick += (s, e) => {
                stop.Stop();
                heartbeat.Stop();
                if (heartbeats < 60) Environment.ExitCode = 3;
                form.Close();
            };
            form.Shown += (s, e) => { heartbeat.Start(); stop.Start(); };
            Application.Run(form);
            if (heartbeats < 60) throw new InvalidOperationException("UI heartbeat stalled: " + heartbeats);
        }
    }
}