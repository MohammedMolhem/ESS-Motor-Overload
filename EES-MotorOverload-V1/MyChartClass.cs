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
        private bool _linearYScale = false;
        private Title _cursorReadoutTitle;
        private const int TIMER_INTERVAL_MS = 100;
        /// <summary>~5 darker major grid lines across the plot.</summary>
        private const int EngineeringMajorDivisions = 5;
        /// <summary>4 lighter minor lines between each major division.</summary>
        private const int EngineeringMinorDivisions = 4;

        public enum SignalType
        {
            SineWave, SquareWave, TriangleWave, Pulse, Noise, Decaying
        }

        /// <summary>A labelled vertical reference line (e.g. a bearing fault frequency).</summary>
        public struct FreqMarker
        {
            public double Hz;
            public string Label;
            public Color Color;
            public FreqMarker(double hz, string label, Color color)
            {
                Hz = hz;
                Label = label;
                Color = color;
            }
        }

        private readonly List<FreqMarker> _faultMarkers = new List<FreqMarker>();

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

        /// <summary>Phase A / B / C line colors on the combined engineering plot.</summary>
        public void SetThreePhaseColors(Color phase1, Color phase2, Color phase3)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Color, Color, Color>(SetThreePhaseColors), phase1, phase2, phase3);
                return;
            }
            if (_dataSeries != null)
            {
                _dataSeries.Color = phase1;
                _dataSeries.BorderColor = phase1;
            }
            if (_phase2Series != null)
            {
                _phase2Series.Color = phase2;
                _phase2Series.BorderColor = phase2;
            }
            if (_phase3Series != null)
            {
                _phase3Series.Color = phase3;
                _phase3Series.BorderColor = phase3;
            }
            Invalidate(true);
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
                    ApplyEngineeringGrid(ca);
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
                    ApplyEngineeringGrid(ca);
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
                    EnsureHarmonicsPlotReady(_xMax > 0 ? _xMax : 2500);
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
                _linearYScale = false;
                AutoScaleAxesFromSeries();
                RefreshEngineeringPlotAppearance();
            }
            catch (Exception ex)
            {
                Logger.Error($"SetThreePhaseDbData error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Linear Y scale for sparse exports (e.g. main.c WAVELET_CSV EMEAN per level).
        /// </summary>
        public void SetThreePhaseLinearData(IList<PointF> phase1, IList<PointF> phase2, IList<PointF> phase3)
        {
            if (_disposed) throw new ObjectDisposedException("MyChartClass");
            if (InvokeRequired)
            {
                BeginInvoke(new Action<IList<PointF>, IList<PointF>, IList<PointF>>(SetThreePhaseLinearData), phase1, phase2, phase3);
                return;
            }

            try
            {
                if (ChartAreas.Count > 0)
                {
                    ChartAreas[0].AxisY.Title = "Amplitude";
                    ChartAreas[0].AxisY.LabelStyle.Format = "G4";
                }

                _dataSeries?.Points.Clear();
                _phase2Series?.Points.Clear();
                _phase3Series?.Points.Clear();

                AddLinearPointsToSeries(_dataSeries, phase1);
                AddLinearPointsToSeries(_phase2Series, phase2);
                AddLinearPointsToSeries(_phase3Series, phase3);
                _linearYScale = true;
                AutoScaleAxesFromSeries(linearY: true);
                RefreshEngineeringPlotAppearance();
            }
            catch (Exception ex)
            {
                Logger.Error($"SetThreePhaseLinearData error: {ex.Message}", ex);
            }
        }

        /// <summary>Shows engineering grid immediately (Harmonics tab), even before USB data arrives.</summary>
        public void EnsureHarmonicsPlotReady(double maxFreqHz = 2500)
        {
            if (_disposed || ChartAreas.Count == 0) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<double>(EnsureHarmonicsPlotReady), maxFreqHz);
                return;
            }

            try
            {
                ChartArea ca = ChartAreas[0];
                ca.AxisX.Minimum = 0;
                ca.AxisX.Maximum = maxFreqHz > 0 ? maxFreqHz : 2500;
                _xMin = ca.AxisX.Minimum;
                _xMax = ca.AxisX.Maximum;
                if (!_linearYScale)
                {
                    ca.AxisY.Title = "Amplitude (dB)";
                    ca.AxisY.Minimum = _yMin;
                    ca.AxisY.Maximum = _yMax;
                    ca.AxisY.LabelStyle.Format = "0";
                }
                RefreshEngineeringPlotAppearance();
            }
            catch (Exception ex)
            {
                Logger.Error($"EnsureHarmonicsPlotReady error: {ex.Message}", ex);
            }
        }

        /// <summary>Re-applies 5 major + 4 minor grid and redraws.</summary>
        public void RefreshEngineeringPlotAppearance()
        {
            if (_disposed || ChartAreas.Count == 0) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshEngineeringPlotAppearance));
                return;
            }

            try
            {
                ApplyEngineeringGrid(ChartAreas[0]);
                Invalidate(true);
            }
            catch (Exception ex)
            {
                Logger.Error($"RefreshEngineeringPlotAppearance error: {ex.Message}", ex);
            }
        }

        public void SetAxisTitles(string xTitle, string yTitle)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, string>(SetAxisTitles), xTitle, yTitle);
                return;
            }
            if (ChartAreas.Count == 0) return;
            var ca = ChartAreas[0];
            if (!string.IsNullOrEmpty(xTitle)) ca.AxisX.Title = xTitle;
            if (!string.IsNullOrEmpty(yTitle)) ca.AxisY.Title = yTitle;
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

        /// <summary>
        /// Overlays labelled vertical reference lines at known fault frequencies
        /// (supply line, shaft, BPFO/BPFI/BSF/FTF) so spectral peaks can be read
        /// directly against their expected fault frequencies.
        /// </summary>
        public void SetFaultMarkers(IList<FreqMarker> markers)
        {
            if (_disposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<IList<FreqMarker>>(SetFaultMarkers), markers);
                return;
            }
            try
            {
                _faultMarkers.Clear();
                if (markers != null)
                    _faultMarkers.AddRange(markers);
                ApplyFaultMarkers();
                Invalidate(true);
            }
            catch (Exception ex) { Logger.Error($"SetFaultMarkers error: {ex.Message}", ex); }
        }

        private void ApplyFaultMarkers()
        {
            if (ChartAreas.Count == 0) return;
            Axis ax = ChartAreas[0].AxisX;

            for (int i = ax.StripLines.Count - 1; i >= 0; i--)
                if (ax.StripLines[i].Tag as string == "FAULT_MARKER")
                    ax.StripLines.RemoveAt(i);

            if (_faultMarkers.Count == 0) return;

            double min = ax.Minimum;
            double max = ax.Maximum;
            foreach (FreqMarker fm in _faultMarkers)
            {
                if (fm.Hz <= 0 || double.IsNaN(fm.Hz)) continue;
                if (fm.Hz < min || fm.Hz > max) continue;

                ax.StripLines.Add(new StripLine
                {
                    Tag = "FAULT_MARKER",
                    Interval = 0,
                    IntervalOffset = fm.Hz,
                    StripWidth = 0,
                    BorderColor = fm.Color,
                    BorderWidth = 2,
                    BorderDashStyle = ChartDashStyle.Dash,
                    Text = fm.Label,
                    TextAlignment = StringAlignment.Near,
                    TextLineAlignment = StringAlignment.Near,
                    ForeColor = fm.Color,
                    Font = new Font("Segoe UI", 7F, FontStyle.Bold)
                });
            }
        }

        public void SetUpdateInterval(int milliseconds)
        {
            if (_dataTimer != null && milliseconds > 0)
                _dataTimer.Interval = milliseconds;
        }

        /// <summary>
        /// Uses nearly all control pixels for the plot (Harmonics tab — frame size unchanged).
        /// </summary>
        public void MaximizeHarmonicsPlotArea()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(MaximizeHarmonicsPlotArea));
                return;
            }

            try
            {
                PhaseName = "";
                Titles.Clear();
                BorderlineWidth = 0;
                Padding = Padding.Empty;
                Margin = Padding.Empty;

                if (ChartAreas.Count > 0)
                {
                    ChartArea ca = ChartAreas[0];
                    ca.Position.Auto = false;
                    ca.Position = new ElementPosition(0, 0, 100, 100);
                    ca.InnerPlotPosition.Auto = false;
                    ca.InnerPlotPosition = new ElementPosition(2, 3, 96, 93);
                }

                if (Legends.Count > 0)
                {
                    Legend leg = Legends[0];
                    leg.Docking = Docking.Bottom;
                    leg.Alignment = StringAlignment.Center;
                    leg.IsDockedInsideChartArea = true;
                    leg.LegendStyle = LegendStyle.Row;
                    leg.Font = new Font("Segoe UI", 7F);
                    leg.BackColor = Color.Transparent;
                }

                if (ChartAreas.Count > 0)
                {
                    ApplyEngineeringGrid(ChartAreas[0]);
                    Invalidate(true);
                }
                EnsureCursorReadoutTitle();
            }
            catch (Exception ex)
            {
                Logger.Error($"MaximizeHarmonicsPlotArea error: {ex.Message}", ex);
            }
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
            this.BackColor = Color.FromArgb(236, 240, 245);
            this.BorderlineColor = Color.FromArgb(120, 130, 145);
            this.BorderlineWidth = 2;
            this.BorderlineDashStyle = ChartDashStyle.Solid;

            ChartArea chartArea = new ChartArea("SpectralData")
            {
                BackColor = Color.White,
                BorderColor = Color.FromArgb(100, 110, 125),
                BorderWidth = 2,
                BorderDashStyle = ChartDashStyle.Solid
            };

            chartArea.AxisX.Title = "Frequency (Hz)";
            chartArea.AxisX.TitleFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            chartArea.AxisX.TitleForeColor = Color.FromArgb(45, 55, 72);
            chartArea.AxisX.Minimum = _xMin;
            chartArea.AxisX.Maximum = _xMax;
            chartArea.AxisX.IsMarginVisible = false;

            chartArea.AxisY.Title = "Amplitude (dB)";
            chartArea.AxisY.TitleFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            chartArea.AxisY.TitleForeColor = Color.FromArgb(45, 55, 72);
            chartArea.AxisY.Minimum = _yMin;
            chartArea.AxisY.Maximum = _yMax;

            ApplyEngineeringGrid(chartArea);

            chartArea.CursorX.IsUserEnabled = true;
            chartArea.CursorX.IsUserSelectionEnabled = true;
            chartArea.CursorY.IsUserEnabled = true;
            chartArea.CursorY.IsUserSelectionEnabled = true;
            chartArea.CursorX.AutoScroll = true;
            chartArea.CursorY.AutoScroll = true;
            chartArea.CursorX.LineColor = Color.FromArgb(192, 57, 43);
            chartArea.CursorY.LineColor = Color.FromArgb(192, 57, 43);
            chartArea.CursorX.LineWidth = 1;
            chartArea.CursorY.LineWidth = 1;
            chartArea.CursorX.LineDashStyle = ChartDashStyle.Dash;
            chartArea.CursorY.LineDashStyle = ChartDashStyle.Dash;
            chartArea.AxisX.ScaleView.Zoomable = true;
            chartArea.AxisY.ScaleView.Zoomable = true;

            chartArea.InnerPlotPosition = new ElementPosition(10, 12, 86, 76);

            this.ChartAreas.Add(chartArea);
            EnsureCursorReadoutTitle();
            MouseMove -= Chart_MouseMoveEngineering;
            MouseMove += Chart_MouseMoveEngineering;

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
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(45, 55, 72),
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

        private void AddLinearPointsToSeries(Series series, IList<PointF> pts)
        {
            if (series == null || pts == null) return;
            for (int i = 0; i < pts.Count; i++)
                series.Points.AddXY(pts[i].X, pts[i].Y);
        }

        private void AutoScaleAxesFromSeries(bool linearY = false)
        {
            if (ChartAreas.Count == 0) return;
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;
            bool any = false;

            foreach (Series s in Series)
            {
                if (s == _peakSeries || s.Points.Count == 0) continue;
                foreach (DataPoint dp in s.Points)
                {
                    any = true;
                    if (dp.XValue < xMin) xMin = dp.XValue;
                    if (dp.XValue > xMax) xMax = dp.XValue;
                    if (dp.YValues[0] < yMin) yMin = dp.YValues[0];
                    if (dp.YValues[0] > yMax) yMax = dp.YValues[0];
                }
            }

            if (!any)
            {
                EnsureHarmonicsPlotReady(_xMax > 0 ? _xMax : 2500);
                return;
            }

            var ca = ChartAreas[0];
            if (xMax <= xMin) xMax = xMin + 1.0;
            double xPad = (xMax - xMin) * 0.02;
            ca.AxisX.Minimum = Math.Max(0, xMin - xPad);
            ca.AxisX.Maximum = xMax + xPad;
            _xMin = ca.AxisX.Minimum;
            _xMax = ca.AxisX.Maximum;

            if (linearY)
            {
                if (yMax <= yMin) yMax = yMin + 1e-6;
                double yPad = (yMax - yMin) * 0.1;
                if (yPad < 1e-9) yPad = yMax * 0.1;
                ca.AxisY.Minimum = yMin - yPad;
                ca.AxisY.Maximum = yMax + yPad;
            }
            else
            {
                ca.AxisY.Title = "Amplitude (dB)";
                ca.AxisY.LabelStyle.Format = "0";
                ca.AxisY.Minimum = _yMin;
                ca.AxisY.Maximum = _yMax;
            }

            ApplyEngineeringGrid(ca);
        }

        /// <summary>Snapped major/minor grid for accurate frequency and amplitude readout.</summary>
        private void ApplyEngineeringGrid(ChartArea ca)
        {
            if (ca == null) return;

            SnapAxisToEngineeringGrid(ca.AxisX, isFrequency: true, isDbScale: false);
            SnapAxisToEngineeringGrid(ca.AxisY, isFrequency: false,
                isDbScale: !_linearYScale && (ca.AxisY.Title ?? "").IndexOf("dB", StringComparison.OrdinalIgnoreCase) >= 0);

            ConfigureAxisGridIntervals(ca.AxisX);
            ConfigureAxisGridIntervals(ca.AxisY);
            StyleAxisGrid(ca.AxisX, isFrequency: true);
            StyleAxisGrid(ca.AxisY, isFrequency: false);
            UpdateZeroDbReferenceLine(ca);
            ApplyEngineeringStripGrid(ca);
        }

        private static void ConfigureAxisGridIntervals(Axis axis)
        {
            axis.MajorGrid.IntervalType = DateTimeIntervalType.Number;
            axis.MinorGrid.IntervalType = DateTimeIntervalType.Number;
            axis.MajorTickMark.IntervalType = DateTimeIntervalType.Number;
            axis.MinorTickMark.IntervalType = DateTimeIntervalType.Number;
            axis.MajorGrid.Interval = axis.Interval;
            axis.MinorGrid.Interval = axis.Interval / EngineeringMinorDivisions;
            axis.MajorGrid.Enabled = true;
            axis.MinorGrid.Enabled = true;
        }

        private static void SnapAxisToEngineeringGrid(Axis axis, bool isFrequency, bool isDbScale)
        {
            double min = axis.Minimum;
            double max = axis.Maximum;
            double span = max - min;
            if (span <= 0 || double.IsNaN(span) || double.IsInfinity(span)) return;

            double major = NiceGridStep(span, EngineeringMajorDivisions);

            if (major <= 0) major = 1;
            double minor = major / EngineeringMinorDivisions;

            double snapMin = Math.Floor(min / major) * major;
            double snapMax = Math.Ceiling(max / major) * major;
            if (snapMax <= snapMin) snapMax = snapMin + major;

            axis.Minimum = snapMin;
            axis.Maximum = snapMax;
            axis.Interval = major;
            axis.IntervalOffset = 0;
            axis.MajorGrid.Interval = major;
            axis.MinorGrid.Interval = minor;
            axis.MajorTickMark.Interval = major;
            axis.MinorTickMark.Interval = minor;
            axis.MajorGrid.IntervalOffset = 0;
            axis.MinorGrid.IntervalOffset = 0;

            if (isFrequency)
                axis.LabelStyle.Format = major >= 100 ? "0" : major >= 10 ? "0" : "0.#";
            else if (isDbScale)
                axis.LabelStyle.Format = major >= 10 ? "0" : "0.#";
            else
                axis.LabelStyle.Format = major >= 1 ? "0.##" : "0.###";
        }

        private static void StyleAxisGrid(Axis axis, bool isFrequency)
        {
            axis.MajorGrid.Enabled = true;
            axis.MajorGrid.LineColor = Color.FromArgb(75, 88, 108);
            axis.MajorGrid.LineDashStyle = ChartDashStyle.Solid;
            axis.MajorGrid.LineWidth = 2;
            axis.MinorGrid.Enabled = true;
            axis.MinorGrid.LineColor = Color.FromArgb(200, 208, 220);
            axis.MinorGrid.LineDashStyle = ChartDashStyle.Solid;
            axis.MinorGrid.LineWidth = 1;
            axis.MajorTickMark.Enabled = true;
            axis.MinorTickMark.Enabled = true;
            axis.MajorTickMark.TickMarkStyle = TickMarkStyle.OutsideArea;
            axis.MinorTickMark.TickMarkStyle = TickMarkStyle.OutsideArea;
            axis.MajorTickMark.LineColor = Color.FromArgb(90, 98, 110);
            axis.MinorTickMark.LineColor = Color.FromArgb(170, 176, 186);
            axis.LabelStyle.Font = new Font("Segoe UI", 7.5F);
            axis.LabelStyle.ForeColor = Color.FromArgb(55, 65, 80);
            axis.LineColor = Color.FromArgb(85, 95, 108);
            axis.LineWidth = 1;
            if (isFrequency)
                axis.IsStartedFromZero = true;
        }

        private static void ClearEngineeringStripLines(ChartArea ca)
        {
            for (int i = ca.AxisX.StripLines.Count - 1; i >= 0; i--)
            {
                string tag = ca.AxisX.StripLines[i].Tag as string;
                if (tag == "ENG_MAJOR" || tag == "ENG_MINOR")
                    ca.AxisX.StripLines.RemoveAt(i);
            }
            for (int i = ca.AxisY.StripLines.Count - 1; i >= 0; i--)
            {
                string tag = ca.AxisY.StripLines[i].Tag as string;
                if (tag == "ENG_MAJOR" || tag == "ENG_MINOR")
                    ca.AxisY.StripLines.RemoveAt(i);
            }
        }

        /// <summary>Draws 5 dark major + 4 light minor division lines (always visible on chart).</summary>
        private static void ApplyEngineeringStripGrid(ChartArea ca)
        {
            ClearEngineeringStripLines(ca);

            AddAxisStripDivisions(ca.AxisX, true);
            AddAxisStripDivisions(ca.AxisY, false);
        }

        private static void AddAxisStripDivisions(Axis axis, bool vertical)
        {
            double min = axis.Minimum;
            double max = axis.Maximum;
            double major = axis.Interval;
            if (major <= 0 || max <= min) return;

            double minor = major / EngineeringMinorDivisions;
            int majorCount = (int)Math.Round((max - min) / major);
            if (majorCount < 1) majorCount = 1;

            for (int m = 0; m <= majorCount; m++)
            {
                double majorPos = min + m * major;
                if (majorPos > max + minor * 0.01) break;
                axis.StripLines.Add(CreateGridStripLine(majorPos, true, vertical));

                if (m >= majorCount) continue;
                for (int n = 1; n < EngineeringMinorDivisions; n++)
                {
                    double minorPos = majorPos + n * minor;
                    if (minorPos >= max - minor * 0.001) break;
                    axis.StripLines.Add(CreateGridStripLine(minorPos, false, vertical));
                }
            }
        }

        private static StripLine CreateGridStripLine(double position, bool major, bool vertical)
        {
            return new StripLine
            {
                Tag = major ? "ENG_MAJOR" : "ENG_MINOR",
                Interval = 0,
                IntervalOffset = position,
                StripWidth = 0,
                BorderColor = major
                    ? Color.FromArgb(vertical ? 70 : 75, 85, 105)
                    : Color.FromArgb(vertical ? 215 : 210, 218, 228),
                BorderWidth = major ? 2 : 1,
                BorderDashStyle = ChartDashStyle.Solid
            };
        }

        private static void UpdateZeroDbReferenceLine(ChartArea ca)
        {
            for (int i = ca.AxisY.StripLines.Count - 1; i >= 0; i--)
            {
                if (ca.AxisY.StripLines[i].Tag as string == "ZERO_DB")
                    ca.AxisY.StripLines.RemoveAt(i);
            }

            string yTitle = ca.AxisY.Title ?? "";
            if (yTitle.IndexOf("dB", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (ca.AxisY.Minimum > 0 || ca.AxisY.Maximum < 0) return;

            ca.AxisY.StripLines.Add(new StripLine
            {
                Tag = "ZERO_DB",
                Interval = 0,
                IntervalOffset = 0,
                StripWidth = 0,
                BorderColor = Color.FromArgb(160, 41, 128, 185),
                BorderWidth = 1,
                BorderDashStyle = ChartDashStyle.Dash
            });
        }

        private static double NiceGridStep(double span, int targetDivisions)
        {
            if (span <= 0 || targetDivisions <= 0) return 1;
            double rough = span / targetDivisions;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            if (mag <= 0) mag = 1;
            double norm = rough / mag;
            double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
            return nice * mag;
        }

        private void EnsureCursorReadoutTitle()
        {
            if (_cursorReadoutTitle == null)
            {
                _cursorReadoutTitle = new Title
                {
                    Name = "EngCursor",
                    Text = "Move cursor over plot for f, amplitude",
                    Docking = Docking.Top,
                    Alignment = ContentAlignment.TopRight,
                    Font = new Font("Consolas", 8F),
                    ForeColor = Color.FromArgb(45, 55, 72),
                    BackColor = Color.FromArgb(245, 247, 250)
                };
            }

            bool found = false;
            foreach (Title t in Titles)
            {
                if (t == _cursorReadoutTitle || t.Name == "EngCursor")
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                Titles.Add(_cursorReadoutTitle);
        }

        private void Chart_MouseMoveEngineering(object sender, MouseEventArgs e)
        {
            if (ChartAreas.Count == 0 || _cursorReadoutTitle == null) return;
            try
            {
                ChartArea ca = ChartAreas[0];
                double x = ca.AxisX.PixelPositionToValue(e.X);
                double y = ca.AxisY.PixelPositionToValue(e.Y);
                if (double.IsNaN(x) || double.IsNaN(y)) return;

                string yUnit = (ca.AxisY.Title ?? "").Contains("dB") ? " dB" :
                    _linearYScale ? "" : " dB";
                _cursorReadoutTitle.Text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "f = {0:0.##} Hz   |   A = {1:0.##}{2}",
                    x, y, yUnit);
            }
            catch
            {
                _cursorReadoutTitle.Text = "";
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