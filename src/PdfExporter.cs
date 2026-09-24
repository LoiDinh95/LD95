using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LDElitechReader
{
    public static class ElitechPdfExporter
    {
        public static void Write(string path, DeviceData d)
        {
            var pages = new List<string> { BuildSummaryPage(d) };
            const int rowsPerPage = 500;
            int dataPages = (int)Math.Ceiling(d.Records.Count / (double)rowsPerPage);
            for (int i = 0; i < d.Records.Count; i += rowsPerPage)
                pages.Add(BuildDataPage(d, i, Math.Min(rowsPerPage, d.Records.Count - i), 2 + i / rowsPerPage, 1 + dataPages));

            var pdf = new SimplePdf();
            pdf.Write(path, pages);
        }

        private static string BuildSummaryPage(DeviceData d)
        {
            var c = new PdfCanvas();
            string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            c.Text(60, 29.32, 24, true, "Data Report", 0, 0, 1);
            c.Text(198, 47.5, 7, true, "File created on:");
            c.Text(267, 47.5, 7, false, created);
            c.Line(60, 65, 401, 65, .7, 0, 0, 0);

            if (d.HasAlarm) c.Text(477, 36, 19, true, "ALARM", .90, 0, 0);
            else c.Text(492, 36, 20, true, "OK", 0, .72, .08);

            Section(c, 90, "Device Information", null);
            Row4(c, 103.25, "Device Model:", d.Model, "Probe Type:", d.Model.StartsWith("RC-5") ? "Temperature(Internal)" : "Temperature");
            Row4(c, 112.65, "Serial Number:", d.Serial, "Firmware Version:", d.Firmware);
            Row4(c, 122.05, d.Model.StartsWith("RC-5") ? "Mode Code:" : "Total Space:", d.Model.StartsWith("RC-5") ? "N/A" : (d.Capacity > 0 ? d.Capacity.ToString() : "16000"), "", "");

            Section(c, 136.45, "Trip Description", d.Model.StartsWith("RC-5") ? "Mark Event" : null);
            Row4(c, 149.70, d.Model.StartsWith("RC-5") ? "Trip Number:" : "Trip Number:", d.TripNumber, d.Model.StartsWith("RC-5") ? "N/A" : "", "");
            Row4(c, 159.10, "Trip Description:", d.TripDescription, "", "");

            Section(c, 191.10, "Config. info", null);
            if (d.Model.StartsWith("RC-5"))
            {
                Row4(c, 204.35, "Start Model:", d.StartMode, "Logging Interval:", IntervalShort(d.Interval));
                Row4(c, 213.75, "Start Delay:", d.StartDelay, "Cyclic Record:", d.RepeatStart);
                Row4(c, 223.15, "Time Zone:", d.TimeZone, "Stop Mode:", d.StopMode);
                DrawRc5Alarm(c, d);
                DrawSummary(c, d, 335.53);
                DrawChart(c, d, 451, 260);
            }
            else
            {
                Row4(c, 204.35, "Button Stop:", "Disable", "Logging Interval:", IntervalShort(d.Interval));
                Row4(c, 217.75, "Mute Button:", "Disable", "Alarm Logging Interval Shorten (1 min):", "--");
                Row4(c, 231.15, "Alarm Tone:", d.AudibleAlarm == "N/A" ? "Disable" : d.AudibleAlarm, "Storage Mode:", d.StorageMode == "N/A" ? "--" : d.StorageMode);
                DrawRc4Alarm(c, d);
                DrawSummary(c, d, 371.83);
                DrawChart(c, d, 481, 250);
            }

            Footer(c, 1, 1 + (int)Math.Ceiling(d.Records.Count / 500.0), d);
            return c.ToString();
        }

        private static void Section(PdfCanvas c, double y, string left, string right)
        {
            c.FillRect(59.5, y, 476, 13.25, .80, .80, .80);
            c.Text(61.5, y + .9, 10, true, left);
            if (!string.IsNullOrEmpty(right)) c.Text(299.5, y + .9, 10, true, right);
        }

        private static void Row4(PdfCanvas c, double y, string l1, string v1, string l2, string v2)
        {
            if (!string.IsNullOrEmpty(l1)) c.Text(60.5, y + .8, 8, true, l1);
            if (!string.IsNullOrEmpty(v1)) c.Text(155.7, y + .8, 8, false, v1);
            if (!string.IsNullOrEmpty(l2)) c.Text(298.5, y + .8, 8, true, l2);
            if (!string.IsNullOrEmpty(v2)) c.Text(393.7, y + .8, 8, false, v2);
        }

        private static void DrawRc4Alarm(PdfCanvas c, DeviceData d)
        {
            c.Text(61.5, 256, 8, true, "Alarm Threshold");
            c.Text(299.5, 256, 8, true, "Alarm Status");
            c.Line(59.5, 270.2, 535.5, 270.2, .7, 0, 0, 0);
            string high = d.HighLimit.HasValue ? d.HighLimit.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°C" : "N/A";
            string low = d.LowLimit.HasValue ? d.LowLimit.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°C" : "N/A";
            c.Text(60.5, 286.3, 8, false, "H1: Above:    " + high, d.HasAlarm ? .9 : 0, 0, 0);
            c.Text(298.5, 286.3, 8, false, d.HasAlarm ? "Alarm" : "Ok", d.HasAlarm ? .9 : 0, 0, 0);
            c.Text(60.5, 295.7, 8, false, "Ideal Zone:");
            c.Text(155.7, 295.7, 8, false, "Unlimited");
            c.Text(60.5, 305.1, 8, false, "L1: Below:    " + low);
            c.Text(298.5, 305.1, 8, false, "Ok");
        }

        private static void DrawRc5Alarm(PdfCanvas c, DeviceData d)
        {
            c.Text(61.5, 252, 9, true, "Alarm Threshold");
            c.Text(156.7, 252, 9, true, "Alarm Delay");
            c.Text(251.9, 252, 9, true, "Alarm Type");
            c.Text(347.1, 252, 9, true, "Over-limit Duration");
            c.Text(442.3, 248, 9, true, "Over-limit\nTimes");
            c.Text(489.9, 248, 9, true, "Alarm\nStatus");
            c.Line(59.5, 270.2, 535.5, 270.2, .7, 0, 0, 0);
            if (!d.HighLimit.HasValue && !d.LowLimit.HasValue)
            {
                c.Text(60.5, 270.0, 8, false, "No Alarm");
                return;
            }
            double y = 272;
            if (d.HighLimit.HasValue)
            {
                c.Text(60.5, y, 8, false, "H1: Above: " + d.HighLimit.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°C");
                c.Text(156.7, y, 8, false, "0m"); c.Text(251.9, y, 8, false, "Single"); c.Text(489.9, y, 8, false, d.HasAlarm ? "Alarm" : "Normal");
                y += 11;
            }
            if (d.LowLimit.HasValue)
            {
                c.Text(60.5, y, 8, false, "L1: Below: " + d.LowLimit.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°C");
                c.Text(156.7, y, 8, false, "0m"); c.Text(251.9, y, 8, false, "Single"); c.Text(489.9, y, 8, false, "Normal");
            }
        }

        private static void DrawSummary(PdfCanvas c, DeviceData d, double y)
        {
            Section(c, y, "Summary", null);
            double r = y + 13.25;
            Row4(c, r, "Maximum:", F1(d.Maximum) + "°C", "Start Time:", DT(d.StartTime ?? d.FirstReading)); r += 9.4;
            Row4(c, r, "Minimum:", F1(d.Minimum) + "°C", "Stop Time:", DT(d.StopTime ?? d.LastReading)); r += 9.4;
            Row4(c, r, "Average:", F1(d.Average) + "°C", "Logging Duration:", DurationPdf(d.RecordedPeriod)); r += 9.4;
            Row4(c, r, "MKT:", F1(d.Mkt) + "°C", d.Model.StartsWith("RC-5") ? "Data Points:" : "Total Memory:", d.Model.StartsWith("RC-5") ? d.DataPoints.ToString() : (d.Capacity > 0 ? d.Capacity.ToString() : d.DataPoints.ToString())); r += 9.4;
            Row4(c, r, d.Model.StartsWith("RC-5") ? "First Alarm(Te):" : "Alarm Time(Te):", d.FirstAlarm.HasValue ? d.FirstAlarm.Value.ToString("yyyy-MM-dd HH:mm:ss") : "N/A", d.Model.StartsWith("RC-5") ? "" : "Current Readings:", d.Model.StartsWith("RC-5") ? "" : d.DataPoints.ToString());
        }

        private static void DrawChart(PdfCanvas c, DeviceData d, double top, double height)
        {
            double x = 88, y = top, w = 430, h = height;
            if (d.Records.Count == 0) return;
            double min = d.Minimum, max = d.Maximum;
            double pad = Math.Max(.5, (max - min) * .15);
            double ymin = Math.Floor((min - pad) * 2) / 2;
            double ymax = Math.Ceiling((max + pad) * 2) / 2;
            if (Math.Abs(ymax - ymin) < .01) ymax = ymin + 1;

            c.Text(x, y - 21, 5.5, false, "Temperature°C");
            c.Line(x + 2, y - 13, x + 18, y - 13, .8, 0, 0, 0);
            c.Text(x + 58, y - 21, 5.5, false, "Upper Limit");
            c.Line(x + 60, y - 13, x + 76, y - 13, .8, 1, 0, 0, true);
            c.Text(x + 132, y - 21, 5.5, false, "Lower Limit");
            c.Line(x + 134, y - 13, x + 150, y - 13, .8, 0, .55, 1, true);
            c.Text(x + 205, y - 21, 5.5, false, "Fault");
            c.Line(x + 207, y - 13, x + 223, y - 13, .6, .55, .55, .55, true);

            c.Rect(x, y, w, h, .5, .45, .45, .45);
            for (int i = 1; i < 8; i++) { double xx = x + w * i / 8.0; c.Line(xx, y, xx, y + h, .35, .55, .55, .55, true); }
            for (int i = 1; i < 5; i++) { double yy = y + h * i / 5.0; c.Line(x, yy, x + w, yy, .35, .55, .55, .55, true); }
            for (int i = 0; i <= 4; i++) { double val = ymax - (ymax - ymin) * i / 4.0; c.Text(x - 29, y + h * i / 4.0 - 4, 5.5, false, val.ToString("0.0", CultureInfo.InvariantCulture)); }

            DateTime t0 = d.FirstReading.Value, t1 = d.LastReading.Value;
            long ticks = Math.Max(1, t1.Ticks - t0.Ticks);
            int step = Math.Max(1, d.Records.Count / 1400);
            bool first = true; double px = 0, py = 0;
            for (int i = 0; i < d.Records.Count; i += step)
            {
                var r = d.Records[i];
                double xx = x + w * (r.Time.Ticks - t0.Ticks) / (double)ticks;
                double yy = y + h - (r.Temperature - ymin) / (ymax - ymin) * h;
                if (first) { px = xx; py = yy; first = false; }
                else { c.Line(px, py, xx, yy, .45, 0, 0, 0); px = xx; py = yy; }
            }

            if (d.HighLimit.HasValue)
            {
                double yy = y + h - (d.HighLimit.Value - ymin) / (ymax - ymin) * h;
                if (yy >= y && yy <= y + h) c.Line(x, yy, x + w, yy, .55, 1, 0, 0, true);
            }
            if (d.LowLimit.HasValue)
            {
                double yy = y + h - (d.LowLimit.Value - ymin) / (ymax - ymin) * h;
                if (yy >= y && yy <= y + h) c.Line(x, yy, x + w, yy, .55, 0, .55, 1, true);
            }

            for (int i = 0; i <= 6; i++)
            {
                var tt = new DateTime(t0.Ticks + (long)(ticks * i / 6.0));
                double xx = x + w * i / 6.0;
                c.Text(xx - 18, y + h + 6, 5, false, tt.ToString("yyyy-MM-dd\nHH:mm:ss"));
            }
            c.Text(x + w + 5, y + h - 2, 5, false, "Time");
        }

        private static string BuildDataPage(DeviceData d, int start, int count, int pageNo, int totalPages)
        {
            var c = new PdfCanvas();
            string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string from = d.FirstReading.HasValue ? d.FirstReading.Value.ToString("yyyy-MM-dd HH:mm:ss") : "N/A";
            string to = d.LastReading.HasValue ? d.LastReading.Value.ToString("yyyy-MM-dd HH:mm:ss") : "N/A";
            c.Text(60, 35, 7, true, "From ");
            c.Text(79, 35, 7, false, from + " to " + to);
            c.Text(430, 35, 7, true, "File created on:");
            c.Text(496, 35, 7, false, created);
            c.Line(60, 48, 535.5, 48, .7, 0, 0, 0);

            double x0 = 59.5, top = 65.7, pairW = 95.2, timeW = 57.12, rowH = 7.0;
            for (int col = 0; col < 5; col++)
            {
                double x = x0 + pairW * col;
                c.FillRect(x, top, timeW, rowH * 101, .902, .902, .902);
                c.Rect(x, top, pairW, rowH * 101, .55, 0, 0, 0);
                c.Line(x + timeW, top, x + timeW, top + rowH * 101, .45, 0, 0, 0);
                c.Text(x + 24, top + .8, 4.5, true, "Time");
                c.Text(x + timeW + 13, top + .8, 4.5, true, "°C");

                int baseIndex = start + col * 100;
                DateTime? previousDate = null;
                for (int row = 0; row < 100; row++)
                {
                    int idx = baseIndex + row;
                    if (idx >= start + count || idx >= d.Records.Count) break;
                    var r = d.Records[idx];
                    bool full = !previousDate.HasValue || previousDate.Value.Date != r.Time.Date;
                    previousDate = r.Time.Date;
                    string ts = full ? r.Time.ToString("yyyy-MM-dd HH:mm:ss") : r.Time.ToString("HH:mm:ss");
                    bool alarm = (d.HighLimit.HasValue && r.Temperature > d.HighLimit.Value) || (d.LowLimit.HasValue && r.Temperature < d.LowLimit.Value);
                    c.Text(x + 2, top + rowH * (row + 1) + .4, 4.2, false, ts, alarm ? .9 : 0, 0, 0);
                    c.Text(x + timeW + 5, top + rowH * (row + 1) + .4, 4.2, false, r.Temperature.ToString("0.0", CultureInfo.InvariantCulture), alarm ? .9 : 0, 0, 0);
                }
            }
            Footer(c, pageNo, totalPages, d);
            return c.ToString();
        }

        private static void Footer(PdfCanvas c, int page, int total, DeviceData d)
        {
            c.Line(49, 806, 546, 806, .7, 0, 0, 0);
            c.Text(49, 811, 6.5, true, "www.e-elitech.com");
            c.Text(289, 811, 6.5, true, page + "/" + total);
            string fn = d.Model.StartsWith("RC-5") ? d.Serial + "_" + d.TripNumber : d.Serial + "_";
            c.Text(430, 811, 6.5, true, "File Name:");
            c.Text(483, 811, 6.5, false, fn);
        }

        private static string F1(double x) { return double.IsNaN(x) ? "N/A" : x.ToString("0.0", CultureInfo.InvariantCulture); }
        private static string DT(DateTime? x) { return x.HasValue ? x.Value.ToString("yyyy-MM-dd HH:mm:ss") : "N/A"; }
        private static string IntervalShort(TimeSpan t) { if (t.TotalMinutes >= 1 && t.Seconds == 0) return ((int)t.TotalMinutes) + "m"; return ((int)t.TotalSeconds) + "s"; }
        private static string DurationPdf(TimeSpan t)
        {
            var s = new StringBuilder();
            if (t.Days > 0) s.Append(t.Days + "d ");
            if (t.Hours > 0) s.Append(t.Hours + "h ");
            if (t.Minutes > 0) s.Append(t.Minutes + "m");
            if (s.Length == 0) s.Append(t.Seconds + "s");
            return s.ToString().Trim();
        }
    }

    internal sealed class PdfCanvas
    {
        private readonly StringBuilder s = new StringBuilder();
        private static string Num(double x) { return x.ToString("0.###", CultureInfo.InvariantCulture); }
        private static double Y(double top) { return 842 - top; }

        public void Text(double x, double top, double size, bool bold, string text, double r = 0, double g = 0, double b = 0)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
                s.Append(Num(r)+" "+Num(g)+" "+Num(b)+" rg BT /F"+(bold?"2":"1")+" "+Num(size)+" Tf 1 0 0 1 "+Num(x)+" "+Num(Y(top+i*(size+1)))+" Tm ("+Escape(lines[i])+") Tj ET\n");
        }

        public void FillRect(double x,double top,double w,double h,double r,double g,double b)
        { s.Append(Num(r)+" "+Num(g)+" "+Num(b)+" rg "+Num(x)+" "+Num(Y(top+h))+" "+Num(w)+" "+Num(h)+" re f\n"); }

        public void Rect(double x,double top,double w,double h,double width,double r,double g,double b)
        { s.Append(Num(r)+" "+Num(g)+" "+Num(b)+" RG "+Num(width)+" w "+Num(x)+" "+Num(Y(top+h))+" "+Num(w)+" "+Num(h)+" re S\n"); }

        public void Line(double x1,double y1,double x2,double y2,double width,double r,double g,double b,bool dashed=false)
        { s.Append(Num(r)+" "+Num(g)+" "+Num(b)+" RG "+Num(width)+" w "+(dashed?"[2 2] 0 d ":"[] 0 d ")+Num(x1)+" "+Num(Y(y1))+" m "+Num(x2)+" "+Num(Y(y2))+" l S\n"); }

        private static string Escape(string t)
        {
            byte[] bb = Encoding.GetEncoding(1252).GetBytes(t);
            var z = new StringBuilder();
            foreach (byte x in bb)
            {
                if (x == 40 || x == 41 || x == 92) z.Append('\\').Append((char)x);
                else if (x < 32 || x > 126) z.Append('\\').Append(Convert.ToString(x, 8).PadLeft(3, '0'));
                else z.Append((char)x);
            }
            return z.ToString();
        }

        public override string ToString() { return s.ToString(); }
    }

    internal sealed class SimplePdf
    {
        private readonly List<byte[]> objects = new List<byte[]>();
        private int AddObject(string text) { objects.Add(Encoding.ASCII.GetBytes(text)); return objects.Count; }
        private int Reserve() { objects.Add(null); return objects.Count; }
        private void Set(int n, string text) { objects[n - 1] = Encoding.ASCII.GetBytes(text); }
        private void Set(int n, byte[] data) { objects[n - 1] = data; }

        public void Write(string path, List<string> pages)
        {
            int font = AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            int bold = AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            int pagesObj = Reserve();
            int catalog = Reserve();
            var pageRefs = new List<int>();

            foreach (string content in pages)
            {
                byte[] cb = Encoding.GetEncoding(1252).GetBytes(content);
                int cont = Reserve();
                Set(cont, StreamObject(cb));
                int page = Reserve();
                pageRefs.Add(page);
                Set(page, "<< /Type /Page /Parent "+pagesObj+" 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 "+font+" 0 R /F2 "+bold+" 0 R >> >> /Contents "+cont+" 0 R >>");
            }

            Set(pagesObj, "<< /Type /Pages /Kids ["+string.Join(" ", pageRefs.Select(x => x+" 0 R"))+"] /Count "+pageRefs.Count+" >>");
            Set(catalog, "<< /Type /Catalog /Pages "+pagesObj+" 0 R >>");

            using (var ms = new MemoryStream())
            {
                WriteA(ms, "%PDF-1.4\n");
                var offsets = new List<long> { 0 };
                for (int i = 0; i < objects.Count; i++)
                {
                    offsets.Add(ms.Position);
                    WriteA(ms, (i + 1) + " 0 obj\n");
                    ms.Write(objects[i], 0, objects[i].Length);
                    WriteA(ms, "\nendobj\n");
                }
                long xref = ms.Position;
                WriteA(ms, "xref\n0 "+(objects.Count+1)+"\n0000000000 65535 f \n");
                for (int i = 1; i < offsets.Count; i++) WriteA(ms, offsets[i].ToString("0000000000")+" 00000 n \n");
                WriteA(ms, "trailer\n<< /Size "+(objects.Count+1)+" /Root "+catalog+" 0 R >>\nstartxref\n"+xref+"\n%%EOF\n");
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        private static byte[] StreamObject(byte[] b)
        {
            using (var ms = new MemoryStream())
            {
                WriteA(ms, "<< /Length "+b.Length+" >>\nstream\n");
                ms.Write(b, 0, b.Length);
                WriteA(ms, "\nendstream");
                return ms.ToArray();
            }
        }

        private static void WriteA(Stream s, string x)
        {
            byte[] b = Encoding.ASCII.GetBytes(x);
            s.Write(b, 0, b.Length);
        }
    }
}