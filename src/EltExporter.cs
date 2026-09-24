using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LDElitechReader
{
    public static class EltExporter
    {
        public static void Write(string outputPath, DeviceData d, string appDir)
        {
            bool rc5 = d.Model.StartsWith("RC-5", StringComparison.OrdinalIgnoreCase);
            string template = Path.Combine(appDir, "assets", "templates", rc5 ? "RC5_template.elt" : "RC4_template.elt");
            if (!File.Exists(template)) throw new FileNotFoundException("Thieu ELT template", template);

            byte[] data = File.ReadAllBytes(template);

            if (rc5)
            {
                data = ReplaceSerializedString(data, "EFI196E01581", d.Serial);
                data = ReplaceSerializedString(data, "000100", d.TripNumber == "N/A" ? "000000" : d.TripNumber);
                data = ReplaceSerializedString(data, "TS000100", d.TripDescription == "N/A" ? "TS000000" : d.TripDescription);
                data = ReplaceSerializedString(data, "EFI196E01581_20260816173411", d.Serial + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                data = ReplaceSerializedString(data, "9D 1H 30M 0S", MainForm.FormatDuration(d.RecordedPeriod));
            }
            else
            {
                data = ReplaceSerializedString(data, "EF5218100616", d.Serial);
                data = ReplaceSerializedString(data, "TS004900", d.TripDescription == "N/A" ? "TS000000" : d.TripDescription);
                data = ReplaceSerializedString(data, "EF5218100616_20260921083430", d.Serial + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            }

            string oldDiff = ExtractUtf8Block(data, "<diffgr:diffgram", "</diffgr:diffgram>");
            if (!string.IsNullOrEmpty(oldDiff))
                data = ReplaceSerializedString(data, oldDiff, BuildDiffgram(d));

            File.WriteAllBytes(outputPath, data);
        }

        private static string BuildDiffgram(DeviceData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<diffgr:diffgram xmlns:msdata=\"urn:schemas-microsoft-com:xml-msdata\" xmlns:diffgr=\"urn:schemas-microsoft-com:xml-diffgram-v1\">");
            sb.AppendLine("  <tmpDataSet>");
            for (int i = 0; i < d.Records.Count; i++)
            {
                var r = d.Records[i];
                sb.Append("    <Table1 diffgr:id=\"Table1").Append(i + 1)
                  .Append("\" msdata:rowOrder=\"").Append(i)
                  .AppendLine("\" diffgr:hasChanges=\"inserted\">");
                sb.Append("      <num>").Append(i + 1).AppendLine("</num>");
                sb.Append("      <Id>").Append(i + 1).AppendLine("</Id>");
                sb.Append("      <Time>").Append(r.Time.ToString("yyyy-MM-ddTHH:mm:ss")).AppendLine("+07:00</Time>");
                sb.Append("      <Value1>").Append(r.Temperature.ToString("0.0", CultureInfo.InvariantCulture)).AppendLine("</Value1>");
                if (d.Model.StartsWith("RC-5", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("      <Value2>0</Value2>");
                sb.Append("      <Mark>").Append(r.Mark ? "true" : "false").AppendLine("</Mark>");
                sb.Append("      <Pause>").Append(r.Pause ? "true" : "false").AppendLine("</Pause>");
                sb.Append("      <Stop>").Append(r.Stop ? "true" : "false").AppendLine("</Stop>");
                if (d.Model.StartsWith("RC-5", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("      <Illumination>0</Illumination>");
                    sb.AppendLine("      <Vibration>0</Vibration>");
                }
                sb.AppendLine("    </Table1>");
            }
            sb.AppendLine("  </tmpDataSet>");
            sb.Append("</diffgr:diffgram>");
            return sb.ToString();
        }

        private static string ExtractUtf8Block(byte[] data, string startText, string endText)
        {
            byte[] start = Encoding.UTF8.GetBytes(startText);
            byte[] end = Encoding.UTF8.GetBytes(endText);
            int p = IndexOf(data, start, 0);
            if (p < 0) return null;
            int q = IndexOf(data, end, p);
            if (q < 0) return null;
            q += end.Length;
            return Encoding.UTF8.GetString(data, p, q - p);
        }

        private static byte[] ReplaceSerializedString(byte[] src, string oldText, string newText)
        {
            if (string.IsNullOrEmpty(oldText) || newText == null) return src;
            byte[] oldB = Encoding.UTF8.GetBytes(oldText);
            byte[] newB = Encoding.UTF8.GetBytes(newText);
            int search = 0;

            while (true)
            {
                int p = IndexOf(src, oldB, search);
                if (p < 0) break;

                int prefixStart = -1;
                int prefixLen = 0;
                for (int k = 1; k <= 5 && p - k >= 0; k++)
                {
                    int val, used;
                    if (TryRead7Bit(src, p - k, out val, out used) && used == k && val == oldB.Length)
                    {
                        prefixStart = p - k;
                        prefixLen = k;
                        break;
                    }
                }

                if (prefixStart >= 0)
                {
                    byte[] npre = Encode7Bit(newB.Length);
                    var dst = new byte[src.Length - prefixLen - oldB.Length + npre.Length + newB.Length];
                    Buffer.BlockCopy(src, 0, dst, 0, prefixStart);
                    Buffer.BlockCopy(npre, 0, dst, prefixStart, npre.Length);
                    Buffer.BlockCopy(newB, 0, dst, prefixStart + npre.Length, newB.Length);

                    int oldTail = p + oldB.Length;
                    int newTail = prefixStart + npre.Length + newB.Length;
                    Buffer.BlockCopy(src, oldTail, dst, newTail, src.Length - oldTail);
                    src = dst;
                    search = newTail;
                }
                else if (oldB.Length == newB.Length)
                {
                    Buffer.BlockCopy(newB, 0, src, p, newB.Length);
                    search = p + newB.Length;
                }
                else
                {
                    search = p + oldB.Length;
                }
            }
            return src;
        }

        private static bool TryRead7Bit(byte[] b, int p, out int value, out int used)
        {
            value = 0; used = 0; int shift = 0;
            for (int i = 0; i < 5 && p + i < b.Length; i++)
            {
                byte x = b[p + i];
                value |= (x & 0x7F) << shift;
                used++;
                if ((x & 0x80) == 0) return true;
                shift += 7;
            }
            return false;
        }

        private static byte[] Encode7Bit(int value)
        {
            var l = new List<byte>();
            uint v = (uint)value;
            while (v >= 0x80)
            {
                l.Add((byte)(v | 0x80));
                v >>= 7;
            }
            l.Add((byte)v);
            return l.ToArray();
        }

        private static int IndexOf(byte[] data, byte[] needle, int start)
        {
            for (int i = start; i <= data.Length - needle.Length; i++)
            {
                int j = 0;
                for (; j < needle.Length; j++) if (data[i + j] != needle[j]) break;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}