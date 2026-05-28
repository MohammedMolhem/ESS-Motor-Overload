using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace EES_MotorOverload_V1
{
    public class MyChartClass : Chart, IDisposable
    {
        private Timer _dataTimer;
        private Stopwatch _stopwatch;
        private Random _random;
        private Series _dataSeries;
        private double _currentX = 0;
        private double _signalPhase = 0;
        private bool _disposed = false;

        // When true, the internal timer generates demo data.
        // When false (default now), only external data is displayed.
        private bool _demoMode = false;

        private const int MAX_POINTS = 4096;
        private double _xMin = 0;
        private double _xMax = 2500;  // Default: 0..2500 Hz (half of 5000 Hz sample rate)
        private double _yMin = -100;
        private double _yMax = 0;
        private const int TIMER_INTERVAL_MS = 100;

        public enum SignalType
        {
            SineWave, SquareWave, TriangleWave, Pulse, Noise, Decaying
        }

        public bool IsRunning => _dataTimer?.Enabled ?? false;

        /// <summary>
        /// Display name used in the chart title area (e.g. "Fourier Spectrum").
        /// </summary>
        public string PhaseName
        {
            get { return _phaseName; }
            set
            {
                _phaseName = value;
                ApplyPhaseName();
            }
        }
        private string _phaseName = "";

        public SignalType CurrentSignalType { get; set; } = SignalType.Noise;

        public MyChartClass()
        {
            try
            {
                _random = new Random();
                _stopwatch = new Stopwatch();
                InitializeChart();
                InitializeTimer();
                _stopwatch.Start();
            }
            catch (Exception ex)
            {
                Logger.Error($"MyChartClass constructor error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Changes the series line color.
        /// </summary>
        public void SetSeriesColor(Color color)
        {
            if (_dataSeries != null)
                _dataSeries.Color = color;
        }

        /// <summary>
        /// Sets the X-axis range (frequency range in Hz).
        /// Call before adding data points.
        /// </summary>
        public void SetXRange(double min, double max)
        {
            _xMin = min;
            _xMax = max;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<double, double>(SetXRange), min, max);
                return;
            }
            try
            {
                if (this.ChartAreas.Count > 0)
                {
                    var ca = this.ChartAreas[0];
                    ca.AxisX.Minimum = _xMin;
                    ca.AxisX.Maximum = _xMax;
                }
            }
            catch { }
        }

        /// <summary>
        /// Sets the Y-axis range (amplitude range in dB).
        /// </summary>
        public void SetYRange(double min, double max)
        {
            _yMin = min;
            _yMax = max;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<double, double>(SetYRange), min, max);
                return;
            }
            try
            {
                if (this.ChartAreas.Count > 0)
                {
                    var ca = this.ChartAreas[0];
                    ca.AxisY.Minimum = _yMin;
                    ca.AxisY.Maximum = _yMax;
                }
            }
            catch { }
        }

        /// <summary>
        /// Enables demo mode (internal timer generates synthetic signals).
        /// </summary>
        public void EnableDemoMode(bool enable)
        {
            _demoMode = enable;
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException("MyChartClass");
            if (_dataTimer != null && !_dataTimer.Enabled)
                _dataTimer.Start();
        }

        public void Stop()
        {
            if (_dataTimer != null && _dataTimer.Enabled)
                _dataTimer.Stop();
        }

        public void ClearData()
        {
            if (_disposed) throw new ObjectDisposedException("MyChartClass");
            if (InvokeRequired) { BeginInvoke(new Action(ClearData)); return; }
            try
            {
                _dataSeries?.Points.Clear();
                _currentX = _xMin;
                _signalPhase = 0;
                if (this.ChartAreas.Count > 0)
                {
                    var ca = this.ChartAreas[0];
                    ca.AxisX.Minimum = _xMin;
                    ca.AxisX.Maximum = _xMax;
                    ca.AxisY.Minimum = _yMin;
                    ca.AxisY.Maximum = _yMax;
                    ca.AxisX.ScaleView.ZoomReset();
                    ca.AxisY.ScaleView.ZoomReset();
                }
            }
            catch (Exception ex) { Logger.Error($"ClearData error: {ex.Message}", ex); }
        }

        public void AddExternalData(double xValue, double yValue)
        {
            if (_disposed) throw new ObjectDisposedException("MyChartClass");
            if (InvokeRequired)
            {
                BeginInvoke(new Action<double, double>(AddExternalData), xValue, yValue);
                return;
            }
            try
            {
                // Clamp to current axis range
                yValue = Math.Max(_yMin, Math.Min(_yMax, yValue));
                AddDataPoint(xValue, yValue);
            }
            catch (Exception ex) { Logger.Error($"AddExternalData error: {ex.Message}", ex); }
        }

        public void SetUpdateInterval(int milliseconds)
        {
            if (_dataTimer != null && milliseconds > 0)
                _dataTimer.Interval = milliseconds;
        }

        public void ResetToDefaultView()
        {
            if (InvokeRequired) { BeginInvoke(new Action(ResetToDefaultView)); return; }
            try
            {
                if (this.ChartAreas.Count > 0)
                {
                    var ca = this.ChartAreas[0];
                    ca.AxisX.ScaleView.ZoomReset();
                    ca.AxisY.ScaleView.ZoomReset();
                }
            }
            catch (Exception ex) { Logger.Error($"ResetToDefaultView error: {ex.Message}", ex); }
        }

        private void InitializeChart()
        {
            this.Series.Clear();
            this.ChartAreas.Clear();

            ChartArea chartArea = new ChartArea("SpectralData") { BackColor = Color.White };

            chartArea.AxisX.Title = "Frequency (Hz)";
            chartArea.AxisX.TitleFont = new Font("Tahoma", 8F);
            chartArea.AxisX.Minimum = _xMin;
            chartArea.AxisX.Maximum = _xMax;
            chartArea.AxisX.MajorGrid.Enabled = true;
            chartArea.AxisX.MajorGrid.LineColor = Color.LightGray;
            chartArea.AxisX.LabelStyle.Font = new Font("Tahoma", 7F);

            chartArea.AxisY.Title = "Amplitude (dB)";
            chartArea.AxisY.TitleFont = new Font("Tahoma", 8F);
            chartArea.AxisY.Minimum = _yMin;
            chartArea.AxisY.Maximum = _yMax;
            chartArea.AxisY.MajorGrid.Enabled = true;
            chartArea.AxisY.MajorGrid.LineColor = Color.LightGray;
            chartArea.AxisY.LabelStyle.Font = new Font("Tahoma", 7F);

            chartArea.CursorX.IsUserEnabled = true;
            chartArea.CursorX.IsUserSelectionEnabled = true;
            chartArea.CursorY.IsUserEnabled = true;
            chartArea.CursorY.IsUserSelectionEnabled = true;
            chartArea.AxisX.ScaleView.Zoomable = true;
            chartArea.AxisY.ScaleView.Zoomable = true;

            chartArea.InnerPlotPosition = new ElementPosition(12, 8, 85, 78);

            this.ChartAreas.Add(chartArea);

            _dataSeries = new Series("Signal")
            {
                ChartType = SeriesChartType.FastLine,
                Color = Color.Blue,
                BorderWidth = 2,
                MarkerStyle = MarkerStyle.None
            };
            this.Series.Add(_dataSeries);

            this.AntiAliasing = AntiAliasingStyles.None;
            this.TextAntiAliasingQuality = TextAntiAliasingQuality.Normal;

            ApplyPhaseName();
        }

        private void ApplyPhaseName()
        {
            if (this.Titles.Count > 0)
                this.Titles[0].Text = _phaseName ?? "";
            else if (!string.IsNullOrEmpty(_phaseName))
            {
                Title t = new Title(_phaseName)
                {
                    Font = new Font("Tahoma", 9F, FontStyle.Bold),
                    Docking = Docking.Top,
                    Alignment = ContentAlignment.MiddleLeft
                };
                this.Titles.Add(t);
            }
        }

        private void InitializeTimer()
        {
            _dataTimer = new Timer { Interval = TIMER_INTERVAL_MS };
            _dataTimer.Tick += (s, e) =>
            {
                // Only generate demo data if demo mode is enabled
                if (_demoMode)
                    GenerateAndAddDataPoint();
            };
        }

        private void GenerateAndAddDataPoint()
        {
            if (InvokeRequired) { BeginInvoke(new Action(GenerateAndAddDataPoint)); return; }
            try
            {
                double yValue = GenerateSignal();
                AddDataPoint(_currentX, yValue);
                _currentX += (_xMax - _xMin) / 1000;
                if (_currentX > _xMax)
                {
                    _dataSeries?.Points.Clear();
                    _currentX = _xMin;
                }
            }
            catch (Exception ex) { Logger.Error($"GenerateAndAddDataPoint error: {ex.Message}", ex); }
        }

        private double GenerateSignal()
        {
            double time = _stopwatch.Elapsed.TotalSeconds;
            _signalPhase += 0.1;
            return Math.Exp(-time * 0.1) * Math.Sin(_signalPhase * 2) * 30 - 50;
        }

        private void AddDataPoint(double xValue, double yValue)
        {
            if (_dataSeries == null) return;
            yValue = Math.Max(_yMin, Math.Min(_yMax, yValue));
            try
            {
                _dataSeries.Points.AddXY(xValue, yValue);
                if (_dataSeries.Points.Count > MAX_POINTS)
                    _dataSeries.Points.RemoveAt(0);
            }
            catch (Exception ex) { Logger.Error($"AddDataPoint error: {ex.Message}", ex); }
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                { try { Stop(); _dataTimer?.Dispose(); _stopwatch?.Stop(); } catch { } }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        ~MyChartClass() { Dispose(false); }
    }
}