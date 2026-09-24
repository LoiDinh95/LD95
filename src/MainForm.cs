using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace LDElitechReader
{
    public sealed class MainForm : Form
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        private readonly Label lblDetected = new Label();
        private readonly Label lblSerial = new Label();
        private readonly Label lblCalib = new Label();
        private readonly Button btnRead = new Button();
        private readonly Button btnOpen = new Button();
        private readonly Button btnZoomIn = new Button();
        private readonly Button btnZoomOut = new Button();
        private readonly Button btnClear = new Button();
        private readonly Button btnExit = new Button();
        private readonly PictureBox devicePicture = new PictureBox();
        private readonly Label deviceCaption = new Label();
        private readonly DataGridView infoGrid = new DataGridView();
        private readonly DataGridView recordGrid = new DataGridView();
        private readonly Chart chart = new Chart();
        private readonly SplitContainer splitOuter = new SplitContainer();
        private readonly SplitContainer splitInner = new SplitContainer();
        private readonly Label zone1Title = new Label();
        private readonly Label zone2Title = new Label();
        private readonly Label zone3Title = new Label();
        private readonly List<string> diag = new List<string>();

        private DeviceData current;
        private bool busy;
        private CancellationTokenSource deviceDebounce;
        private double zoom = 0.80;
        private double split1Ratio = 0.35;
        private double split2Ratio = 0.3846153846; // 25/(25+40)
        private readonly string appDir = AppDomain.CurrentDomain.BaseDirectory;
        private readonly string iniPath;

        public MainForm()
        {
            iniPath = Path.Combine(appDir, "LD_Elitech_Reader.ini");
            Text = "LD Elitech Reader v0.8 - C# - AUTO RC-4 / RC-5+ READ ONLY";
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            MinimumSize = new Size(1100, 650);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            BuildUi();
            LoadSettings();

            Shown += async (s, e) =>
            {
                ApplySavedLayout();
                await Task.Delay(500);
                StartRead(false);
            };
            FormClosing += (s, e) => SaveSettings();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Color.White };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            root.Controls.Add(new Label {
                Dock = DockStyle.Fill,
                Text = "LD ELITECH READER v0.8  |  C#  |  AUTO RC-4 / RC-5+  |  READ ONLY",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14,0,0,0),
                BackColor = Color.FromArgb(247,248,250)
            }, 0, 0);

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(14,5,8,4), BackColor = Color.White };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            root.Controls.Add(top, 0, 1);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            top.Controls.Add(left,0,0);

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            toolbar.Controls.Add(TopLabel("Thiết bị:", true));
            lblDetected.Text = "Chưa nhận dạng"; lblDetected.AutoSize = true; lblDetected.Margin = new Padding(5,9,14,0); toolbar.Controls.Add(lblDetected);
            SetupButton(btnRead, "Đọc thiết bị", 124, (s,e)=>StartRead(true)); toolbar.Controls.Add(btnRead);
            SetupButton(btnOpen, "Mở thư mục", 114, (s,e)=>OpenFolder()); toolbar.Controls.Add(btnOpen);
            SetupButton(btnZoomIn, "Zoom +", 82, (s,e)=>SetZoom(zoom+.10)); toolbar.Controls.Add(btnZoomIn);
            SetupButton(btnZoomOut, "Zoom -", 82, (s,e)=>SetZoom(zoom-.10)); toolbar.Controls.Add(btnZoomOut);
            SetupButton(btnClear, "Xóa log", 82, (s,e)=>ClearView()); toolbar.Controls.Add(btnClear);
            SetupButton(btnExit, "Thoát", 76, (s,e)=>Close()); toolbar.Controls.Add(btnExit);
            left.Controls.Add(toolbar,0,0);

            var details = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            details.Controls.Add(TopLabel("Serial:", true));
            lblSerial.Text="-"; lblSerial.AutoSize=true; lblSerial.Margin=new Padding(5,7,24,0); details.Controls.Add(lblSerial);
            details.Controls.Add(TopLabel("Calib. Certificate:", true));
            lblCalib.Text="-"; lblCalib.AutoSize=true; lblCalib.Margin=new Padding(5,7,0,0); details.Controls.Add(lblCalib);
            left.Controls.Add(details,0,1);

            var imagePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(0) };
            deviceCaption.Dock = DockStyle.Bottom; deviceCaption.Height=19; deviceCaption.TextAlign=ContentAlignment.MiddleCenter; deviceCaption.Font=new Font("Segoe UI",8.5F,FontStyle.Bold); deviceCaption.Visible=false;
            devicePicture.Dock=DockStyle.Fill; devicePicture.SizeMode=PictureBoxSizeMode.Zoom; devicePicture.BackColor=Color.White; devicePicture.Visible=false;
            imagePanel.Controls.Add(devicePicture); imagePanel.Controls.Add(deviceCaption);
            top.Controls.Add(imagePanel,1,0);

            var results = new Panel { Dock=DockStyle.Fill, BackColor=Color.White, Padding=new Padding(4,0,4,4) };
            root.Controls.Add(results,0,2);

            splitOuter.Dock=DockStyle.Fill; splitOuter.Orientation=Orientation.Vertical; splitOuter.SplitterWidth=6; splitOuter.BackColor=Color.FromArgb(225,228,232);
            splitInner.Dock=DockStyle.Fill; splitInner.Orientation=Orientation.Vertical; splitInner.SplitterWidth=6; splitInner.BackColor=Color.FromArgb(225,228,232);
            results.Controls.Add(splitOuter); splitOuter.Panel2.Controls.Add(splitInner);

            BuildZone(splitOuter.Panel1, zone1Title, "THÔNG TIN THIẾT BỊ", infoGrid);
            BuildZone(splitInner.Panel1, zone2Title, "DỮ LIỆU GHI", recordGrid);
            BuildChartZone(splitInner.Panel2);
            ConfigureInfoGrid();
            ConfigureRecordGrid();
            ConfigureChart();
        }

        private static Label TopLabel(string t, bool bold)
        {
            return new Label { Text=t, AutoSize=true, Font=new Font("Segoe UI",9.5F,bold?FontStyle.Bold:FontStyle.Regular), Margin=new Padding(0,7,0,0) };
        }

        private static void SetupButton(Button b, string text, int width, EventHandler click)
        {
            b.Text=text; b.Width=width; b.Height=34; b.Margin=new Padding(4,1,4,0); b.FlatStyle=FlatStyle.System; b.Click+=click;
        }

        private static void BuildZone(Control host, Label title, string text, Control content)
        {
            var t=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,BackColor=Color.White,Padding=new Padding(2)};
            t.RowStyles.Add(new RowStyle(SizeType.Absolute,29)); t.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            title.Text=text; title.Dock=DockStyle.Fill; title.TextAlign=ContentAlignment.MiddleLeft; title.Font=new Font("Segoe UI",10F,FontStyle.Bold); title.Padding=new Padding(3,0,0,0);
            content.Dock=DockStyle.Fill; t.Controls.Add(title,0,0); t.Controls.Add(content,0,1); host.Controls.Add(t);
        }

        private void BuildChartZone(Control host)
        {
            var t=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,BackColor=Color.White,Padding=new Padding(2)};
            t.RowStyles.Add(new RowStyle(SizeType.Absolute,29)); t.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            zone3Title.Text="BIỂU ĐỒ NHIỆT ĐỘ"; zone3Title.Dock=DockStyle.Fill; zone3Title.TextAlign=ContentAlignment.MiddleLeft; zone3Title.Font=new Font("Segoe UI",10F,FontStyle.Bold); zone3Title.Padding=new Padding(3,0,0,0);
            chart.Dock=DockStyle.Fill; t.Controls.Add(zone3Title,0,0); t.Controls.Add(chart,0,1); host.Controls.Add(t);
        }

        private void ConfigureInfoGrid()
        {
            infoGrid.AllowUserToAddRows=false; infoGrid.AllowUserToDeleteRows=false; infoGrid.ReadOnly=true; infoGrid.RowHeadersVisible=false; infoGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect; infoGrid.MultiSelect=false;
            infoGrid.BackgroundColor=Color.White; infoGrid.BorderStyle=BorderStyle.FixedSingle; infoGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            infoGrid.Columns.Add("Info","Thông tin"); infoGrid.Columns.Add("Value","Giá trị"); infoGrid.Columns[0].FillWeight=58; infoGrid.Columns[1].FillWeight=42;
            infoGrid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(228,239,252); infoGrid.DefaultCellStyle.SelectionForeColor=Color.Black;
        }

        private void ConfigureRecordGrid()
        {
            recordGrid.AllowUserToAddRows=false; recordGrid.AllowUserToDeleteRows=false; recordGrid.ReadOnly=true; recordGrid.RowHeadersVisible=false; recordGrid.VirtualMode=true; recordGrid.BackgroundColor=Color.White; recordGrid.BorderStyle=BorderStyle.FixedSingle;
            recordGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect; recordGrid.MultiSelect=false;
            recordGrid.Columns.Add("No","STT"); recordGrid.Columns.Add("Time","Thời gian"); recordGrid.Columns.Add("Temp","Nhiệt độ (°C)");
            recordGrid.Columns[0].AutoSizeMode=DataGridViewAutoSizeColumnMode.AllCells; recordGrid.Columns[1].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill; recordGrid.Columns[2].AutoSizeMode=DataGridViewAutoSizeColumnMode.AllCells;
            recordGrid.CellValueNeeded += (s,e) => {
                if(current==null||e.RowIndex<0||e.RowIndex>=current.Records.Count)return;
                var r=current.Records[e.RowIndex];
                if(e.ColumnIndex==0)e.Value=r.No;
                else if(e.ColumnIndex==1)e.Value=r.Time.ToString("yyyy-MM-dd HH:mm:ss");
                else if(e.ColumnIndex==2)e.Value=r.Temperature.ToString("0.0",CultureInfo.InvariantCulture);
            };
        }

        private void ConfigureChart()
        {
            chart.BackColor=Color.White;
            var ca=new ChartArea("Temperature");
            ca.BackColor=Color.White; ca.AxisX.MajorGrid.LineColor=Color.FromArgb(225,229,235); ca.AxisY.MajorGrid.LineColor=Color.FromArgb(225,229,235);
            ca.AxisX.LabelStyle.Format="HH:mm\ndd/MM"; ca.AxisY.Title="Nhiệt độ (°C)";
            chart.ChartAreas.Add(ca);
            chart.Series.Add(new Series("Nhiệt độ (°C)") { ChartType=SeriesChartType.FastLine, XValueType=ChartValueType.DateTime, BorderWidth=1, Color=Color.Red });
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            int change=(int)m.WParam;
            if(m.Msg==WM_DEVICECHANGE && (change==DBT_DEVICEARRIVAL || change==DBT_DEVICEREMOVECOMPLETE))
            {
                deviceDebounce?.Cancel();
                var cts=new CancellationTokenSource(); deviceDebounce=cts;
                Task.Run(async()=> {
                    try{await Task.Delay(900,cts.Token);}catch{return;}
                    if(cts.IsCancellationRequested||IsDisposed)return;
                    BeginInvoke((Action)(()=> {
                        if(change==DBT_DEVICEREMOVECOMPLETE && !busy) ClearRecognizedOnly();
                        else StartRead(false);
                    }));
                });
            }
        }

        private void StartRead(bool userInitiated)
        {
            if(busy)return;
            busy=true; SetBusy(true); lock(diag)diag.Clear(); Log("Start read");
            Task.Run(()=>DeviceReaders.ReadAuto(Log)).ContinueWith(t=>BeginInvoke((Action)(()=>{
                try{
                    if(t.IsFaulted){
                        ClearRecognizedOnly();
                        string msg=t.Exception?.GetBaseException().Message??"Không đọc được thiết bị";
                        Log("ERROR: "+msg);
                        if(userInitiated && !msg.Contains("Không tìm thấy") && !msg.Contains("Khong tim thay")) MessageBox.Show(msg,"LD Elitech Reader",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                    }else{
                        current=t.Result; DisplayData(current); ExportAll(current);
                    }
                }finally{busy=false;SetBusy(false);}
            })));
        }

        private void SetBusy(bool x)
        {
            btnRead.Enabled=!x; btnOpen.Enabled=!x; btnZoomIn.Enabled=!x; btnZoomOut.Enabled=!x; btnClear.Enabled=!x; Cursor=x?Cursors.WaitCursor:Cursors.Default;
        }

        private void DisplayData(DeviceData d)
        {
            lblDetected.Text="Đã nhận dạng "+d.Model+"  |  "+d.Transport; lblSerial.Text=d.Serial; lblCalib.Text=d.CalibCertificate;
            zone2Title.Text="DỮ LIỆU GHI  ("+d.Model+" - "+d.Serial+")"; ShowDeviceImage(d.Model);
            infoGrid.Rows.Clear(); foreach(var kv in BuildInfoRows(d))infoGrid.Rows.Add(kv.Key,kv.Value);
            recordGrid.RowCount=d.Records.Count; recordGrid.Invalidate();
            chart.Series[0].Points.Clear(); foreach(var r in d.Records)chart.Series[0].Points.AddXY(r.Time,r.Temperature);
            if(d.Records.Count>0){
                double min=d.Minimum,max=d.Maximum,pad=Math.Max(.5,(max-min)*.12);
                chart.ChartAreas[0].AxisY.Minimum=Math.Floor((min-pad)*2)/2; chart.ChartAreas[0].AxisY.Maximum=Math.Ceiling((max+pad)*2)/2;
            }
        }

        private IEnumerable<KeyValuePair<string,string>> BuildInfoRows(DeviceData d)
        {
            yield return KV("Device Model",d.Model); yield return KV("Serial Number",d.Serial); yield return KV("Calib. Certificate",d.CalibCertificate);
            yield return KV("Sensor Type",d.SensorType); yield return KV("Firmware Version",d.Firmware); yield return KV("Start Mode",d.StartMode);
            yield return KV("Logging Interval",FormatIntervalClock(d.Interval)); yield return KV("Start Delay",d.StartDelay); yield return KV("Repeat Start",d.RepeatStart);
            yield return KV("Time Zone",d.TimeZone); yield return KV("Stop Mode",d.StopMode); yield return KV("Storage Mode",d.StorageMode);
            yield return KV("Audible Alarm",d.AudibleAlarm); yield return KV("Notification Tone",d.NotificationTone); yield return KV("Trip Number",d.TripNumber);
            yield return KV("Trip Description",d.TripDescription); yield return KV("Maximum (Temperature)",F1(d.Maximum)+" °C"); yield return KV("Minimum (Temperature)",F1(d.Minimum)+" °C");
            yield return KV("Average (Temperature)",F1(d.Average)+" °C"); yield return KV("Mean Kinetic Temperature (MKT)",F1(d.Mkt)+" °C");
            yield return KV("First Reading",DT(d.FirstReading)); yield return KV("Last Reading",DT(d.LastReading)); yield return KV("Data Points",d.DataPoints.ToString());
            yield return KV("Recorded Period",FormatDuration(d.RecordedPeriod));
        }

        private static KeyValuePair<string,string> KV(string a,string b){return new KeyValuePair<string,string>(a,b??"N/A");}
        private static string F1(double x){return double.IsNaN(x)?"N/A":x.ToString("0.0",CultureInfo.InvariantCulture);}
        private static string DT(DateTime? x){return x.HasValue?x.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A";}
        private static string FormatIntervalClock(TimeSpan t){return t==TimeSpan.Zero?"N/A":string.Format("{0}:{1:00}:{2:00}",(int)t.TotalHours,t.Minutes,t.Seconds);}
        public static string FormatDuration(TimeSpan t){return t.TotalDays>=1?string.Format("{0}D {1}H {2}M {3}S",(int)t.TotalDays,t.Hours,t.Minutes,t.Seconds):string.Format("{0}H {1}M {2}S",(int)t.TotalHours,t.Minutes,t.Seconds);}

        private void ShowDeviceImage(string model)
        {
            string file=model.StartsWith("RC-5",StringComparison.OrdinalIgnoreCase)?"rc5.png":"rc4.png";
            string p=Path.Combine(appDir,"assets",file);
            try{
                if(devicePicture.Image!=null){var old=devicePicture.Image;devicePicture.Image=null;old.Dispose();}
                if(File.Exists(p)){using(var tmp=Image.FromFile(p))devicePicture.Image=new Bitmap(tmp);devicePicture.Visible=true;deviceCaption.Text=model;deviceCaption.Visible=true;}
                else{devicePicture.Visible=false;deviceCaption.Visible=false;}
            }catch{devicePicture.Visible=false;deviceCaption.Visible=false;}
        }

        private void ClearRecognizedOnly(){lblDetected.Text="Chưa nhận dạng";lblSerial.Text="-";lblCalib.Text="-";devicePicture.Visible=false;deviceCaption.Visible=false;}
        private void ClearView(){current=null;infoGrid.Rows.Clear();recordGrid.RowCount=0;chart.Series[0].Points.Clear();zone2Title.Text="DỮ LIỆU GHI";ClearRecognizedOnly();}

        private void ExportAll(DeviceData d)
        {
            string dl=DlMapping.GetOrAssign(appDir,d.Serial,d.Model),stem=dl+d.Serial;
            string csv=Path.Combine(appDir,stem+".csv"),pdf=Path.Combine(appDir,stem+".pdf"),elt=Path.Combine(appDir,stem+".elt"),diagnostic=Path.Combine(appDir,stem+"_diagnostic.txt");
            CsvExporter.Write(csv,d); ElitechPdfExporter.Write(pdf,d); EltExporter.Write(elt,d,appDir);
            lock(diag)File.WriteAllLines(diagnostic,diag.ToArray(),Encoding.UTF8);
            Log("Exported "+Path.GetFileName(elt)+" + "+Path.GetFileName(pdf));
        }

        private void OpenFolder(){try{System.Diagnostics.Process.Start("explorer.exe",appDir);}catch(Exception ex){MessageBox.Show(ex.Message);}}
        private void Log(string s){lock(diag)diag.Add(DateTime.Now.ToString("HH:mm:ss.fff")+" "+s);}

        private void SetZoom(double v){zoom=Math.Max(.60,Math.Min(1.60,Math.Round(v,2)));ApplyZoom();}
        private void ApplyZoom()
        {
            float f=(float)(10.0*zoom); var normal=new Font("Segoe UI",f,FontStyle.Regular); var bold=new Font("Segoe UI",Math.Max(7,f+1),FontStyle.Bold);
            infoGrid.Font=normal;recordGrid.Font=normal;infoGrid.ColumnHeadersDefaultCellStyle.Font=bold;recordGrid.ColumnHeadersDefaultCellStyle.Font=bold;
            infoGrid.RowTemplate.Height=Math.Max(18,(int)(24*zoom));recordGrid.RowTemplate.Height=Math.Max(18,(int)(24*zoom));
            infoGrid.ColumnHeadersHeight=Math.Max(21,(int)(27*zoom));recordGrid.ColumnHeadersHeight=Math.Max(21,(int)(27*zoom));
            zone1Title.Font=bold;zone2Title.Font=bold;zone3Title.Font=bold;
            chart.ChartAreas[0].AxisX.LabelStyle.Font=new Font("Segoe UI",Math.Max(6,(float)(8*zoom)));
            chart.ChartAreas[0].AxisY.LabelStyle.Font=new Font("Segoe UI",Math.Max(6,(float)(8*zoom)));
            chart.ChartAreas[0].AxisY.TitleFont=new Font("Segoe UI",Math.Max(6,(float)(8*zoom)));
        }

        private void ApplySavedLayout()
        {
            try{
                int w=splitOuter.ClientSize.Width;if(w>100)splitOuter.SplitterDistance=Math.Max(80,Math.Min(w-120,(int)(w*split1Ratio)));
                int w2=splitInner.ClientSize.Width;if(w2>100)splitInner.SplitterDistance=Math.Max(80,Math.Min(w2-120,(int)(w2*split2Ratio)));
            }catch{}
            ApplyZoom();
        }

        private void LoadSettings()
        {
            try{
                if(!File.Exists(iniPath))return;
                foreach(string line in File.ReadAllLines(iniPath)){
                    string[] p=line.Split(new[]{'='},2); if(p.Length!=2)continue; double x;
                    if(!double.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out x))continue;
                    if(p[0]=="Split1")split1Ratio=x;else if(p[0]=="Split2")split2Ratio=x;else if(p[0]=="Zoom")zoom=x;
                }
            }catch{}
        }

        private void SaveSettings()
        {
            try{
                if(splitOuter.ClientSize.Width>0)split1Ratio=splitOuter.SplitterDistance/(double)splitOuter.ClientSize.Width;
                if(splitInner.ClientSize.Width>0)split2Ratio=splitInner.SplitterDistance/(double)splitInner.ClientSize.Width;
                File.WriteAllLines(iniPath,new[]{"Split1="+split1Ratio.ToString(CultureInfo.InvariantCulture),"Split2="+split2Ratio.ToString(CultureInfo.InvariantCulture),"Zoom="+zoom.ToString(CultureInfo.InvariantCulture)});
            }catch{}
        }
    }

    public static class CsvExporter
    {
        public static void Write(string path, DeviceData d)
        {
            using(var sw=new StreamWriter(path,false,new UTF8Encoding(true))){
                sw.WriteLine("No,Time,Temperature_C");
                foreach(var r in d.Records)sw.WriteLine(r.No+","+r.Time.ToString("yyyy-MM-dd HH:mm:ss")+","+r.Temperature.ToString("0.0",CultureInfo.InvariantCulture));
            }
        }
    }

    public static class DlMapping
    {
        public static string GetOrAssign(string dir,string serial,string model)
        {
            string p=Path.Combine(dir,"DL_Mapping.csv");var rows=new List<string[]>();
            if(File.Exists(p))foreach(var line in File.ReadAllLines(p)){var x=line.Split(',');if(x.Length>=2)rows.Add(x);}
            var hit=rows.FirstOrDefault(x=>x.Length>=2&&string.Equals(x[1],serial,StringComparison.OrdinalIgnoreCase));if(hit!=null)return hit[0];
            int max=0;foreach(var x in rows){int n;if(x.Length>0&&x[0].StartsWith("DL")&&int.TryParse(x[0].Substring(2),out n))max=Math.Max(max,n);}
            string dl="DL"+(max+1).ToString("00");File.AppendAllText(p,dl+","+serial+","+model+","+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+Environment.NewLine,Encoding.UTF8);return dl;
        }
    }
}