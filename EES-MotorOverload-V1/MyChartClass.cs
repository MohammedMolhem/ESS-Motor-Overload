using System;
using System.Collections.Generic;
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
        private Series _phase2Series;
        private Series _phase3Series;
        private Series _peakSeries;
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
                    double span = Math.Max(1.0, _xMax - _xMin);
                    ca.AxisX.Interval = span / 10.0;
                    ca.AxisX.MinorGrid.Interval = ca.AxisX.Interval / 2.0;
                    ca.AxisX.MajorGrid.Enabled = true;
                    ca.AxisX.MinorGrid.Enabled = true;
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
                    double ySpan = Math.Max(1.0, _yMax - _yMin);
                    ca.AxisY.Interval = ySpan / 10.0;
                    ca.AxisY.MinorGrid.Interval = ca.AxisY.Interval / 2.0;
                    ca.AxisY.MajorGrid.Enabled = true;
                    ca.AxisY.MinorGrid.Enabled = true;
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
                _phase2Series?.Points.Clear();
                _phase3Series?.Points.Clear();
                _peakSeries?.Points.Clear();
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

        /// <summary>
        /// Plots three phase spectra on one engineering figure.
        /// Input points are expected to already be in dB scale.
        /// </summary>
        public void SetThreePhaseDbData(IList<PointF> phase1, IList<PointF> phase2, IList<PointF> phase3)
        {
            if (_disposed) throw new ObjectDisposedException("MyChartClass");
            if (InvokeRequired)
            {
                BeginInvoke(new Action<IList<PointF>, IList<PointF>, IList<PointF>>(SetThreePhaseDbData), phase1, phase2, phase3);
                return;
            }

            try
            {
                _dataSeries?.Points.Clear();
                _phase2Series?.Points.Clear();
                _phase3Series?.Points.Clear();

                AddDbPointsToSeries(_dataSeries, phase1);
                AddDbPointsToSeries(_phase2Series, phase2);
                AddDbPointsToSeries(_phase3Series, phase3);
            }
            catch (Exception ex)
            {
                Logger.Error($"SetThreePhaseDbData error: {ex.Message}", ex);
            }
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

        /// <summary>
        /// Highlights dominant local peaks with marker and frequency labels.
        /// Input values are expected in dB scale.
        /// </summary>
        public void ShowPeakMarkers(IList<PointF> dbPoints, int maxMarkers = 8)
        {
            if (_disposed) throw new ObjectDisposedException("MyChartClass");
            if (InvokeRequired)
            {
                BeginInvoke(new Action<IList<PointF>, int>(ShowPeakMarkers), dbPoints, maxMarkers);
                return;
            }
            if (_peakSeries == null) return;
            _peakSeries.Points.Clear();
            if (dbPoints == null || dbPoints.Count < 3 || maxMarkers <= 0) return;

            try
            {
                const float noiseFloorDb = -80f;
                float minSeparationHz = (float)Math.Max(2.0, (_xMax - _xMin) / 40.0);

                List<int> candidateIndices = new List<int>();
                for (int i = 1; i < dbPoints.Count - 1; i++)
                {
                    float prev = dbPoints[i - 1].Y;
                    float curr = dbPoints[i].Y;
                    float next = dbPoints[i + 1].Y;

                    if (curr < noiseFloorDb) continue;
                    if (curr >= prev && curr >= next)
                        candidateIndices.Add(i);
                }

                // Sort by strongest amplitude first (higher dB means stronger).
                candidateIndices.Sort(delegate (int a, int b)
                {
                    return dbPoints[b].Y.CompareTo(dbPoints[a].Y);
                });

                List<int> selected = new List<int>();
                for (int i = 0; i < candidateIndices.Count; i++)
                {
                    int idx = candidateIndices[i];
                    float x = dbPoints[idx].X;
                    bool tooClose = false;

                    for (int j = 0; j < selected.Count; j++)
                    {
                        float sx = dbPoints[selected[j]].X;
                        if (Math.Abs(sx - x) < minSeparationHz)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (tooClose) continue;

                    selected.Add(idx);
                    if (selected.Count >= maxMarkers) break;
                }

                // Sort back by frequency for clean left-to-right labels.
                selected.Sort(delegate (int a, int b)
                {
                    return dbPoints[a].X.CompareTo(dbPoints[b].X);
                });

                for (int i = 0; i < selected.Count; i++)
                {
                    PointF p = dbPoints[selected[i]];
                    int pointIndex = _peakSeries.Points.AddXY(p.X, p.Y);
                    DataPoint dp = _peakSeries.Points[pointIndex];
                    dp.Label = p.X.ToString("F1") + " Hz";
                    dp.LabelForeColor = Color.FromArgb(45, 55, 72);
                    dp.Font = new Font("Segoe UI", 7F, FontStyle.Bold);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ShowPeakMarkers error: {ex.Message}", ex);
            }
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
            this.Titles.Clear();
            this.Legends.Clear();
            this.Palette = ChartColorPalette.None;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.BorderlineColor = Color.FromArgb(189, 195, 199);
            this.BorderlineWidth = 1;
            this.BorderlineDashStyle = ChartDashStyle.Solid;

            ChartArea chartArea = new ChartArea("SpectralData")
            {
                BackColor = Color.White,
                BorderColor = Color.FromArgb(205, 210, 214),
                BorderWidth = 1
            };

            chartArea.AxisX.Title = "Frequency (Hz)";
            chartArea.AxisX.TitleFont = new Font("Segoe UI", 8F, FontStyle.Bold);
            chartArea.AxisX.Minimum = _xMin;
            chartArea.AxisX.Maximum = _xMax;
            chartArea.AxisX.IsMarginVisible = false;
            chartArea.AxisX.MajorGrid.Enabled = true;
            chartArea.AxisX.MajorGrid.LineColor = Color.FromArgb(70, 0, 0, 0);
            chartArea.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dot;
            chartArea.AxisX.MinorGrid.Enabled = true;
            chartArea.AxisX.MinorGrid.LineColor = Color.FromArgb(35, 0, 0, 0);
            chartArea.AxisX.MinorGrid.LineDashStyle = ChartDashStyle.Dot;
            chartArea.AxisX.MinorTickMark.Enabled = true;
            chartArea.AxisX.MajorTickMark.LineColor = Color.FromArgb(130, 130, 130);
            chartArea.AxisX.LabelStyle.Font = new Font("Segoe UI", 7F);
            chartArea.AxisX.LineColor = Color.FromArgb(90, 90, 90);
            chartArea.AxisX.LineWidth = 1;
            chartArea.AxisX.LabelStyle.Format = "0";
            chartArea.AxisX.Interval = Math.Max(1.0, (_xMax - _xMin) / 10.0);
            chartArea.AxisX.MinorGrid.Interval = chartArea.AxisX.Interval / 2.0;
            chartArea.AxisX.MajorGrid.LineWidth = 1;
            chartArea.AxisX.MinorGrid.LineWidth = 1;

            chartArea.AxisY.Title = "Amplitude (dB)";
            chartArea.AxisY.TitleFont = new Font("Segoe UI", 8F, FontStyle.Bold);
            chartArea.AxisY.Minimum = _yMin;
            chartArea.AxisY.Maximum = _yMax;
            chartArea.AxisY.MajorGrid.Enabled = true;
            chartArea.AxisY.MajorGrid.LineColor = Color.FromArgb(70, 0, 0, 0);
            chartArea.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dot;
            chartArea.AxisY.MinorGrid.Enabled = true;
            chartArea.AxisY.MinorGrid.LineColor = Color.FromArgb(35, 0, 0, 0);
            chartArea.AxisY.MinorGrid.LineDashStyle = ChartDashStyle.Dot;
            chartArea.AxisY.MinorTickMark.Enabled = true;
            chartArea.AxisY.MajorTickMark.LineColor = Color.FromArgb(130, 130, 130);
            chartArea.AxisY.LabelStyle.Font = new Font("Segoe UI", 7F);
            chartArea.AxisY.LineColor = Color.FromArgb(90, 90, 90);
            chartArea.AxisY.LineWidth = 1;
            chartArea.AxisY.Interval = 10;
            chartArea.AxisY.MinorGrid.Interval = 5;
            chartArea.AxisY.MajorGrid.LineWidth = 1;
            chartArea.AxisY.MinorGrid.LineWidth = 1;

            chartArea.CursorX.IsUserEnabled = true;
            chartArea.CursorX.IsUserSelectionEnabled = true;
            chartArea.CursorY.IsUserEnabled = true;
            chartArea.CursorY.IsUserSelectionEnabled = true;
            chartArea.CursorX.LineColor = Color.FromArgb(65, 90, 115);
            chartArea.CursorY.LineColor = Color.FromArgb(65, 90, 115);
            chartArea.CursorX.LineDashStyle = ChartDashStyle.Dash;
            chartArea.CursorY.LineDashStyle = ChartDashStyle.Dash;
            chartArea.AxisX.ScaleView.Zoomable = true;
            chartArea.AxisY.ScaleView.Zoomable = true;

            chartArea.InnerPlotPosition = new ElementPosition(12, 8, 85, 78);
            chartArea.AxisY.StripLines.Add(new StripLine
            {
                Interval = 0,
                StripWidth = 0,
                BorderColor = Color.FromArgb(80, 120, 120, 120),
                BorderDashStyle = ChartDashStyle.Solid,
                BorderWidth = 1,
                IntervalOffset = -20
            });

            this.ChartAreas.Add(chartArea);

            Legend legend = new Legend("PhaseLegend")
            {
                Docking = Docking.Top,
                Alignment = StringAlignment.Far,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F)
            };
            this.Legends.Add(legend);

            _dataSeries = new Series("Phase 1")
            {
                ChartType = SeriesChartType.FastLine,
                Color = Color.Black,
                BorderWidth = 2,
                MarkerStyle = MarkerStyle.None
            };
            this.Series.Add(_dataSeries);

            _phase2Series = new Series("Phase 2")
            {
                ChartType = SeriesChartType.FastLine,
                Color = Color.FromArgb(41, 128, 185),
                BorderWidth = 2,
                MarkerStyle = MarkerStyle.None
            };
            this.Series.Add(_phase2Series);

            _phase3Series = new Series("Phase 3")
            {
                ChartType = SeriesChartType.FastLine,
                Color = Color.FromArgb(192, 57, 43),
                BorderWidth = 2,
                MarkerStyle = MarkerStyle.None
            };
            this.Series.Add(_phase3Series);

            _peakSeries = new Series("PeakMarkers")
            {
                ChartType = SeriesChartType.Point,
                Color = Color.FromArgb(231, 76, 60),
                MarkerColor = Color.FromArgb(231, 76, 60),
                MarkerBorderColor = Color.FromArgb(192, 57, 43),
                MarkerBorderWidth = 1,
                MarkerStyle = MarkerStyle.Triangle,
                MarkerSize = 8,
                IsVisibleInLegend = false
            };
            this.Series.Add(_peakSeries);

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

        private void AddDbPointsToSeries(Series series, IList<PointF> pts)
        {
            if (series == null || pts == null) return;
            for (int i = 0; i < pts.Count; i++)
            {
                double y = Math.Max(_yMin, Math.Min(_yMax, pts[i].Y));
                series.Points.AddXY(pts[i].X, y);
            }
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