using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LDElitechReader
{
    public sealed class LogRecord
    {
        public int No { get; set; }
        public DateTime Time { get; set; }
        public double Temperature { get; set; }
        public bool Mark { get; set; }
        public bool Pause { get; set; }
        public bool Stop { get; set; }
    }

    public sealed class DeviceData
    {
        public string Model = "N/A";
        public string Serial = "N/A";
        public string Transport = "N/A";
        public string Port = "";
        public string SensorType = "N/A";
        public string Firmware = "N/A";
        public string Protocol = "N/A";
        public string StartMode = "N/A";
        public TimeSpan Interval = TimeSpan.Zero;
        public string StartDelay = "N/A";
        public string RepeatStart = "N/A";
        public string TimeZone = "N/A";
        public string StopMode = "N/A";
        public string StorageMode = "N/A";
        public string AudibleAlarm = "N/A";
        public string NotificationTone = "N/A";
        public string TripNumber = "N/A";
        public string TripDescription = "N/A";
        public string CalibCertificate = "N/A";
        public double? HighLimit;
        public double? LowLimit;
        public int Capacity;
        public DateTime? StartTime;
        public DateTime? StopTime;
        public List<LogRecord> Records = new List<LogRecord>();

        public int DataPoints { get { return Records.Count; } }
        public DateTime? FirstReading { get { return Records.Count == 0 ? (DateTime?)null : Records[0].Time; } }
        public DateTime? LastReading { get { return Records.Count == 0 ? (DateTime?)null : Records[Records.Count - 1].Time; } }
        public double Maximum { get { return Records.Count == 0 ? double.NaN : Records.Max(r => r.Temperature); } }
        public double Minimum { get { return Records.Count == 0 ? double.NaN : Records.Min(r => r.Temperature); } }
        public double Average { get { return Records.Count == 0 ? double.NaN : Records.Average(r => r.Temperature); } }
        public TimeSpan RecordedPeriod { get { return Records.Count < 2 ? TimeSpan.Zero : Records[Records.Count - 1].Time - Records[0].Time; } }

        public bool HasAlarm
        {
            get
            {
                return Records.Any(r =>
                    (HighLimit.HasValue && r.Temperature > HighLimit.Value) ||
                    (LowLimit.HasValue && r.Temperature < LowLimit.Value));
            }
        }

        public DateTime? FirstAlarm
        {
            get
            {
                var r = Records.FirstOrDefault(x =>
                    (HighLimit.HasValue && x.Temperature > HighLimit.Value) ||
                    (LowLimit.HasValue && x.Temperature < LowLimit.Value));
                return r == null ? (DateTime?)null : r.Time;
            }
        }

        public double Mkt
        {
            get
            {
                if (Records.Count == 0) return double.NaN;
                const double dh = 83.144;
                const double gas = 0.0083144;
                double s = 0;
                foreach (var r in Records)
                {
                    double k = r.Temperature + 273.15;
                    s += Math.Exp(-dh / (gas * k));
                }
                return (dh / gas) / (-Math.Log(s / Records.Count)) - 273.15;
            }
        }

        public string IntervalText
        {
            get
            {
                if (Interval == TimeSpan.Zero) return "N/A";
                if (Interval.TotalDays >= 1) return ((int)Interval.TotalDays).ToString(CultureInfo.InvariantCulture) + "d";
                if (Interval.Seconds == 0 && Interval.Minutes == 0) return ((int)Interval.TotalHours).ToString(CultureInfo.InvariantCulture) + "h";
                if (Interval.Seconds == 0) return ((int)Interval.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m";
                return ((int)Interval.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
            }
        }
    }
}