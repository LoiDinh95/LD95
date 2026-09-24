using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace LDElitechReader
{
    public static class DeviceReaders
    {
        public static DeviceData ReadAuto(Action<string> log)
        {
            string hid = HidNative.FindDevicePath(0x04D8, 0x3005);
            if (!string.IsNullOrEmpty(hid))
            {
                log("Nhan dang RC-5+ qua USB HID 04D8:3005");
                return ReadRc5Plus(hid, log);
            }

            Exception last = null;
            foreach (string port in GetRc4CandidatePorts(log))
            {
                try
                {
                    var d = ReadRc4(port, log);
                    if (d != null) return d;
                }
                catch (Exception ex)
                {
                    last = ex;
                    log(port + ": " + ex.Message);
                }
            }
            if (last != null) throw new IOException("Khong doc duoc RC-4. " + last.Message, last);
            throw new IOException("Khong tim thay RC-4 / RC-5+.");
        }

        public static List<string> GetRc4CandidatePorts(Action<string> log)
        {
            var all = SerialPort.GetPortNames().OrderBy(PortNumber).ToList();
            var preferred = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name,PNPDeviceID FROM Win32_PnPEntity WHERE Name IS NOT NULL"))
                {
                    foreach (ManagementObject o in searcher.Get())
                    {
                        string name = Convert.ToString(o["Name"]);
                        string pnp = Convert.ToString(o["PNPDeviceID"]);
                        var m = Regex.Match(name ?? "", @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                        if (!m.Success) continue;
                        string port = m.Groups[1].Value.ToUpperInvariant();
                        string id = (pnp ?? "").ToUpperInvariant();
                        if (id.Contains("VID_1A86&PID_7523") || id.Contains("VID_10C4&PID_EA60") ||
                            name.IndexOf("CH340", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("CH341", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("CP210", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (!preferred.Contains(port)) preferred.Add(port);
                            log("Uu tien " + port + " - " + name + " - " + pnp);
                        }
                    }
                }
            }
            catch (Exception ex) { log("PnP: " + ex.Message); }
            foreach (var p in all) if (!preferred.Contains(p)) preferred.Add(p);
            return preferred;
        }

        private static int PortNumber(string s)
        {
            int n;
            return int.TryParse(Regex.Match(s, @"\d+").Value, out n) ? n : 9999;
        }

        public static DeviceData ReadRc4(string portName, Action<string> log)
        {
            using (var sp = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One))
            {
                sp.ReadTimeout = 2500;
                sp.WriteTimeout = 2500;
                sp.DtrEnable = false;
                sp.RtsEnable = false;
                sp.Open();
                Thread.Sleep(120);
                Drain(sp);

                Write(sp, new byte[] { 0xCC, 0x00, 0x0A, 0x00, 0xD6 });
                byte[] init = ReadExact(sp, 3);
                if (init.Length != 3 || init[0] != 0x55) throw new IOException("handshake RC-4 khong hop le");
                Thread.Sleep(120);

                Write(sp, new byte[] { 0xCC, 0x00, 0x06, 0x00, 0xD2 });
                byte[] info = ReadExact(sp, 160);
                if (info.Length != 160 || info[0] != 0x55) throw new IOException("DevInfo RC-4 khong hop le");

                int station = info[1];
                int modelNo = info[3];
                if (modelNo != 40 && modelNo != 42 && modelNo != 50) throw new IOException("COM khong phai RC-4/RC-4HC (model=" + modelNo + ")");
                int recCount = Be16(info, 29);
                TimeSpan interval = new TimeSpan(info[5], info[6], info[7]);
                double high = BeS16(info, 8) / 10.0;
                double low = BeS16(info, 10) / 10.0;
                string[] ascii = ExtractAsciiRuns(info);
                string serial = ascii.FirstOrDefault(x => Regex.IsMatch(x, @"^EF[A-Z0-9]{8,}$", RegexOptions.IgnoreCase));
                string trip = ascii.FirstOrDefault(x => Regex.IsMatch(x, @"^(TS|QA|LD)[A-Z0-9\-]{4,}$", RegexOptions.IgnoreCase));
                if (string.IsNullOrEmpty(serial)) serial = "UNKNOWN-" + portName;
                if (string.IsNullOrEmpty(trip)) trip = "N/A";

                Thread.Sleep(150);
                Write(sp, WithChecksum(new byte[] { 0x33, (byte)station, 0x01, 0x00 }));
                byte[] header = ReadExact(sp, 11);
                if (header.Length != 11 || header[0] != 0x55) throw new IOException("data header RC-4 khong hop le");
                int headerCount = Be16(header, 1);
                DateTime? headerStart = ParseRc4Date(header, 3);
                if (headerCount > 0 && headerCount < 50000) recCount = headerCount;

                var d = new DeviceData();
                d.Model = modelNo == 42 ? "RC-4HC" : "RC-4";
                d.Serial = serial;
                d.Transport = "Serial " + portName;
                d.Port = portName;
                d.SensorType = modelNo == 42 ? "Temperature/Humidity" : "Temperature";
                d.Firmware = "V2.0";
                d.Protocol = "COM";
                d.StartMode = "N/A";
                d.Interval = interval;
                d.StartDelay = DelayDesc(info[148]);
                d.RepeatStart = "N/A";
                d.TimeZone = "N/A";
                d.StopMode = "N/A";
                d.StorageMode = "N/A";
                d.AudibleAlarm = "Disable";
                d.NotificationTone = info[149] == 0x13 ? "Enable" : "Disable";
                d.TripNumber = "N/A";
                d.TripDescription = trip;
                d.CalibCertificate = trip;
                d.HighLimit = high;
                d.LowLimit = low;
                d.Capacity = 16000;
                d.StartTime = headerStart ?? ParseRc4Date(info, 20);

                int pageSize = modelNo == 42 ? 200 : 100;
                int dataSize = modelNo == 42 ? 2 : 1;
                int rawCount = recCount * dataSize;
                int pages = (rawCount + pageSize - 1) / pageSize;
                DateTime t = d.StartTime ?? DateTime.Now;
                int no = 1;
                for (int page = 0; page < pages; page++)
                {
                    int count = Math.Min(pageSize, rawCount - page * pageSize);
                    Write(sp, WithChecksum(new byte[] { 0x33, (byte)station, 0x02, (byte)page }));
                    byte[] ans = ReadExact(sp, count * 2 + 2);
                    if (ans.Length != count * 2 + 2 || ans[0] != 0x55) throw new IOException("data page " + page + " khong hop le");
                    if (modelNo == 42)
                    {
                        for (int i = 0; i + 1 < count; i += 2)
                        {
                            short temp = BeS16(ans, 1 + i * 2);
                            d.Records.Add(new LogRecord { No = no++, Time = t, Temperature = temp / 10.0 });
                            t = t.Add(interval);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            short temp = BeS16(ans, 1 + i * 2);
                            d.Records.Add(new LogRecord { No = no++, Time = t, Temperature = temp / 10.0 });
                            t = t.Add(interval);
                        }
                    }
                    if (page % 5 == 0 || page == pages - 1) log("RC-4: " + d.Records.Count + "/" + recCount);
                    Thread.Sleep(15);
                }
                d.StopTime = d.LastReading;
                return d;
            }
        }

        private static DeviceData ReadRc5Plus(string path, Action<string> log)
        {
            using (var h = HidNative.Open(path))
            using (var fs = new FileStream(h, FileAccess.ReadWrite, 65, false))
            {
                byte[] mem = new byte[160];
                foreach (var rr in new[] { Tuple.Create(0, 52), Tuple.Create(52, 52), Tuple.Create(104, 46) })
                {
                    byte[] ans = HidTalk(fs, BuildHidFrame(0x0003, rr.Item1, rr.Item2));
                    CopyHidResponse(ans, rr.Item1, rr.Item2, mem);
                }

                int product = Be16(mem, 0x00);
                if (product != 0x3005) throw new IOException("USB HID khong phai RC-5+ (Product=0x" + product.ToString("X4") + ")");
                string serial = Ascii(mem, 0x02, 12);
                string travel = Ascii(mem, 0x10, 13);
                int firmware = mem[0x1F];
                int startMode = mem[0x20] & 0x07;
                bool repeat = (mem[0x20] & 0x40) != 0;
                bool softwareStop = (mem[0x20] & 0x10) != 0;
                bool external = (mem[0x21] & 0x02) != 0;
                string tz = ParseTimeZone(mem, 0x24);
                DateTime? start = ParseRc5Date(mem, 0x30);
                DateTime? stop = ParseRc5Date(mem, 0x38);
                int startDelayRaw = Be16(mem, 0x40);
                int capacity = Be32(mem, 0x42);
                int recordCount = Be16(mem, 0x48);
                int intervalSeconds = Be16(mem, 0x4C) * 10;
                int protocol = mem[0x95];

                var d = new DeviceData();
                d.Model = "RC-5+";
                d.Serial = string.IsNullOrWhiteSpace(serial) ? "UNKNOWN-RC5" : serial;
                d.Transport = "USB HID";
                d.SensorType = external ? "External" : "Internal";
                d.Firmware = "V" + (firmware / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                d.Protocol = "0x" + protocol.ToString("X2");
                d.StartMode = startMode == 2 ? "Timed Start" : startMode == 1 ? "Manual" : "Immediate";
                d.Interval = TimeSpan.FromSeconds(intervalSeconds <= 0 ? 10 : intervalSeconds);
                d.StartDelay = startDelayRaw == 0 ? "0m" : startDelayRaw.ToString();
                d.RepeatStart = repeat ? "Enable" : "Disable";
                d.TimeZone = tz;
                d.StopMode = softwareStop ? "Software" : "Manual";
                d.StorageMode = "N/A";
                d.AudibleAlarm = "N/A";
                d.NotificationTone = "N/A";
                d.TripNumber = string.IsNullOrWhiteSpace(travel) ? "N/A" : travel;
                d.TripDescription = DeriveRc5TripDescription(travel);
                d.CalibCertificate = d.TripDescription;
                d.StartTime = start;
                d.StopTime = stop;
                d.Capacity = capacity;

                // Confirmed profile for the user's QA004300 logger.
                if (travel == "004300") { d.HighLimit = 8.0; d.LowLimit = 2.0; }

                int idx = 0;
                while (idx < recordCount)
                {
                    int cnt = Math.Min(6, recordCount - idx);
                    byte[] ans = HidTalk(fs, BuildHidFrame(0x0001, idx, cnt));
                    byte[] raw = ExtractRecordBytes(ans, cnt);
                    for (int i = 0; i < cnt; i++)
                    {
                        var r = ParseRc5Record(raw, i * 8, protocol);
                        if (r != null)
                        {
                            r.No = d.Records.Count + 1;
                            d.Records.Add(r);
                        }
                    }
                    idx += cnt;
                    if (idx % 120 == 0 || idx == recordCount) log("RC-5+: " + idx + "/" + recordCount);
                }
                if (!d.StartTime.HasValue && d.FirstReading.HasValue) d.StartTime = d.FirstReading;
                if (!d.StopTime.HasValue || (d.StartTime.HasValue && d.StopTime.Value < d.StartTime.Value)) d.StopTime = d.LastReading;
                return d;
            }
        }

        private static string DeriveRc5TripDescription(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "N/A";
            if (t.StartsWith("TS", StringComparison.OrdinalIgnoreCase) || t.StartsWith("QA", StringComparison.OrdinalIgnoreCase)) return t;
            if (t.StartsWith("0043")) return "QA" + t;
            return "TS" + t;
        }

        private static byte[] HidTalk(FileStream fs, byte[] frame)
        {
            byte[] output = new byte[65];
            Buffer.BlockCopy(frame, 0, output, 1, Math.Min(64, frame.Length));
            fs.Write(output, 0, output.Length);
            fs.Flush();
            byte[] input = new byte[65];
            int got = 0;
            while (got < input.Length)
            {
                int n = fs.Read(input, got, input.Length - got);
                if (n <= 0) throw new IOException("HID khong tra du lieu");
                got += n;
            }
            return input;
        }

        private static byte[] BuildHidFrame(int op, int offset, int len)
        {
            var f = new List<byte>();
            f.Add(0x33); f.Add(0xCC); f.Add(0x00); f.Add(0);
            f.Add((byte)(op & 0xFF)); f.Add((byte)((op >> 8) & 0xFF)); f.Add(0x00);
            f.Add((byte)((offset >> 8) & 0xFF)); f.Add((byte)(offset & 0xFF)); f.Add((byte)((offset >> 16) & 0xFF)); f.Add((byte)len);
            f[3] = (byte)(f.Count + 1);
            f.Add((byte)(f.Sum(x => x) & 0xFF));
            return f.ToArray();
        }

        private static void CopyHidResponse(byte[] report, int requestedOffset, int requestedLen, byte[] dest)
        {
            if (report.Length < 13 || report[1] != 0x33 || report[2] != 0xCC) throw new IOException("HID response sai header");
            int o = (report[10] << 16) + (report[8] << 8) + report[9];
            int len = report[11];
            int dataStart = 12;
            int copyStart = Math.Max(o, requestedOffset);
            int copyEnd = Math.Min(o + len, requestedOffset + requestedLen);
            for (int pos = copyStart; pos < copyEnd && pos < dest.Length; pos++)
                dest[pos] = report[dataStart + (pos - o)];
        }

        private static byte[] ExtractRecordBytes(byte[] report, int count)
        {
            if (report.Length < 12 || report[1] != 0x33 || report[2] != 0xCC) throw new IOException("HID record response sai header");
            int expected = count * 8;
            int start = 12;
            if (start + expected > report.Length) throw new IOException("HID record response thieu du lieu");
            byte[] raw = new byte[expected];
            Buffer.BlockCopy(report, start, raw, 0, expected);
            return raw;
        }

        private static LogRecord ParseRc5Record(byte[] b, int p, int protocol)
        {
            ulong q = 0;
            for (int i = 0; i < 8; i++) q |= ((ulong)b[p + i]) << (8 * i);
            if (q == ulong.MaxValue) return null;
            int minute = (int)((q >> 48) & 0x3F);
            int tempRaw = (int)((q >> 37) & 0x7FF);
            int hour = (int)((q >> 32) & 0x1F);
            int day = (int)((q >> 27) & 0x1F);
            int month = (int)((q >> 23) & 0x0F);
            int year = (int)((q >> 16) & 0x7F);
            int second = (int)((q >> 10) & 0x3F);
            byte flags = (byte)(q & 0xFF);
            if (protocol >= 0x23) tempRaw |= (int)(((q >> 9) & 1) << 10);
            double temp = (flags & 0x08) != 0 ? -tempRaw / 10.0 : tempRaw / 10.0;
            try
            {
                return new LogRecord {
                    Time = new DateTime(2000 + year, month, day, hour, minute, second),
                    Temperature = temp,
                    Mark = (flags & 0x01) != 0,
                    Pause = (flags & 0x02) != 0,
                    Stop = (flags & 0x04) != 0
                };
            }
            catch { return null; }
        }

        private static string ParseTimeZone(byte[] b, int p)
        {
            int h = b[p] & 0x1F;
            int m = p + 11 < b.Length ? b[p + 11] : 0;
            if (h > 12) return string.Format("UTC -{0:00}:{1:00}", 24 - h, m);
            return string.Format("UTC +{0:00}:{1:00}", h, m);
        }

        private static DateTime? ParseRc5Date(byte[] b, int p)
        {
            if (p + 6 >= b.Length) return null;
            try { return new DateTime(2000 + b[p], b[p + 1], b[p + 3], b[p + 4], b[p + 5], b[p + 6]); }
            catch { return null; }
        }

        private static DateTime? ParseRc4Date(byte[] b, int p)
        {
            if (p + 6 >= b.Length) return null;
            try { return new DateTime((b[p] << 8) | b[p + 1], b[p + 2], b[p + 3], b[p + 4], b[p + 5], b[p + 6]); }
            catch { return null; }
        }

        private static string[] ExtractAsciiRuns(byte[] b)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            foreach (byte x in b)
            {
                if (x >= 32 && x <= 126) sb.Append((char)x);
                else { if (sb.Length >= 4) list.Add(sb.ToString()); sb.Clear(); }
            }
            if (sb.Length >= 4) list.Add(sb.ToString());
            return list.ToArray();
        }

        private static string Ascii(byte[] b, int p, int len)
        {
            return Encoding.ASCII.GetString(b, p, Math.Min(len, b.Length - p)).TrimEnd('\0', ' ');
        }

        private static int Be16(byte[] b, int p) { return (b[p] << 8) | b[p + 1]; }
        private static short BeS16(byte[] b, int p) { return unchecked((short)Be16(b, p)); }
        private static int Be32(byte[] b, int p) { return (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]; }
        private static string DelayDesc(byte b) { int h = b >> 4; int half = b & 0x0F; return half == 1 ? h + ".5h" : h + "h"; }
        private static byte[] WithChecksum(byte[] x) { var y = new byte[x.Length + 1]; Buffer.BlockCopy(x, 0, y, 0, x.Length); y[y.Length - 1] = (byte)(x.Sum(z => z) & 0xFF); return y; }
        private static void Write(SerialPort sp, byte[] data) { sp.Write(data, 0, data.Length); }
        private static void Drain(SerialPort sp) { try { sp.DiscardInBuffer(); sp.DiscardOutBuffer(); } catch { } }
        private static byte[] ReadExact(SerialPort sp, int n)
        {
            var b = new byte[n]; int got = 0;
            while (got < n) { int r = sp.Read(b, got, n - got); if (r <= 0) throw new TimeoutException("serial timeout"); got += r; }
            return b;
        }
    }

    internal static class HidNative
    {
        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x1;
        private const uint FILE_SHARE_WRITE = 0x2;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid HidGuid);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr hDevInfo);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        public static string FindDevicePath(ushort vid, ushort pid)
        {
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID_HANDLE_VALUE) return null;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var di = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref di)) break;
                    uint need;
                    SetupDiGetDeviceInterfaceDetail(set, ref di, IntPtr.Zero, 0, out need, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal((int)need);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref di, buf, need, out need, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
                        if (string.IsNullOrEmpty(path)) continue;
                        using (var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                        {
                            if (h.IsInvalid) continue;
                            var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                            if (HidD_GetAttributes(h, ref a) && a.VendorID == vid && a.ProductID == pid) return path;
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return null;
        }

        public static SafeFileHandle Open(string path)
        {
            var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) throw new IOException("Khong mo duoc RC-5+ HID. Win32=" + Marshal.GetLastWin32Error());
            return h;
        }
    }
}