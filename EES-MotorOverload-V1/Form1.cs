using EES_MotorOverload_V1.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EES_MotorOverload_V1
{
    public partial class Form1 : Form
    {
        private const string DB_CONNECTION_STRING =
            "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=BearDatabase.accdb";

        private bool _formClosing = false;
        private DatabaseService _dbService;
        private STM32ReportParser _reportParser;

        private TabControl tabControl1;
        private TabPage tabStart, tabBearingSelect, tabAlarmMonitor, tabHarmonics, tabSettings;

        // ── Start Tab ──
        private GroupBox grpMainItems;
        private Button btnBearing, btnAlarm, btnHarmonics, btnSettings, btnExit;

        // ── Bearing Select Tab ──
        private GroupBox grpBearingType, grpBearingDatabase;
        private ComboBox cmbBearingSelect;
        private TextBox txtID, txtBea, txtNB, txtBD, txtPD, txtPHI;
        private TextBox txtBPFO, txtBPFI, txtFTF, txtBSF;
        //private TextBox TempBox;
        private Label lblSelectBear;
        private Button btnEnableBearing;
        private DataGridView dgvBearings;

        // ── Report Tab ──
        private ReportHeaderPanel pnlReportHeader;
        private Panel pnlReportCards;
        private ReportMetricCard cardReportOverall;
        private ReportMetricCard cardReportBpfo;
        private ReportMetricCard cardReportBpfi;
        private ReportMetricCard cardReportBsf;
        private ReportMetricCard cardReportStator;
        private Label lblReport12Caption;
        private Panel pnlReport12Techniques;
        private ReportTechniqueTile[] _reportTechniqueTiles;
        private StatorSectionHeaderPanel pnlStatorHeader;
        private Panel pnlReportStatorAlarms;
        private StatorFaultCard cardReportStatorShort;
        private StatorFaultCard cardReportStatorGround;
        private Panel pnlReportStatorMetrics;
        private StatorMetricTile[] _reportStatorTiles;
        private Button btnReportRefresh;
        private Button btnReportExport;
        private Button btnReportCopy;
        private CheckBox chkReportShowReference;
        private RichTextBox rtbTechniqueReport;

        // ── Harmonics Tab ──
        private GroupBox grpHarmonicControl;
        private GroupBox grpSmartEngineering;
        private Button btnSmartViewRefresh, btnSmartViewExport;
        private RichTextBox rtbSmartEngineering;

        // Technique selection buttons
        private Button btnTechFourier, btnTechMusic, btnTechEsprit, btnTechCyclo;
        private Button btnTechSk, btnTechWavelet, btnTechWelch, btnTechCoh;

        // Report tab USB actions
        private Button btnReportFullReport, btnReportGraphData, btnReportPhaseCsv;

        // Settings — USB data export & baseline (main.c)
        private GroupBox grpBaseline;

        private MyChartClass _xyChart1;
        private MyChartClass _xyChart2;
        private MyChartClass _xyChart3;
        private Label lblPhase1, lblPhase2, lblPhase3;
        private ToolTip _harmonicsStatusToolTip;

        // Current technique selection and last received frame
        private enum SpectralTechnique { Fourier, Music, Esprit, Cyclostationary, Sk, Wavelet, Welch, Coherence }
        private SpectralTechnique _currentTechnique = SpectralTechnique.Fourier;
        private SpectralFrame _lastFrame = null;
        private TelemetryData _lastTelemetryForReport = null;

        // ── Settings Tab — STM32 Communication ──
        private STM32CommManager _comm;

        private GroupBox grpConnection;
        private ComboBox cmbPorts;
        private GroupBox grpMonitor;
        private Button btnRefresh, btnConnect, btnPing;
        private Panel pnlIndicator;
        private Label lblConnStatus;

        private GroupBox grpMotor;
        private TextBox txtRPM, txtSlip;

        private GroupBox grpBearingCoeff;
        private TextBox txtStmBPFO, txtStmBPFI, txtStmFTF, txtStmBSF;

        private GroupBox grpThresholds;
        private TextBox txtFaultThresh, txtWarnThresh, txtSupplyLineHz;

        private CheckBox chkUseTextProtocol;

        private Button btnSendAll, btnReadAll;
        private Button btnSaveFlash, btnLoadFlash, btnResetDefault;
        private Button btnSaveAdc;
        private Button btnGetCoeff;
        private CheckBox chkSlipAuto;
        private CheckBox chkLiveStream;

        // Device clock (main.c software RTC via DATE / TIME text commands).
        private Label lblDeviceClock;
        private DateTimePicker dtpClockDate;
        private DateTimePicker dtpClockTime;
        private Button btnSendClock, btnSyncPcClock, btnReadClock;

        private TextBox txtTerminalCmd;
        private Button btnTerminalSend;

        private Panel pnlHarmonicsSpectrumFrame;
        private HarmonicsStatusChip chipBpfo, chipBpfi, chipBsf, chipFtf;
        private HarmonicsStatusChip chipFft, chipMusic, chipEsprit, chipCyclic2, chipSk, chipWavelet;
        private HarmonicsStatusChip chipWelch, chipCoh;
        private readonly Dictionary<string, Label> _liveTelemetryLabels =
            new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Label> _settingsMonitorLabels =
            new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Label> _alarmBearingDetailLabels =
            new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Label> _alarmStatorDetailLabels =
            new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
        private TextBox txtSettingsTemperature;
        private MyBar barSettingsStatorShort;
        private MyBar barSettingsStatorGround;
        private TextBox txtSettingsStatorShort;
        private TextBox txtSettingsStatorGround;

        private RichTextBox rtbLog;

        private Control[] _paramControls;
        private ToolTip _mainCToolTip;

        // ═════════════════════════════════════════════════════════════
        // Constructor
        // ═══════════════════════════════════════════════════════���═════

        public Form1()
        {
            InitializeComponent();

            try
            {
                this.Text = "Bearing Fault Diagnosis System";
                this.ClientSize = new Size(900, 600);
                this.StartPosition = FormStartPosition.CenterScreen;
                this.Font = new Font("Tahoma", 10F);
                this.FormClosing += Form1_FormClosing;

                _reportParser = new STM32ReportParser();
                _reportParser.OnFrameReady += ReportParser_OnFrameReady;

                BuildAllUI();
                InitializeServices();
                LoadApplicationData();

                Logger.Info("Form1 initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Form1 constructor error: {ex.Message}", ex);
                MessageBox.Show($"Initialization error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ═════════════════════════════════════════════════════════════
        // UI Construction
        // ═════════════════════════════════════════════════════════════

        private void BuildAllUI()
        {
            BuildTabControl();
            BuildStartTab();
            BuildBearingSelectTab();
            BuildAlarmMonitorTab();
            BuildHarmonicsTab();
            BuildSettingsTab();
        }

        private void BuildTabControl()
        {
            tabControl1 = new TabControl
            {
                Location = new Point(0, 0),
                Size = new Size(1280, 720),
                Dock = DockStyle.Fill,
                Font = new Font("Tahoma", 10F)
            };

            tabStart = new TabPage("Start");
            tabBearingSelect = new TabPage("Bearing Select");
            tabAlarmMonitor = new TabPage("Report");
            tabHarmonics = new TabPage("Harmonics");
            tabSettings = new TabPage("Settings");

            tabControl1.TabPages.AddRange(new TabPage[] {
                tabStart, tabBearingSelect, tabAlarmMonitor,
                tabHarmonics, tabSettings
            });

            this.Controls.Add(tabControl1);
        }

        // ─────────────────────────────────────────────────────────────
        // Start Tab
        // ─────────────────────────────────────────────────────────────

        private void BuildStartTab()
        {
            grpMainItems = new GroupBox
            {
                Text = "Main Items",
                Location = new Point(44, 30),
                Size = new Size(280, 460),
                Font = new Font("Tahoma", 11F, FontStyle.Bold)
            };

            string[] names = { "Bearing Select", "Report", "Harmonics",
                               "Settings", "Exit" };
            Button[] buttons = new Button[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                buttons[i] = new Button
                {
                    Text = names[i],
                    Location = new Point(24, 40 + i * 75),
                    Size = new Size(230, 55),
                    Font = new Font("Tahoma", 11F, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(52, 73, 94),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                buttons[i].FlatAppearance.BorderColor = Color.FromArgb(41, 128, 185);
                grpMainItems.Controls.Add(buttons[i]);
            }

            btnBearing = buttons[0];
            btnAlarm = buttons[1];
            btnHarmonics = buttons[2];
            btnSettings = buttons[3];
            btnExit = buttons[4];
            btnExit.BackColor = Color.FromArgb(192, 57, 43);

            btnBearing.Click += (s, e) => tabControl1.SelectedTab = tabBearingSelect;
            btnAlarm.Click += (s, e) => tabControl1.SelectedTab = tabAlarmMonitor;
            btnHarmonics.Click += (s, e) => tabControl1.SelectedTab = tabHarmonics;
            btnSettings.Click += (s, e) => tabControl1.SelectedTab = tabSettings;
            btnExit.Click += (s, e) => this.Close();

            tabStart.Controls.Add(grpMainItems);

            PictureBox picLogo = new PictureBox
            {
                Location = new Point(500, 40),
                Size = new Size(450, 350),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.None
            };

            try
            {
                string imagePath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "EES.png");

                if (System.IO.File.Exists(imagePath))
                {
                    picLogo.Image = Image.FromFile(imagePath);
                    Logger.Info($"Logo loaded from: {imagePath}");
                }
                else
                {
                    Logger.Warn($"Logo not found at: {imagePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load logo: {ex.Message}", ex);
            }

            tabStart.Controls.Add(picLogo);

            var lblTitle = new Label
            {
                Text = "Bearing Fault Diagnosis Using Motor\n Current Signature Analysis",
                Location = new Point(480, 360),
                Size = new Size(500, 150),
                Font = new Font("Tahoma", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185),
                TextAlign = ContentAlignment.MiddleCenter
            };
            tabStart.Controls.Add(lblTitle);

            var lblExplain = new Label
            {
                Text =
                "Motor  Current  Signature  Analysis (MCSA) is  a predictive  maintenance  technique used to\n" +
                "monitor and diagnose the condition of induction motors without interrupting their  operation.\n" +
                "It  functions  by  analyzing  the  stator current  in  the frequency  domain to  identify unique\n" +
                "signatures or harmonics associated with specific mechanical or electrical faults.",
                Location = new Point(50, 520),
                Size = new Size(1050, 400),
                Font = new Font("Times New Roman", 18F, FontStyle.Italic),
                ForeColor = Color.FromArgb(0, 32, 96),
                TextAlign = ContentAlignment.TopLeft
            };
            tabStart.Controls.Add(lblExplain);
        }

        // ─────────────────────────────────────────────────────────────
        // Bearing Select Tab
        // ─────────────────────────────────────────────────────────────

        private void BuildBearingSelectTab()
        {
            grpBearingType = new GroupBox
            {
                Text = "Bearing Type",
                Location = new Point(30, 20),
                Size = new Size(320, 580),
                Font = new Font("Tahoma", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };

            lblSelectBear = CreateLabel("Select BearNB:", 15, 30, grpBearingType);
            cmbBearingSelect = new ComboBox
            {
                Location = new Point(150, 28),
                Size = new Size(150, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Tahoma", 10F),
                Enabled = false
            };
            cmbBearingSelect.SelectedIndexChanged += ComboBox1_SelectedIndexChanged;
            grpBearingType.Controls.Add(cmbBearingSelect);

            string[] fieldNames = { "ID", "BearNB", "NB", "BD", "PD", "PHI",
                                    "BPFO", "BPFI", "FTF", "BSF" };
            TextBox[] fieldBoxes = new TextBox[fieldNames.Length];

            for (int i = 0; i < fieldNames.Length; i++)
            {
                CreateLabel(fieldNames[i] + ":", 15, 70 + i * 35, grpBearingType);
                fieldBoxes[i] = new TextBox
                {
                    Location = new Point(150, 68 + i * 35),
                    Size = new Size(150, 28),
                    Font = new Font("Tahoma", 10F),
                    ReadOnly = true
                };
                grpBearingType.Controls.Add(fieldBoxes[i]);
            }

            txtID = fieldBoxes[0]; txtBea = fieldBoxes[1]; txtNB = fieldBoxes[2];
            txtBD = fieldBoxes[3]; txtPD = fieldBoxes[4]; txtPHI = fieldBoxes[5];
            txtBPFO = fieldBoxes[6]; txtBPFI = fieldBoxes[7];
            txtFTF = fieldBoxes[8]; txtBSF = fieldBoxes[9];

            btnEnableBearing = new Button
            {
                Text = "Enable Edit",
                Location = new Point(30, 430),
                Size = new Size(130, 45),
                BackColor = Color.IndianRed,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Popup,
                Font = new Font("Tahoma", 10F, FontStyle.Bold)
            };
            btnEnableBearing.Click += BtnEnableBearing_Click;
            grpBearingType.Controls.Add(btnEnableBearing);
            tabBearingSelect.Controls.Add(grpBearingType);

            grpBearingDatabase = new GroupBox
            {
                Text = "Bearing Types Database",
                Location = new Point(370, 20),
                Size = new Size(680, 580),
                Font = new Font("Tahoma", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };

            dgvBearings = new DataGridView
            {
                Location = new Point(10, 30),
                Size = new Size(650, 530),
                Font = new Font("Tahoma", 9F),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            grpBearingDatabase.Controls.Add(dgvBearings);
            tabBearingSelect.Controls.Add(grpBearingDatabase);
        }

        // ─────────────────────────────────────────────────────────────
        // Report Tab — 12 features match main.c BL_* (baseline CALIB order)
        // ─────────────────────────────────────────────────────────────

        private sealed class MainCFeatureDescriptor
        {
            public readonly int BlIndex;
            public readonly int DetectionPath;
            public readonly string StreamKey;
            public readonly string ShortName;
            public readonly string MethodName;
            public readonly string MainCPathNote;
            public readonly string UsbCsvCommand;
            public readonly Func<TelemetryData, float> GetIndex;
            public readonly bool IsBearingPf;

            public MainCFeatureDescriptor(
                int blIndex, int detectionPath, string streamKey, string shortName,
                string methodName, string mainCPathNote, string usbCsv,
                Func<TelemetryData, float> getIndex, bool isBearingPf)
            {
                BlIndex = blIndex;
                DetectionPath = detectionPath;
                StreamKey = streamKey;
                ShortName = shortName;
                MethodName = methodName;
                MainCPathNote = mainCPathNote;
                UsbCsvCommand = usbCsv;
                GetIndex = getIndex;
                IsBearingPf = isBearingPf;
            }

            public bool HasHarmonicsPlot => !string.IsNullOrEmpty(UsbCsvCommand);
        }

        /// <summary>
        /// 15 main.c techniques: 11 MCSA detection paths (incl. Wavelet, Welch, Coherence)
        /// + 4 bearing partial-fault indices. BlIndex ≥ 0 marks the 12 CALIB/SAVEBASE baseline
        /// slots; Wavelet/Welch/Coherence are computed indices outside the baseline (BlIndex = -1).
        /// </summary>
        private static readonly MainCFeatureDescriptor[] MainCTechniqueFeatures =
        {
            new MainCFeatureDescriptor(0, 1, "LS", "Fourier", "Fourier LS",
                "Kim et al. TIE 2013 sinusoid LS", "FFTCSV", t => t.Index_LS, false),
            new MainCFeatureDescriptor(1, 2, "MI", "MUSIC", "MUSIC steering",
                "Complex-exponential MUSIC on residual", "MUSICCSV", t => t.Index_Music, false),
            new MainCFeatureDescriptor(2, 10, "ES", "ESPRIT", "ESPRIT fusion",
                "Per-phase max LS + TLS-ESPRIT", "ESPRITCSV", t => t.Index_Esprit, false),
            new MainCFeatureDescriptor(3, 3, "TK", "Teager", "Teager-Kaiser",
                "AM/FM stress on mean current + LS", null, t => t.Index_Teager, false),
            new MainCFeatureDescriptor(4, 5, "SK", "Kurtogram", "Spectral kurtosis",
                "Fast kurtogram → envelope LS (Antoni 2007)", "SKCSV", t => t.Index_SK, false),
            new MainCFeatureDescriptor(-1, 6, "WV", "Wavelet", "Wavelet DWT",
                "DB4 DWT, 5 levels, periodic extension (path 6)", "WAVELETCSV", t => t.Index_Wavelet, false),
            new MainCFeatureDescriptor(5, 7, "CY", "Cyclic2", "Cyclostationary",
                "2nd-order MCSA |FFT(x²)| vs baseline", "CYCLIC2CSV", t => t.Index_Cyclic, false),
            new MainCFeatureDescriptor(6, 8, "SB", "Sideband", "Supply sideband",
                "LS at f_line ± k·f_fault", null, t => t.Index_Sideband, false),
            new MainCFeatureDescriptor(7, 9, "ACF", "Env-ACF", "Envelope ACF",
                "Squared-envelope ACF peak at fault period", null, t => t.Index_EnvAcf, false),
            new MainCFeatureDescriptor(-1, 13, "WL", "Welch", "Welch PSD",
                "50%-overlap Hamming PSD, per-phase max (path 13)", "WELCHCSV", t => t.Index_Welch, false),
            new MainCFeatureDescriptor(-1, 14, "COH", "Coherence", "Spectral coh.",
                "min |Cxy|² over phase pairs (path 14)", "COHCSV", t => t.Index_Coherence, false),
            new MainCFeatureDescriptor(8, 0, "PF_O", "BPFO", "Outer race PF",
                "Partial fault index — outer race", null, t => t.Index_Bpfo, true),
            new MainCFeatureDescriptor(9, 0, "PF_I", "BPFI", "Inner race PF",
                "Partial fault index — inner race", null, t => t.Index_Bpfi, true),
            new MainCFeatureDescriptor(10, 0, "PF_B", "BSF", "Ball PF",
                "Partial fault index — ball spin", null, t => t.Index_Bsf, true),
            new MainCFeatureDescriptor(11, 0, "PF_T", "FTF", "Cage PF",
                "Partial fault index — cage / FTF", null, t => t.Index_Ftf, true),
        };

        private sealed class StatorBaselineDescriptor
        {
            public readonly int SbIndex;
            public readonly string StreamKey;
            public readonly string MethodName;
            public readonly string Note;
            public readonly float Early;
            public readonly float Warn;
            public readonly float Alarm;
            public readonly Func<TelemetryData, float> GetValue;
            public readonly bool ValueIsPercent;

            public StatorBaselineDescriptor(
                int sbIndex, string streamKey, string methodName, string note,
                float early, float warn, float alarm,
                Func<TelemetryData, float> getValue, bool valueIsPercent = false)
            {
                SbIndex = sbIndex;
                StreamKey = streamKey;
                MethodName = methodName;
                Note = note;
                Early = early;
                Warn = warn;
                Alarm = alarm;
                GetValue = getValue;
                ValueIsPercent = valueIsPercent;
            }
        }

        /// <summary>10 stator baseline slots (main.c SB_* = CALIBST / SAVESTST).</summary>
        private static readonly StatorBaselineDescriptor[] MainCStatorBaseline =
        {
            new StatorBaselineDescriptor(0, "NSR", "NSR", "Negative-seq @ f/3f/5f",
                0.012f, 0.028f, 0.085f, t => t.Stator_NSR),
            new StatorBaselineDescriptor(1, "ZSR", "ZSR", "Zero-seq (ground path)",
                0.010f, 0.024f, 0.075f, t => t.Stator_ZSR),
            new StatorBaselineDescriptor(2, "NSR_TD", "NSR-TD", "NSR time-domain",
                0.010f, 0.022f, 0.070f, t => t.Stator_NsrTd),
            new StatorBaselineDescriptor(3, "ZSR_TD", "ZSR-TD", "ZSR time-domain",
                0.008f, 0.020f, 0.065f, t => t.Stator_ZsrTd),
            new StatorBaselineDescriptor(4, "IMB_PCT", "Imbalance", "Phase imbalance %",
                2.5f, 4.5f, 10.0f, t => t.Stator_Imbalance, true),
            new StatorBaselineDescriptor(5, "HARM", "Harmonics", "Odd-harmonic ratio",
                0.032f, 0.048f, 0.115f, t => t.Stator_HarmRatio),
            new StatorBaselineDescriptor(6, "RESID", "Residual", "Ground residual ratio",
                0.006f, 0.009f, 0.030f, t => t.Stator_ResidRatio),
            new StatorBaselineDescriptor(7, "ZSR_H3", "ZSR-H3", "3rd-harmonic ZSR",
                0.028f, 0.042f, 0.115f, t => t.Stator_ZSR_H3),
            new StatorBaselineDescriptor(8, "ODD", "Odd harmonics", "Odd-harm index",
                0.028f, 0.045f, 0.110f, t => t.Stator_OddHarm),
            new StatorBaselineDescriptor(9, "PS_DEG", "Phase spread", "Phase spread °",
                4.0f, 7.0f, 15.0f, t => t.Stator_PhaseSpreadDeg),
        };

        private void BuildAlarmMonitorTab()
        {
            tabAlarmMonitor.Padding = new Padding(10);
            tabAlarmMonitor.BackColor = Color.FromArgb(245, 247, 250);
            tabAlarmMonitor.AutoScroll = true;

            pnlReportHeader = new ReportHeaderPanel
            {
                Location = new Point(10, 8),
                Size = new Size(1040, 96),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            FlowLayoutPanel reportCmdPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 112),
                Size = new Size(1040, 38),
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            btnReportFullReport = MakeUsbActionButton("REPORT", Color.FromArgb(41, 128, 185));
            btnReportFullReport.Click += async (s, e) => await RunUsbFullReportAsync();
            btnReportGraphData = MakeUsbActionButton("GRAPHDATA", Color.FromArgb(52, 73, 94));
            btnReportGraphData.Click += async (s, e) => await RunUsbGraphDataAsync();
            btnReportPhaseCsv = MakeUsbActionButton("PHASE CSV", Color.FromArgb(52, 73, 94));
            btnReportPhaseCsv.Click += async (s, e) => await RunUsbPhaseCsvAsync();

            btnReportRefresh = MakeUsbActionButton("Refresh View", Color.FromArgb(127, 140, 141));
            btnReportRefresh.Click += (s, e) => UpdateReportPageFromCurrentData();

            btnReportExport = MakeUsbActionButton("Export TXT", Color.FromArgb(39, 174, 96));
            btnReportExport.Click += (s, e) => ExportDiagnosticReport();

            btnReportCopy = MakeUsbActionButton("Copy", Color.FromArgb(52, 73, 94));
            btnReportCopy.Click += (s, e) => CopyDiagnosticReportToClipboard();

            chkReportShowReference = new CheckBox
            {
                Text = "Append USB command reference",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(70, 80, 95),
                Margin = new Padding(12, 8, 0, 0)
            };
            chkReportShowReference.CheckedChanged += (s, e) => UpdateReportPageFromCurrentData();

            reportCmdPanel.Controls.AddRange(new Control[] {
                btnReportFullReport, btnReportGraphData, btnReportPhaseCsv,
                btnReportRefresh, btnReportExport, btnReportCopy, chkReportShowReference });

            pnlReportCards = new Panel
            {
                Location = new Point(10, 154),
                Size = new Size(1040, 88),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            cardReportOverall = CreateReportMetricCard("OVERALL", 0);
            cardReportBpfo = CreateReportMetricCard("BPFO", 1);
            cardReportBpfi = CreateReportMetricCard("BPFI", 2);
            cardReportBsf = CreateReportMetricCard("BSF / FTF", 3);
            cardReportStator = CreateReportMetricCard("STATOR", 4);
            pnlReportCards.Controls.AddRange(new Control[] {
                cardReportOverall, cardReportBpfo, cardReportBpfi, cardReportBsf, cardReportStator });

            lblReport12Caption = new Label
            {
                Text = "15 H750 techniques (main.c detection paths 1–14) — 11 MCSA detection methods + 4 bearing partial-fault (teal)",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                Location = new Point(12, 246)
            };

            pnlReport12Techniques = new Panel
            {
                Location = new Point(10, 266),
                Size = new Size(1040, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _reportTechniqueTiles = new ReportTechniqueTile[MainCTechniqueFeatures.Length];
            for (int i = 0; i < MainCTechniqueFeatures.Length; i++)
            {
                _reportTechniqueTiles[i] = new ReportTechniqueTile { FeatureIndex = i };
                pnlReport12Techniques.Controls.Add(_reportTechniqueTiles[i]);
            }
            pnlReport12Techniques.Paint += DrawTechniqueGroupLabels;

            pnlStatorHeader = new StatorSectionHeaderPanel
            {
                Location = new Point(10, 470),
                Size = new Size(1040, 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            pnlReportStatorAlarms = new Panel
            {
                Location = new Point(10, 514),
                Size = new Size(1040, 92),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            cardReportStatorShort = new StatorFaultCard
            {
                CardTitle = "STATOR INTER-TURN SHORT",
                Location = new Point(4, 4),
                Size = new Size(512, 84)
            };
            cardReportStatorGround = new StatorFaultCard
            {
                CardTitle = "STATOR GROUND / EARTH FAULT",
                Location = new Point(524, 4),
                Size = new Size(512, 84)
            };
            pnlReportStatorAlarms.Controls.AddRange(new Control[] { cardReportStatorShort, cardReportStatorGround });

            pnlReportStatorMetrics = new Panel
            {
                Location = new Point(10, 612),
                Size = new Size(1040, 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _reportStatorTiles = new StatorMetricTile[MainCStatorBaseline.Length];
            for (int i = 0; i < MainCStatorBaseline.Length; i++)
            {
                StatorBaselineDescriptor d = MainCStatorBaseline[i];
                _reportStatorTiles[i] = new StatorMetricTile
                {
                    SbIndex = d.SbIndex,
                    StreamKey = d.StreamKey,
                    MethodName = d.MethodName,
                    Note = d.Note,
                    Early = d.Early,
                    Warn = d.Warn,
                    Alarm = d.Alarm,
                    ValueIsPercent = d.ValueIsPercent
                };
                pnlReportStatorMetrics.Controls.Add(_reportStatorTiles[i]);
            }

            rtbTechniqueReport = new RichTextBox
            {
                Location = new Point(10, 740),
                Size = new Size(1040, 280),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Consolas", 9.75F),
                ReadOnly = true,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(35, 45, 55),
                BorderStyle = BorderStyle.FixedSingle,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both
            };

            tabAlarmMonitor.Controls.Add(pnlReportHeader);
            tabAlarmMonitor.Controls.Add(reportCmdPanel);
            tabAlarmMonitor.Controls.Add(pnlReportCards);
            tabAlarmMonitor.Controls.Add(lblReport12Caption);
            tabAlarmMonitor.Controls.Add(pnlReport12Techniques);
            tabAlarmMonitor.Controls.Add(pnlStatorHeader);
            tabAlarmMonitor.Controls.Add(pnlReportStatorAlarms);
            tabAlarmMonitor.Controls.Add(pnlReportStatorMetrics);
            tabAlarmMonitor.Controls.Add(rtbTechniqueReport);

            tabAlarmMonitor.Resize += ReportTab_Resize;
            ReportTab_Resize(tabAlarmMonitor, EventArgs.Empty);
            UpdateReportPageFromCurrentData();
        }

        private static ReportMetricCard CreateReportMetricCard(string title, int index)
        {
            return new ReportMetricCard
            {
                CardTitle = title,
                Location = new Point(4 + index * 206, 4),
                Size = new Size(200, 80)
            };
        }

        // Technique grid layout: 11 MCSA detection tiles (6 cols × 2 rows) + 4 bearing tiles on their own row.
        private const int TechCols = 6;
        private const int TechGap = 6;
        private const int TechLabelBand = 18;
        private const int TechDetRows = 2;
        private const int TechBearingStart = 11;

        private static int TechTileWidth(int panelWidth)
        {
            return Math.Max(110, (panelWidth - TechGap * (TechCols + 1)) / TechCols);
        }

        private static int TechTileHeight(int panelHeight)
        {
            return Math.Max(54, (panelHeight - TechGap * (TechDetRows + 2) - TechLabelBand) / (TechDetRows + 1));
        }

        /// <summary>Positions the 15 technique tiles: detection grid on top, bearing row below the divider.</summary>
        private void LayoutTechniqueGrid(int panelWidth, int panelHeight)
        {
            if (_reportTechniqueTiles == null) return;
            int tileW = TechTileWidth(panelWidth);
            int tileH = TechTileHeight(panelHeight);
            int brgTop = TechGap + TechDetRows * (tileH + TechGap) + TechLabelBand;

            for (int i = 0; i < _reportTechniqueTiles.Length; i++)
            {
                var tile = _reportTechniqueTiles[i];
                int x, y;
                if (i < TechBearingStart)
                {
                    int col = i % TechCols;
                    int row = i / TechCols;
                    x = TechGap + col * (tileW + TechGap);
                    y = TechGap + row * (tileH + TechGap);
                }
                else
                {
                    int col = i - TechBearingStart;
                    x = TechGap + col * (tileW + TechGap);
                    y = brgTop;
                }
                tile.Location = new Point(x, y);
                tile.Size = new Size(tileW, tileH);
            }
        }

        /// <summary>Draws the "MCSA detection" / "bearing partial-fault" group captions in the divider band.</summary>
        private void DrawTechniqueGroupLabels(object sender, PaintEventArgs e)
        {
            var panel = sender as Panel;
            if (panel == null) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int tileW = TechTileWidth(panel.ClientSize.Width);
            int tileH = TechTileHeight(panel.ClientSize.Height);
            int bandY = TechGap + TechDetRows * (tileH + TechGap);

            int brgX0 = TechGap;
            int brgX1 = TechGap + 3 * (tileW + TechGap) + tileW;
            int detX0 = TechGap + 4 * (tileW + TechGap);
            int detX1 = TechGap + 5 * (tileW + TechGap) + tileW;

            Color teal = Color.FromArgb(22, 160, 133);
            using (var detFont = new Font("Segoe UI", 7F, FontStyle.Bold))
            using (var brgFont = new Font("Segoe UI", 7.25F, FontStyle.Bold))
            using (var detBr = new SolidBrush(Color.FromArgb(120, 130, 144)))
            using (var brgBr = new SolidBrush(teal))
            using (var pen = new Pen(teal, 1.4f))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawLine(pen, brgX0 + 2, bandY + 9, brgX1 - 2, bandY + 9);
                g.DrawString("BEARING PARTIAL-FAULT  —  BPFO · BPFI · BSF · FTF",
                    brgFont, brgBr, new RectangleF(brgX0, bandY + 1, brgX1 - brgX0, 15), center);

                g.DrawString("↑  11 MCSA DETECTION METHODS",
                    detFont, detBr, new RectangleF(detX0, bandY + 1, detX1 - detX0, 15), center);
            }
        }

        private void ReportTab_Resize(object sender, EventArgs e)
        {
            int w = Math.Max(500, tabAlarmMonitor.ClientSize.Width - 20);
            const int sectionGap = 10;
            const int statorHeaderH = 40;
            const int statorAlarmsH = 92;
            const int statorMetricsH = 120;
            const int reportTextH = 240;

            if (pnlReportHeader != null)
                pnlReportHeader.Width = w;

            if (pnlReportCards != null)
            {
                pnlReportCards.Width = w;
                int cardW = Math.Max(120, (w - 24) / 5);
                int x = 4;
                foreach (Control c in pnlReportCards.Controls)
                {
                    if (c is ReportMetricCard card)
                    {
                        card.Location = new Point(x, 4);
                        card.Width = cardW;
                        x += cardW + 4;
                    }
                }
            }

            int techniquesPanelH = 220;
            if (pnlReport12Techniques != null && _reportTechniqueTiles != null)
            {
                pnlReport12Techniques.Width = w;
                int tileH = TechTileHeight(techniquesPanelH);
                int brgBottom = TechGap + TechDetRows * (tileH + TechGap) + TechLabelBand + tileH + TechGap;
                techniquesPanelH = brgBottom;
                pnlReport12Techniques.Height = techniquesPanelH;
                LayoutTechniqueGrid(w, techniquesPanelH);
                pnlReport12Techniques.Invalidate();
            }

            int y = (pnlReport12Techniques != null)
                ? pnlReport12Techniques.Bottom + sectionGap
                : 476;

            if (pnlStatorHeader != null)
            {
                pnlStatorHeader.Location = new Point(10, y);
                pnlStatorHeader.Size = new Size(w, statorHeaderH);
                y = pnlStatorHeader.Bottom + sectionGap;
            }

            if (pnlReportStatorAlarms != null)
            {
                pnlReportStatorAlarms.Width = w;
                pnlReportStatorAlarms.Height = statorAlarmsH;
                pnlReportStatorAlarms.Location = new Point(10, y);
                int half = Math.Max(200, (w - 12) / 2);
                if (cardReportStatorShort != null)
                {
                    cardReportStatorShort.Location = new Point(4, 4);
                    cardReportStatorShort.Size = new Size(half, statorAlarmsH - 8);
                }
                if (cardReportStatorGround != null)
                {
                    cardReportStatorGround.Location = new Point(8 + half, 4);
                    cardReportStatorGround.Size = new Size(half, statorAlarmsH - 8);
                }
                y = pnlReportStatorAlarms.Bottom + sectionGap;
            }

            if (pnlReportStatorMetrics != null && _reportStatorTiles != null)
            {
                pnlReportStatorMetrics.Width = w;
                pnlReportStatorMetrics.Height = statorMetricsH;
                pnlReportStatorMetrics.Location = new Point(10, y);
                const int cols = 5;
                const int rows = 2;
                int gap = 4;
                int tileW = Math.Max(90, (w - gap * (cols + 1)) / cols);
                int tileH = Math.Max(44, (statorMetricsH - gap * (rows + 1)) / rows);
                for (int i = 0; i < _reportStatorTiles.Length; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    var tile = _reportStatorTiles[i];
                    tile.Location = new Point(gap + col * (tileW + gap), gap + row * (tileH + gap));
                    tile.Size = new Size(tileW, tileH);
                }
                y = pnlReportStatorMetrics.Bottom + sectionGap;
            }

            if (rtbTechniqueReport != null)
            {
                rtbTechniqueReport.Width = w;
                rtbTechniqueReport.Location = new Point(10, y);
                rtbTechniqueReport.Height = reportTextH;
                y = rtbTechniqueReport.Bottom + sectionGap;
            }

            tabAlarmMonitor.AutoScrollMinSize = new Size(0, y);
        }

        // ─────────────────────────────────────────────────────────────
        // Harmonics Tab — Three charts: Phase 1 / Phase 2 / Phase 3
        // Technique buttons: Fourier, MUSIC, ESPRIT, Cyclostationary
        // ───────��─────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────
        // Harmonics Tab — Three charts: Phase 1 / Phase 2 / Phase 3
        // Technique buttons: Fourier, MUSIC, ESPRIT, Cyclostationary
        // Each technique button requests a report then displays its data.
        // ─────────────────────────────────────────────────────────────

        private void BuildHarmonicsTab()
        {
            const int controlBarHeight = 74;
            const int statusBarHeight = 58;
            const int chartPadding = 4;
            const int topMargin = 5;

            Color[] phaseColors = { Color.Black,
                                    Color.FromArgb(41, 128, 185),
                                    Color.FromArgb(192, 57, 43) };

            if (_harmonicsStatusToolTip == null)
                _harmonicsStatusToolTip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 300, ShowAlways = true };

            BuildHarmonicsStatusBar(chartPadding, topMargin + 2, statusBarHeight);

            pnlHarmonicsSpectrumFrame = new Panel
            {
                BackColor = Color.FromArgb(236, 240, 245),
                Padding = new Padding(0, 0, 0, 0)
            };
            pnlHarmonicsSpectrumFrame.Paint += HarmonicsSpectrumFrame_Paint;

            _xyChart1 = new MyChartClass { PhaseName = "" };
            _xyChart2 = new MyChartClass { PhaseName = "Phase 2" };
            _xyChart3 = new MyChartClass { PhaseName = "Phase 3" };

            lblPhase1 = new Label
            {
                Text = "Combined Engineering Plot — select a technique",
                Visible = true,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 47, 61),
                BackColor = Color.FromArgb(224, 230, 238),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            };

            lblPhase2 = new Label
            {
                Text = "Phase 2",
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                ForeColor = phaseColors[1],
                AutoSize = true
            };

            lblPhase3 = new Label
            {
                Text = "Phase 3",
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                ForeColor = phaseColors[2],
                AutoSize = true
            };

            _xyChart1.SetSeriesColor(phaseColors[0]);
            _xyChart2.Visible = false;
            _xyChart3.Visible = false;
            lblPhase2.Visible = false;
            lblPhase3.Visible = false;

            pnlHarmonicsSpectrumFrame.Controls.Add(lblPhase1);
            lblPhase1.Dock = DockStyle.Top;
            pnlHarmonicsSpectrumFrame.Controls.Add(_xyChart1);
            _xyChart1.Dock = DockStyle.Fill;
            _xyChart1.MaximizeHarmonicsPlotArea();
            _xyChart1.EnsureHarmonicsPlotReady(2500);
            tabHarmonics.Controls.Add(pnlHarmonicsSpectrumFrame);
            tabHarmonics.Controls.Add(lblPhase2);
            tabHarmonics.Controls.Add(_xyChart2);
            tabHarmonics.Controls.Add(lblPhase3);
            tabHarmonics.Controls.Add(_xyChart3);

            MyChartClass[] charts = { _xyChart1, _xyChart2, _xyChart3 };

            Action layoutCharts = () =>
            {
                int tabW = tabHarmonics.ClientSize.Width;
                int tabH = tabHarmonics.ClientSize.Height;
                int chartsW = tabW - 2 * chartPadding;
                if (chartsW < 320) chartsW = 320;

                int availableHeight = tabH - controlBarHeight - topMargin - statusBarHeight - 8;
                int smartPanelHeight = Math.Max(120, Math.Min(180, availableHeight / 3));
                int chartHeight = availableHeight - chartPadding - chartPadding - smartPanelHeight;
                if (chartHeight < 40) chartHeight = 40;

                int y = topMargin + statusBarHeight + 6;
                if (pnlHarmonicsSpectrumFrame != null)
                {
                    pnlHarmonicsSpectrumFrame.Location = new Point(chartPadding, y);
                    pnlHarmonicsSpectrumFrame.Size = new Size(chartsW, chartHeight);
                }
                y += chartHeight + chartPadding;

                if (grpSmartEngineering != null)
                {
                    grpSmartEngineering.Visible = true;
                    grpSmartEngineering.Location = new Point(chartPadding, y);
                    grpSmartEngineering.Size = new Size(chartsW, smartPanelHeight);
                }
            };

            tabHarmonics.Resize += (s, e) => layoutCharts();
            tabHarmonics.Layout += (s, e) => layoutCharts();
            layoutCharts();

            grpHarmonicControl = new GroupBox
            {
                Text = "Spectral Technique (H750 USB — main.c)",
                Size = new Size(860, 64),
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            Action layoutControlBar = () =>
            {
                int tabW = tabHarmonics.ClientSize.Width;
                int controlW = tabW - 20;
                if (controlW < 400) controlW = 400;

                grpHarmonicControl.Location = new Point(
                    10,
                    tabHarmonics.ClientSize.Height - controlBarHeight + 5);
                grpHarmonicControl.Size = new Size(controlW, 64);
            };
            tabHarmonics.Resize += (s, e) => layoutControlBar();
            layoutControlBar();

            // ── Technique buttons — each requests its main.c CSV export, then displays ──
            // Built from one factory so every technique button is laid out identically.
            Func<string, SpectralTechnique, Button> makeTechButton = (text, tech) =>
            {
                Button b = new Button
                {
                    Text = text,
                    Size = new Size(96, 34),
                    Font = new Font("Tahoma", 9F, FontStyle.Bold),
                    BackColor = Color.FromArgb(52, 73, 94),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat
                };
                b.FlatAppearance.BorderColor = Color.FromArgb(30, 45, 60);
                b.Click += async (s, e) => await SelectTechnique(tech);
                return b;
            };

            btnTechFourier = makeTechButton("Fourier", SpectralTechnique.Fourier);
            btnTechMusic = makeTechButton("MUSIC", SpectralTechnique.Music);
            btnTechEsprit = makeTechButton("ESPRIT", SpectralTechnique.Esprit);
            btnTechCyclo = makeTechButton("CYCLIC2", SpectralTechnique.Cyclostationary);
            btnTechSk = makeTechButton("SK", SpectralTechnique.Sk);
            btnTechWavelet = makeTechButton("WAVELET", SpectralTechnique.Wavelet);
            btnTechWelch = makeTechButton("WELCH", SpectralTechnique.Welch);
            btnTechCoh = makeTechButton("COHERENCE", SpectralTechnique.Coherence);

            Button[] techButtons =
            {
                btnTechFourier, btnTechMusic, btnTechEsprit, btnTechCyclo,
                btnTechSk, btnTechWavelet, btnTechWelch, btnTechCoh
            };
            int techX = 10;
            for (int i = 0; i < techButtons.Length; i++)
            {
                techButtons[i].Location = new Point(techX, 22);
                techX += techButtons[i].Width + 5;
            }

            grpHarmonicControl.Controls.AddRange(techButtons);
            tabHarmonics.Controls.Add(grpHarmonicControl);
            UpdateTechniqueButtonColors();

            grpSmartEngineering = new GroupBox
            {
                Text = "Smart Engineering View",
                Font = new Font("Tahoma", 9.5F, FontStyle.Bold),
                Location = new Point(560, topMargin),
                Size = new Size(320, 380),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
            };

            btnSmartViewRefresh = new Button
            {
                Text = "Refresh View",
                Location = new Point(10, 22),
                Size = new Size(120, 32),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(41, 128, 185),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSmartViewRefresh.Click += (s, e) => UpdateSmartEngineeringView();

            btnSmartViewExport = new Button
            {
                Text = "Export Report",
                Location = new Point(135, 22),
                Size = new Size(120, 32),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSmartViewExport.Click += (s, e) => ExportSmartEngineeringReport();

            rtbSmartEngineering = new RichTextBox
            {
                Location = new Point(10, 60),
                Size = new Size(300, 300),
                ReadOnly = true,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9F),
                WordWrap = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            grpSmartEngineering.Controls.Add(btnSmartViewRefresh);
            grpSmartEngineering.Controls.Add(btnSmartViewExport);
            grpSmartEngineering.Controls.Add(rtbSmartEngineering);
            grpSmartEngineering.Resize += (s, e) =>
            {
                if (rtbSmartEngineering != null)
                {
                    rtbSmartEngineering.Size = new Size(
                        Math.Max(120, grpSmartEngineering.ClientSize.Width - 20),
                        Math.Max(120, grpSmartEngineering.ClientSize.Height - 70));
                }
            };
            tabHarmonics.Controls.Add(grpSmartEngineering);

            _xyChart1.SetUpdateInterval(50);
            _xyChart2.SetUpdateInterval(50);
            _xyChart3.SetUpdateInterval(50);

            UpdateSmartEngineeringView();
            UpdateTechniqueDataLeds(_lastFrame);
        }

        private static void HarmonicsSpectrumFrame_Paint(object sender, PaintEventArgs e)
        {
            var panel = sender as Panel;
            if (panel == null) return;
            Rectangle r = panel.ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            using (var border = new Pen(Color.FromArgb(120, 130, 145), 1.5f))
                e.Graphics.DrawRectangle(border, r);
            var titleRect = new Rectangle(6, 2, 140, 14);
            using (var font = new Font("Segoe UI", 7.5F, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromArgb(55, 65, 80)))
            using (var bg = new SolidBrush(Color.FromArgb(245, 247, 250)))
            {
                e.Graphics.FillRectangle(bg, titleRect);
                e.Graphics.DrawString("Spectral figure", font, br, titleRect);
            }
        }

        /// <summary>Bearing PF_* + technique cache status chips (Harmonics tab).</summary>
        private void BuildHarmonicsStatusBar(int x, int y, int height)
        {
            Panel strip = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(900, height),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(241, 244, 248)
            };
            strip.Paint += (s, pe) =>
            {
                Rectangle r = strip.ClientRectangle;
                r.Width -= 1;
                r.Height -= 1;
                using (var pen = new Pen(Color.FromArgb(185, 192, 202), 1.5f))
                    pe.Graphics.DrawRectangle(pen, r);
            };

            int lx = 10;
            AddHarmonicsSectionLabel(strip, "BEARING", ref lx);
            chipBpfo = AddHarmonicsStatusChip(strip, "BPFO", HarmonicsStatusChip.ChipKind.Bearing,
                "Outer race PF_O vs WTH/FTH", ref lx);
            chipBpfi = AddHarmonicsStatusChip(strip, "BPFI", HarmonicsStatusChip.ChipKind.Bearing,
                "Inner race PF_I", ref lx);
            chipBsf = AddHarmonicsStatusChip(strip, "BSF", HarmonicsStatusChip.ChipKind.Bearing,
                "Ball spin PF_B", ref lx);
            chipFtf = AddHarmonicsStatusChip(strip, "FTF", HarmonicsStatusChip.ChipKind.Bearing,
                "Cage / FTF PF_T", ref lx);

            lx += 8;
            AddHarmonicsStripDivider(strip, ref lx);
            AddHarmonicsSectionLabel(strip, "ANALYSIS", ref lx);

            chipFft = AddHarmonicsStatusChip(strip, "FFT", HarmonicsStatusChip.ChipKind.Technique,
                "FFT spectrum in frame", ref lx);
            chipMusic = AddHarmonicsStatusChip(strip, "MUSIC", HarmonicsStatusChip.ChipKind.Technique,
                "MUSIC spectrum in frame", ref lx);
            chipEsprit = AddHarmonicsStatusChip(strip, "ESPRIT", HarmonicsStatusChip.ChipKind.Technique,
                "ESPRIT impulses in frame", ref lx);
            chipCyclic2 = AddHarmonicsStatusChip(strip, "CYCLIC2", HarmonicsStatusChip.ChipKind.Technique,
                "CYCLIC2 spectrum in frame", ref lx);
            chipSk = AddHarmonicsStatusChip(strip, "SK", HarmonicsStatusChip.ChipKind.Technique,
                "Spectral kurtosis in frame", ref lx);
            chipWavelet = AddHarmonicsStatusChip(strip, "WAVELET", HarmonicsStatusChip.ChipKind.Technique,
                "Wavelet EMEAN in frame", ref lx);
            chipWelch = AddHarmonicsStatusChip(strip, "WELCH", HarmonicsStatusChip.ChipKind.Technique,
                "Welch PSD in frame", ref lx);
            chipCoh = AddHarmonicsStatusChip(strip, "COH", HarmonicsStatusChip.ChipKind.Technique,
                "3-phase coherence in frame", ref lx);

            tabHarmonics.Controls.Add(strip);
            tabHarmonics.Resize += (s, e) => strip.Width = Math.Max(400, tabHarmonics.ClientSize.Width - 2 * x);
        }

        private static void AddHarmonicsSectionLabel(Panel parent, string text, ref int x)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                Location = new Point(x, 20),
                Size = new Size(52, 18),
                Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                ForeColor = Color.FromArgb(110, 118, 130),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            });
            x += 56;
        }

        private static void AddHarmonicsStripDivider(Panel parent, ref int x)
        {
            parent.Controls.Add(new Panel
            {
                Location = new Point(x, 14),
                Size = new Size(1, 34),
                BackColor = Color.FromArgb(200, 206, 214)
            });
            x += 12;
        }

        private HarmonicsStatusChip AddHarmonicsStatusChip(Panel parent, string tag,
            HarmonicsStatusChip.ChipKind kind, string toolTip, ref int x)
        {
            var chip = new HarmonicsStatusChip
            {
                TagCode = tag,
                Mode = kind,
                Location = new Point(x, 12),
                Size = new Size(kind == HarmonicsStatusChip.ChipKind.Technique ? 72 : 64, 40),
                Level = 3
            };
            parent.Controls.Add(chip);
            if (_harmonicsStatusToolTip != null)
                _harmonicsStatusToolTip.SetToolTip(chip, toolTip);
            x += chip.Width + 5;
            return chip;
        }

        /// <summary>
        /// Requests a fresh spectral report from STM32 (if connected),
        /// selects the technique, and redraws charts.
        /// If already have data and STM32 is not connected, just switches view.
        /// </summary>
        /// <summary>Maps main.c MODE= / END_EXPORT tags to UI technique selection.</summary>
        private void SyncTechniqueFromFirmwareMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return;
            string m = mode.Trim().ToUpperInvariant();
            if (m == "FULL") return;

            SpectralTechnique? tech = null;
            if (m == "FOURIER") tech = SpectralTechnique.Fourier;
            else if (m == "MUSIC") tech = SpectralTechnique.Music;
            else if (m == "ESPRIT") tech = SpectralTechnique.Esprit;
            else if (m == "CYCLIC2") tech = SpectralTechnique.Cyclostationary;
            else if (m == "SK") tech = SpectralTechnique.Sk;
            else if (m == "WAVELET") tech = SpectralTechnique.Wavelet;
            else if (m == "WELCH") tech = SpectralTechnique.Welch;
            else if (m == "COH") tech = SpectralTechnique.Coherence;

            if (tech.HasValue)
            {
                _currentTechnique = tech.Value;
                UpdateTechniqueButtonColors();
            }
        }

        private async Task SelectTechnique(SpectralTechnique tech)
        {
            _currentTechnique = tech;
            UpdateTechniqueButtonColors();
            SetTechniqueButtonsEnabled(false);

            if (_comm != null && _comm.IsConnected)
            {
                string cmd;
                switch (tech)
                {
                    case SpectralTechnique.Fourier: cmd = "FFTCSV"; break;
                    case SpectralTechnique.Music: cmd = "MUSICCSV"; break;
                    case SpectralTechnique.Esprit: cmd = "ESPRITCSV"; break;
                    case SpectralTechnique.Cyclostationary: cmd = "CYCLIC2CSV"; break;
                    case SpectralTechnique.Sk: cmd = "SKCSV"; break;
                    case SpectralTechnique.Wavelet: cmd = "WAVELETCSV"; break;
                    case SpectralTechnique.Welch: cmd = "WELCHCSV"; break;
                    case SpectralTechnique.Coherence: cmd = "COHCSV"; break;
                    default: cmd = "FFTCSV"; break;
                }

                LogUI("Requesting " + cmd + "...", Color.Yellow);

                List<string> lines = await _comm.RequestTechniqueCsv(cmd);

                if (lines != null && lines.Count > 0)
                {
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            _reportParser?.ParseLine(line.Trim());
                    }
                    LogUI(cmd + ": " + lines.Count + " lines received", Color.LimeGreen);
                }
                else
                {
                    LogUI(cmd + ": no data received — wait ~2 s after connect for first capture, then retry",
                        Color.Red);
                }
            }
            else
            {
                LogUI(tech + ": USB not connected — connect H750 first", Color.Orange);
            }

            RefreshSpectralFrameFromParser();
            RedrawChartsForCurrentTechnique();
            UpdateTechniqueDataLeds(_lastFrame);
            SetTechniqueButtonsEnabled(true);
        }

        private void RefreshSpectralFrameFromParser()
        {
            if (_reportParser == null) return;
            SpectralFrame f = _reportParser.GetLastFrame();
            if (f != null)
                _lastFrame = f;
        }

        private void SetTechniqueButtonsEnabled(bool enabled)
        {
            if (btnTechFourier != null) btnTechFourier.Enabled = enabled;
            if (btnTechMusic != null) btnTechMusic.Enabled = enabled;
            if (btnTechEsprit != null) btnTechEsprit.Enabled = enabled;
            if (btnTechCyclo != null) btnTechCyclo.Enabled = enabled;
            if (btnTechSk != null) btnTechSk.Enabled = enabled;
            if (btnTechWavelet != null) btnTechWavelet.Enabled = enabled;
            if (btnTechWelch != null) btnTechWelch.Enabled = enabled;
            if (btnTechCoh != null) btnTechCoh.Enabled = enabled;
            if (btnReportFullReport != null) btnReportFullReport.Enabled = enabled;
            if (btnReportGraphData != null) btnReportGraphData.Enabled = enabled;
            if (btnReportPhaseCsv != null) btnReportPhaseCsv.Enabled = enabled;
        }



        private void UpdateTechniqueButtonColors()
        {
            Color active = Color.FromArgb(41, 128, 185);
            Color inactive = Color.FromArgb(52, 73, 94);

            btnTechFourier.BackColor = (_currentTechnique == SpectralTechnique.Fourier) ? active : inactive;
            btnTechMusic.BackColor = (_currentTechnique == SpectralTechnique.Music) ? active : inactive;
            btnTechEsprit.BackColor = (_currentTechnique == SpectralTechnique.Esprit) ? active : inactive;
            btnTechCyclo.BackColor = (_currentTechnique == SpectralTechnique.Cyclostationary) ? active : inactive;
            if (btnTechSk != null)
                btnTechSk.BackColor = (_currentTechnique == SpectralTechnique.Sk) ? active : inactive;
            if (btnTechWavelet != null)
                btnTechWavelet.BackColor = (_currentTechnique == SpectralTechnique.Wavelet) ? active : inactive;
            if (btnTechWelch != null)
                btnTechWelch.BackColor = (_currentTechnique == SpectralTechnique.Welch) ? active : inactive;
            if (btnTechCoh != null)
                btnTechCoh.BackColor = (_currentTechnique == SpectralTechnique.Coherence) ? active : inactive;
        }


        /// <summary>
        /// Redraws the 3 phase charts using the last received frame
        /// and the currently selected technique.
        /// </summary>
        private void RedrawChartsForCurrentTechnique()
        {
            if (_lastFrame == null)
            {
                LogUI("No spectral frame available — use Full Report or a technique button", Color.Orange);
                UpdateSmartEngineeringView();
                return;
            }

            string techName = "";

            switch (_currentTechnique)
            {
                case SpectralTechnique.Fourier:
                    techName = "Fourier Engineering Spectrum";
                    break;
                case SpectralTechnique.Music:
                    techName = "MUSIC Engineering Spectrum";
                    break;
                case SpectralTechnique.Esprit:
                    techName = "ESPRIT Engineering Spectrum";
                    DisplayEspritThreePhaseOnCharts();
                    return;
                case SpectralTechnique.Cyclostationary:
                    techName = "Cyclostationary Engineering Spectrum";
                    break;
                case SpectralTechnique.Sk:
                    techName = "Spectral Kurtosis (SK)";
                    break;
                case SpectralTechnique.Wavelet:
                    techName = "Wavelet Energy (EMEAN)";
                    break;
                case SpectralTechnique.Welch:
                    techName = "Welch PSD Spectrum";
                    break;
                case SpectralTechnique.Coherence:
                    techName = "3-Phase Coherence (0..1)";
                    break;
            }

            List<PointF> phase1 = GetPointsForPhase(_lastFrame, _currentTechnique, 1);
            List<PointF> phase2 = GetPointsForPhase(_lastFrame, _currentTechnique, 2);
            List<PointF> phase3 = GetPointsForPhase(_lastFrame, _currentTechnique, 3);

            bool hasP1 = phase1 != null && phase1.Count > 0;
            bool hasP2 = phase2 != null && phase2.Count > 0;
            bool hasP3 = phase3 != null && phase3.Count > 0;

            if (!hasP1 && !hasP2 && !hasP3)
            {
                _xyChart1?.ClearData();
                _xyChart1?.SetFaultMarkers(null);
                lblPhase1.Text = "Combined Engineering Plot — " + techName + " (no data)";
                LogUI(techName + ": no spectral data in last frame", Color.Orange);
                UpdateSmartEngineeringView();
                UpdateTechniqueDataLeds(_lastFrame);
                return;
            }

            if (_currentTechnique == SpectralTechnique.Wavelet)
            {
                _xyChart1?.SetAxisTitles("Frequency (Hz)", "Wavelet EMEAN");
                _xyChart1?.SetThreePhaseLinearData(
                    hasP1 ? phase1 : null,
                    hasP2 ? phase2 : null,
                    hasP3 ? phase3 : null);
                _xyChart1?.ShowPeakMarkers(new List<PointF>(), 0);
                _xyChart1?.RefreshEngineeringPlotAppearance();
                _xyChart1?.SetFaultMarkers(BuildFaultMarkers());
                ApplyHarmonicsHeader("Combined Engineering Plot — " + techName, null);
                LogUI("Wavelet: " + CountPoints(phase1, phase2, phase3) + " level(s) plotted", Color.Cyan);
                UpdateSmartEngineeringView();
                UpdateTechniqueDataLeds(_lastFrame);
                return;
            }

            // Coherence is a bounded 0..1 magnitude-squared metric (main.c [COH_CSV]):
            // plot it linearly, not in dB.
            if (_currentTechnique == SpectralTechnique.Coherence)
            {
                _xyChart1?.SetAxisTitles("Frequency (Hz)", "Coherence (0..1)");
                _xyChart1?.SetThreePhaseLinearData(
                    hasP1 ? phase1 : null,
                    hasP2 ? phase2 : null,
                    hasP3 ? phase3 : null);
                _xyChart1?.ShowPeakMarkers(new List<PointF>(), 0);
                _xyChart1?.RefreshEngineeringPlotAppearance();
                _xyChart1?.SetFaultMarkers(BuildFaultMarkers());
                ApplyHarmonicsHeader("Combined Engineering Plot — " + techName, null);
                LogUI("Coherence: plotted " + CountPoints(phase1, phase2, phase3) + " points", Color.Cyan);
                UpdateSmartEngineeringView();
                UpdateTechniqueDataLeds(_lastFrame);
                return;
            }

            List<PointF> dbP1 = ConvertPointsToDb(hasP1 ? phase1 : null);
            List<PointF> dbP2 = ConvertPointsToDb(hasP2 ? phase2 : null);
            List<PointF> dbP3 = ConvertPointsToDb(hasP3 ? phase3 : null);
            _xyChart1?.SetAxisTitles("Frequency (Hz)", "Amplitude (dB)");
            _xyChart1?.SetThreePhaseDbData(dbP1, dbP2, dbP3);
            IList<PointF> markerSource = dbP1;
            if (markerSource == null || markerSource.Count == 0) markerSource = dbP2;
            if (markerSource == null || markerSource.Count == 0) markerSource = dbP3;
            if (markerSource != null && markerSource.Count > 0)
                _xyChart1?.ShowPeakMarkers(markerSource, 8);
            else
                _xyChart1?.ShowPeakMarkers(new List<PointF>(), 0);

            _xyChart1?.RefreshEngineeringPlotAppearance();
            _xyChart1?.SetFaultMarkers(BuildFaultMarkers());
            ApplyHarmonicsHeader("Combined Engineering Plot — " + techName, markerSource);
            LogUI(techName + ": plotted " + CountPoints(phase1, phase2, phase3) + " points", Color.Cyan);
            UpdateSmartEngineeringView();
            UpdateTechniqueDataLeds(_lastFrame);
        }

        private static int CountPoints(List<PointF> p1, List<PointF> p2, List<PointF> p3)
        {
            int n = 0;
            if (p1 != null) n += p1.Count;
            if (p2 != null) n += p2.Count;
            if (p3 != null) n += p3.Count;
            return n;
        }

        /// <summary>
        /// Builds labelled reference lines (supply line, shaft, BPFO/BPFI/BSF/FTF) for the
        /// spectrum plot so peaks can be read directly against the expected fault frequencies.
        /// </summary>
        private List<MyChartClass.FreqMarker> BuildFaultMarkers()
        {
            var list = new List<MyChartClass.FreqMarker>();
            TelemetryData t = _lastTelemetryForReport;
            MotorParameters mp = _lastFrame != null ? _lastFrame.ReportMotorParams : null;

            double lineHz = 0;
            if (mp != null && mp.SupplyLineHz > 1 && mp.SupplyLineHz < 400) lineHz = mp.SupplyLineHz;
            if (lineHz <= 0) lineHz = ParseUiDouble(txtSupplyLineHz, 0);

            double rpm = mp != null && mp.MotorRPM > 0 ? mp.MotorRPM : ParseUiDouble(txtRPM, 0);
            double shaftHz = rpm > 0 ? rpm / 60.0 : 0;

            Color cLine = Color.FromArgb(41, 128, 185);
            Color cShaft = Color.FromArgb(127, 140, 141);
            Color cBpfo = Color.FromArgb(230, 126, 34);
            Color cBpfi = Color.FromArgb(142, 68, 173);
            Color cBsf = Color.FromArgb(22, 160, 133);
            Color cFtf = Color.FromArgb(160, 90, 60);

            if (lineHz > 0)
            {
                list.Add(new MyChartClass.FreqMarker(lineHz, "LINE " + lineHz.ToString("F0", CultureInfo.InvariantCulture), cLine));
                list.Add(new MyChartClass.FreqMarker(2 * lineHz, "2×LINE", cLine));
            }
            if (shaftHz > 0)
                list.Add(new MyChartClass.FreqMarker(shaftHz, "Shaft " + shaftHz.ToString("F1", CultureInfo.InvariantCulture), cShaft));

            double bpfo = t != null && t.BPFO_Hz > 0 ? t.BPFO_Hz : (mp != null && shaftHz > 0 ? mp.BPFO * shaftHz : 0);
            double bpfi = t != null && t.BPFI_Hz > 0 ? t.BPFI_Hz : (mp != null && shaftHz > 0 ? mp.BPFI * shaftHz : 0);
            double bsf = t != null && t.BSF_Hz > 0 ? t.BSF_Hz : (mp != null && shaftHz > 0 ? mp.BSF * shaftHz : 0);
            double ftf = t != null && t.FTF_Hz > 0 ? t.FTF_Hz : (mp != null && shaftHz > 0 ? mp.FTF * shaftHz : 0);

            if (bpfo > 0) list.Add(new MyChartClass.FreqMarker(bpfo, "BPFO", cBpfo));
            if (bpfi > 0) list.Add(new MyChartClass.FreqMarker(bpfi, "BPFI", cBpfi));
            if (bsf > 0) list.Add(new MyChartClass.FreqMarker(bsf, "BSF", cBsf));
            if (ftf > 0) list.Add(new MyChartClass.FreqMarker(ftf, "FTF", cFtf));

            return list;
        }

        private static double ParseUiDouble(TextBox tb, double fallback)
        {
            if (tb == null || string.IsNullOrWhiteSpace(tb.Text)) return fallback;
            return double.TryParse(tb.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : fallback;
        }

        /// <summary>Returns the strongest point (peak) in a spectrum, or null if empty.</summary>
        private static PointF? FindDominantPeak(IList<PointF> pts)
        {
            if (pts == null || pts.Count == 0) return null;
            PointF best = pts[0];
            for (int i = 1; i < pts.Count; i++)
                if (pts[i].Y > best.Y) best = pts[i];
            return best;
        }

        private void ApplyHarmonicsHeader(string techName, IList<PointF> dominantSource)
        {
            PointF? peak = FindDominantPeak(dominantSource);
            string peakText = peak.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "   •   Peak  f = {0:F1} Hz   A = {1:F1} dB",
                    peak.Value.X, peak.Value.Y)
                : "";
            if (lblPhase1 != null)
                lblPhase1.Text = techName + peakText
                    + "   •   refs (dashed): LINE · Shaft · BPFO · BPFI · BSF · FTF";
        }

        private List<PointF> GetPointsForPhase(SpectralFrame frame, SpectralTechnique tech, int phaseIndex)
        {
            if (frame == null) return null;

            switch (tech)
            {
                case SpectralTechnique.Fourier:
                    return SelectPerPhaseOrFallback(
                        frame.FourierPhase1Points, frame.FourierPhase2Points, frame.FourierPhase3Points,
                        frame.FourierPoints, phaseIndex, false);

                case SpectralTechnique.Music:
                    return SelectPerPhaseOrFallback(
                        frame.MusicPhase1Points, frame.MusicPhase2Points, frame.MusicPhase3Points,
                        frame.MusicPoints, phaseIndex, true);

                case SpectralTechnique.Cyclostationary:
                    return SelectPerPhaseOrFallback(
                        frame.Cyclic2Phase1Points, frame.Cyclic2Phase2Points, frame.Cyclic2Phase3Points,
                        frame.Cyclic2Points, phaseIndex, false);

                case SpectralTechnique.Sk:
                    return SelectPerPhaseOrFallback(
                        frame.SkPhase1Points, frame.SkPhase2Points, frame.SkPhase3Points,
                        frame.SkPoints, phaseIndex, false);

                case SpectralTechnique.Wavelet:
                    return SelectPerPhaseOrFallback(
                        frame.WaveletPhase1Points, frame.WaveletPhase2Points, frame.WaveletPhase3Points,
                        frame.WaveletPoints, phaseIndex, false);

                case SpectralTechnique.Welch:
                    // main.c emits a single 3-phase-max Welch trace; show it on phase 1 only.
                    return phaseIndex == 1 ? frame.WelchPoints : null;

                case SpectralTechnique.Coherence:
                    // main.c emits a single 3-phase-min coherence trace; show it on phase 1 only.
                    return phaseIndex == 1 ? frame.CoherencePoints : null;

                default:
                    return null;
            }
        }

        private List<PointF> SelectPerPhaseOrFallback(
            List<PointF> p1, List<PointF> p2, List<PointF> p3,
            List<PointF> fallback, int phaseIndex, bool normalize)
        {
            List<PointF> selected = null;
            if (phaseIndex == 1) selected = p1;
            else if (phaseIndex == 2) selected = p2;
            else if (phaseIndex == 3) selected = p3;

            if (selected != null && selected.Count > 0)
                return normalize ? NormalizePointsToMaxOne(selected) : selected;

            if (fallback == null || fallback.Count == 0) return null;
            return normalize ? NormalizePointsToMaxOne(fallback) : fallback;
        }

        private void UpdateSmartEngineeringView()
        {
            if (rtbSmartEngineering == null) return;

            StringBuilder sb = BuildSmartEngineeringReportText();
            rtbSmartEngineering.Text = sb.ToString();
        }

        private void ExportSmartEngineeringReport()
        {
            try
            {
                SaveFileDialog dlg = new SaveFileDialog
                {
                    Filter = "Text Report (*.txt)|*.txt|CSV Report (*.csv)|*.csv",
                    FileName = "H750_SmartEngineering_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                    DefaultExt = "txt",
                    AddExtension = true,
                    Title = "Save Smart Engineering Report"
                };

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                StringBuilder report = BuildSmartEngineeringReportText();
                File.WriteAllText(dlg.FileName, report.ToString(), Encoding.UTF8);

                LogUI("Smart engineering report saved: " + dlg.FileName, Color.LimeGreen);
                MessageBox.Show(
                    "Report file saved successfully.\n\n" + dlg.FileName,
                    "Export Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("ExportSmartEngineeringReport: " + ex.Message, ex);
                MessageBox.Show(
                    "Failed to export report:\n" + ex.Message,
                    "Export Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private StringBuilder BuildSmartEngineeringReportText()
        {
            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("SMART ENGINEERING SPECTRAL VIEW");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Current technique: " + _currentTechnique.ToString().ToUpperInvariant());
            sb.AppendLine(new string('-', 64));

            if (_lastFrame == null)
            {
                sb.AppendLine("No spectral frame available yet.");
                sb.AppendLine("Connect H750 and request REPORT / FULLREPORT / FFTCSV.");
                return sb;
            }

            AppendTechniqueSummary(sb, "FOURIER",
                _lastFrame.FourierPhase1Points, _lastFrame.FourierPhase2Points, _lastFrame.FourierPhase3Points,
                _lastFrame.FourierPoints);
            AppendTechniqueSummary(sb, "MUSIC",
                _lastFrame.MusicPhase1Points, _lastFrame.MusicPhase2Points, _lastFrame.MusicPhase3Points,
                _lastFrame.MusicPoints);
            AppendTechniqueSummary(sb, "CYCLIC2",
                _lastFrame.Cyclic2Phase1Points, _lastFrame.Cyclic2Phase2Points, _lastFrame.Cyclic2Phase3Points,
                _lastFrame.Cyclic2Points);
            AppendTechniqueSummary(sb, "SK",
                null, null, null, _lastFrame.SkPoints);
            AppendTechniqueSummary(sb, "WAVELET",
                null, null, null, _lastFrame.WaveletPoints);
            AppendTechniqueSummary(sb, "WELCH",
                null, null, null, _lastFrame.WelchPoints);
            AppendTechniqueSummary(sb, "COHERENCE",
                null, null, null, _lastFrame.CoherencePoints);

            sb.AppendLine();
            sb.AppendLine("ESPRIT DETAILS");
            sb.AppendLine("  Peaks count: " + _lastFrame.EspritFrequencies.Count);
            if (_lastFrame.EspritFrequencies.Count > 0)
            {
                List<float> sorted = new List<float>(_lastFrame.EspritFrequencies);
                sorted.Sort();
                sb.Append("  Peaks Hz: ");
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(sorted[i].ToString("F2"));
                    if (i >= 11 && sorted.Count > 12)
                    {
                        sb.Append(", ...");
                        break;
                    }
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("  Peaks Hz: none");
            }

            sb.AppendLine();
            sb.AppendLine("AUXILIARY TECHNIQUES");
            sb.AppendLine("  SK points: " + _lastFrame.SkPoints.Count);
            sb.AppendLine("  Wavelet points: " + _lastFrame.WaveletPoints.Count);
            sb.AppendLine("  Mode: " + (string.IsNullOrEmpty(_lastFrame.Mode) ? "n/a" : _lastFrame.Mode));
            sb.AppendLine("  End marker: " + (string.IsNullOrEmpty(_lastFrame.FinalReportSummary) ? "n/a" : _lastFrame.FinalReportSummary));

            if (_lastFrame.ReportTelemetry != null)
            {
                TelemetryData t = _lastFrame.ReportTelemetry;
                sb.AppendLine();
                sb.AppendLine("STATOR / BEARING INDICATORS");
                sb.AppendLine("  FI=" + t.FaultIndex.ToString("F4") +
                              "  LV=" + t.FaultLevel.ToString(CultureInfo.InvariantCulture) +
                              "  NSR=" + t.Stator_NSR.ToString("F4") +
                              "  HARM=" + t.Stator_HarmRatio.ToString("F4") +
                              "  IMB=" + t.Stator_Imbalance.ToString("F4"));
            }

            return sb;
        }

        private void AppendTechniqueSummary(
            StringBuilder sb,
            string name,
            List<PointF> p1,
            List<PointF> p2,
            List<PointF> p3,
            List<PointF> fallback)
        {
            List<PointF> s1 = (p1 != null && p1.Count > 0) ? p1 : fallback;
            List<PointF> s2 = (p2 != null && p2.Count > 0) ? p2 : fallback;
            List<PointF> s3 = (p3 != null && p3.Count > 0) ? p3 : fallback;

            sb.AppendLine();
            sb.AppendLine(name + " DETAILS");
            sb.AppendLine("  Phase1 points: " + ((s1 != null) ? s1.Count : 0));
            sb.AppendLine("  Phase2 points: " + ((s2 != null) ? s2.Count : 0));
            sb.AppendLine("  Phase3 points: " + ((s3 != null) ? s3.Count : 0));

            AppendPhasePeakLine(sb, "  Peak P1", s1);
            AppendPhasePeakLine(sb, "  Peak P2", s2);
            AppendPhasePeakLine(sb, "  Peak P3", s3);
        }

        private void AppendPhasePeakLine(StringBuilder sb, string prefix, List<PointF> points)
        {
            if (points == null || points.Count == 0)
            {
                sb.AppendLine(prefix + ": no data");
                return;
            }

            float maxMag = points[0].Y;
            float maxFreq = points[0].X;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].Y > maxMag)
                {
                    maxMag = points[i].Y;
                    maxFreq = points[i].X;
                }
            }

            sb.AppendLine(prefix + ": " +
                          maxFreq.ToString("F2", CultureInfo.InvariantCulture) + " Hz @ " +
                          maxMag.ToString("F6", CultureInfo.InvariantCulture));
        }

        private List<PointF> NormalizePointsToMaxOne(List<PointF> pts)
        {
            if (pts == null || pts.Count == 0) return pts;

            float maxVal = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i].Y > maxVal) maxVal = pts[i].Y;
            }
            if (maxVal < 1e-30f) return pts;

            List<PointF> result = new List<PointF>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                result.Add(new PointF(pts[i].X, pts[i].Y / maxVal));
            }
            return result;
        }

        private List<PointF> ConvertPointsToDb(List<PointF> points)
        {
            if (points == null || points.Count == 0) return null;

            List<PointF> db = new List<PointF>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                float freq = points[i].X;
                float mag = points[i].Y;

                double magDb = (mag > 1e-12f) ? 20.0 * Math.Log10(mag) : -240.0;
                if (magDb < -100.0) magDb = -100.0;
                if (magDb > 0.0) magDb = 0.0;

                db.Add(new PointF(freq, (float)magDb));
            }
            return db;
        }

        /// <summary>
        /// Displays ESPRIT frequency estimates as impulse lines overlaid on the
        /// per-phase Fourier spectrum background, all three phases in one chart.
        /// Each phase shows its own Fourier spectral curve with ESPRIT impulse
        /// spikes at the estimated frequencies, so the 3-phase curves are
        /// distinct and the ESPRIT peaks are clearly visible against real data.
        /// </summary>
        private void DisplayEspritThreePhaseOnCharts()
        {
            List<float> p1Freqs = (_lastFrame.EspritPhase1Frequencies != null && _lastFrame.EspritPhase1Frequencies.Count > 0)
                ? _lastFrame.EspritPhase1Frequencies : null;
            List<float> p2Freqs = (_lastFrame.EspritPhase2Frequencies != null && _lastFrame.EspritPhase2Frequencies.Count > 0)
                ? _lastFrame.EspritPhase2Frequencies : null;
            List<float> p3Freqs = (_lastFrame.EspritPhase3Frequencies != null && _lastFrame.EspritPhase3Frequencies.Count > 0)
                ? _lastFrame.EspritPhase3Frequencies : null;

            // If no per-phase data, use mono frequencies for all phases
            List<float> monoFreqs = _lastFrame.EspritFrequencies;
            if (p1Freqs == null && p2Freqs == null && p3Freqs == null)
            {
                if (monoFreqs == null || monoFreqs.Count == 0)
                {
                    _xyChart1?.ClearData();
                    _xyChart1?.SetFaultMarkers(null);
                    lblPhase1.Text = "Combined Engineering Plot — ESPRIT (no peaks found)";
                    LogUI("ESPRIT: no frequency estimates in last frame (exp_esprit_n=0)", Color.Orange);
                    UpdateSmartEngineeringView();
                    return;
                }
                // Use mono data for all three phases as fallback
                p1Freqs = monoFreqs;
                p2Freqs = monoFreqs;
                p3Freqs = monoFreqs;
            }

            // Get per-phase Fourier spectrum as background curves
            List<PointF> fourierP1 = GetPointsForPhase(_lastFrame, SpectralTechnique.Fourier, 1);
            List<PointF> fourierP2 = GetPointsForPhase(_lastFrame, SpectralTechnique.Fourier, 2);
            List<PointF> fourierP3 = GetPointsForPhase(_lastFrame, SpectralTechnique.Fourier, 3);

            bool hasFourier = (fourierP1 != null && fourierP1.Count > 0) ||
                              (fourierP2 != null && fourierP2.Count > 0) ||
                              (fourierP3 != null && fourierP3.Count > 0);

            List<PointF> dbP1, dbP2, dbP3;

            if (hasFourier)
            {
                // Merge Fourier background + ESPRIT impulses for each phase
                dbP1 = MergeFourierWithEspritImpulses(fourierP1, p1Freqs);
                dbP2 = MergeFourierWithEspritImpulses(fourierP2, p2Freqs);
                dbP3 = MergeFourierWithEspritImpulses(fourierP3, p3Freqs);
            }
            else
            {
                // No Fourier data: fall back to ESPRIT-only impulses
                float maxFreq = DetermineEspritMaxFreq(p1Freqs, p2Freqs, p3Freqs);
                dbP1 = ConvertPointsToDb(BuildEspritImpulsePoints(p1Freqs, maxFreq));
                dbP2 = ConvertPointsToDb(BuildEspritImpulsePoints(p2Freqs, maxFreq));
                dbP3 = ConvertPointsToDb(BuildEspritImpulsePoints(p3Freqs, maxFreq));
            }

            _xyChart1?.SetAxisTitles("Frequency (Hz)", "Amplitude (dB)");
            _xyChart1?.SetThreePhaseDbData(dbP1, dbP2, dbP3);

            // Show ESPRIT peak markers on the first available phase
            IList<PointF> markerSource = dbP1 ?? dbP2 ?? dbP3;
            if (markerSource != null && markerSource.Count > 0)
                _xyChart1?.ShowPeakMarkers(markerSource, 8);
            else
                _xyChart1?.ShowPeakMarkers(new List<PointF>(), 0);
            _xyChart1?.RefreshEngineeringPlotAppearance();
            _xyChart1?.SetFaultMarkers(BuildFaultMarkers());

            // Build frequency list string for label
            List<float> primaryFreqs = p1Freqs ?? p2Freqs ?? p3Freqs;
            List<float> sorted = new List<float>(primaryFreqs);
            sorted.Sort();
            string freqList = "";
            for (int i = 0; i < sorted.Count && i < 6; i++)
            {
                if (i > 0) freqList += ", ";
                freqList += sorted[i].ToString("F1") + " Hz";
            }
            if (sorted.Count > 6) freqList += " ...";

            string phaseInfo = hasFourier ? "3-phase Fourier + " : "";
            lblPhase1.Text = "Combined Engineering Plot — ESPRIT (" + phaseInfo + sorted.Count + " peaks: " + freqList + ")";
            LogUI("ESPRIT: " + phaseInfo + sorted.Count + " frequency estimates: " + freqList, Color.Cyan);
            UpdateSmartEngineeringView();
            UpdateTechniqueDataLeds(_lastFrame);
        }

        /// <summary>
        /// Merges a per-phase Fourier spectrum (as linear magnitude PointF list)
        /// with ESPRIT impulse spikes. The Fourier curve provides the spectral
        /// background; ESPRIT impulses are injected as narrow dB spikes that
        /// reach 0 dB at each estimated frequency, making them stand out clearly
        /// against the phase-specific Fourier curve.
        /// </summary>
        private List<PointF> MergeFourierWithEspritImpulses(List<PointF> fourierPoints, List<float> espritFreqs)
        {
            if (fourierPoints == null || fourierPoints.Count == 0)
            {
                if (espritFreqs == null || espritFreqs.Count == 0) return null;
                return ConvertPointsToDb(BuildEspritImpulsePoints(espritFreqs, DetermineEspritMaxFreq(espritFreqs, null, null)));
            }

            // Convert Fourier to dB first
            List<PointF> dbPoints = ConvertPointsToDb(fourierPoints);
            if (dbPoints == null || dbPoints.Count == 0) return null;

            if (espritFreqs == null || espritFreqs.Count == 0)
                return dbPoints; // No ESPRIT data, just return Fourier spectrum

            // Sort ESPRIT frequencies
            List<float> sorted = new List<float>(espritFreqs);
            sorted.Sort();

            // Find the maximum Fourier dB value to scale ESPRIT spikes above it
            float maxFourierDb = -100f;
            for (int i = 0; i < dbPoints.Count; i++)
                if (dbPoints[i].Y > maxFourierDb) maxFourierDb = dbPoints[i].Y;

            // ESPRIT spikes extend from floor to slightly above the Fourier peak
            float spikeTopDb = Math.Min(0f, maxFourierDb + 6f); // 6 dB above max Fourier, capped at 0
            float spikeFloorDb = -100f;
            float spikeWidthHz = 2.0f; // narrow spike width in Hz

            // Build merged list by inserting ESPRIT spikes into the Fourier curve
            List<PointF> merged = new List<PointF>(dbPoints.Count + sorted.Count * 4);
            int fourierIdx = 0;

            for (int espIdx = 0; espIdx < sorted.Count; espIdx++)
            {
                float espFreq = sorted[espIdx];
                float spikeStart = espFreq - spikeWidthHz;
                float spikeEnd = espFreq + spikeWidthHz;

                // Add Fourier points before this spike
                while (fourierIdx < dbPoints.Count && dbPoints[fourierIdx].X < spikeStart)
                {
                    merged.Add(dbPoints[fourierIdx]);
                    fourierIdx++;
                }

                // Drop any Fourier points inside the spike zone (they'll be replaced)
                while (fourierIdx < dbPoints.Count && dbPoints[fourierIdx].X < spikeEnd)
                    fourierIdx++;

                // Insert the ESPRIT spike
                merged.Add(new PointF(spikeStart, spikeFloorDb));
                merged.Add(new PointF(espFreq, spikeTopDb));
                merged.Add(new PointF(spikeEnd, spikeFloorDb));
            }

            // Add remaining Fourier points after the last spike
            while (fourierIdx < dbPoints.Count)
            {
                merged.Add(dbPoints[fourierIdx]);
                fourierIdx++;
            }

            return merged;
        }

        /// <summary>
        /// Determines the maximum frequency for the ESPRIT display from phase frequency lists.
        /// </summary>
        private float DetermineEspritMaxFreq(List<float> p1, List<float> p2, List<float> p3)
        {
            float maxFreq = 100f;
            if (p1 != null) for (int i = 0; i < p1.Count; i++) if (p1[i] > maxFreq) maxFreq = p1[i];
            if (p2 != null) for (int i = 0; i < p2.Count; i++) if (p2[i] > maxFreq) maxFreq = p2[i];
            if (p3 != null) for (int i = 0; i < p3.Count; i++) if (p3[i] > maxFreq) maxFreq = p3[i];
            maxFreq *= 1.2f;

            if (_lastFrame != null && _lastFrame.FourierPoints != null && _lastFrame.FourierPoints.Count > 0)
            {
                for (int i = 0; i < _lastFrame.FourierPoints.Count; i++)
                    if (_lastFrame.FourierPoints[i].X > maxFreq)
                        maxFreq = _lastFrame.FourierPoints[i].X;
            }
            if (maxFreq < 100f) maxFreq = 2500f;
            return maxFreq;
        }

        /// <summary>
        /// Builds impulse spike points for a list of ESPRIT frequencies.
        /// Each frequency becomes a vertical spike from floor (1e-12) to full scale (1.0).
        /// </summary>
        private List<PointF> BuildEspritImpulsePoints(List<float> freqs, float maxFreq)
        {
            if (freqs == null || freqs.Count == 0) return null;

            List<float> sorted = new List<float>(freqs);
            sorted.Sort();

            List<PointF> displayPoints = new List<PointF>();
            displayPoints.Add(new PointF(0f, 1e-12f));

            for (int i = 0; i < sorted.Count; i++)
            {
                float f = sorted[i];
                float fBefore = f - 0.5f;
                float fAfter = f + 0.5f;
                if (fBefore < 0f) fBefore = 0f;

                displayPoints.Add(new PointF(fBefore, 1e-12f));
                displayPoints.Add(new PointF(f, 1.0f));
                displayPoints.Add(new PointF(fAfter, 1e-12f));
            }

            displayPoints.Add(new PointF(maxFreq, 1e-12f));
            return displayPoints;
        }


        private Button MakeUsbActionButton(string text, Color backColor)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(100, 32),
                Margin = new Padding(0, 0, 8, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
        }

        private bool EnsureUsbConnected()
        {
            if (_comm != null && _comm.IsConnected) return true;
            MessageBox.Show("Connect to the H750 over USB first.", "Not Connected",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private async Task RunUsbMultiLineAsync(string label, Func<Task<List<string>>> request, string waitMarker)
        {
            if (!EnsureUsbConnected()) return;

            SetTechniqueButtonsEnabled(false);
            LogUI("USB " + label + ": sending…", Color.Yellow);

            List<string> lines = await request();

            if (lines != null && lines.Count > 0)
                LogUI(label + ": " + lines.Count + " lines (parsed on stream)", Color.LimeGreen);
            else
                LogUI(label + ": no data (timeout or firmware ERR)", Color.Red);

            SetTechniqueButtonsEnabled(true);
        }

        private Task RunUsbFullReportAsync()
        {
            return RunUsbMultiLineAsync("REPORT",
                () => _comm.RequestReport(),
                "### END_FULL_REPORT");
        }

        private Task RunUsbGraphDataAsync()
        {
            return RunUsbMultiLineAsync("GRAPHDATA",
                () => _comm.RequestGraphData(),
                "### END_GRAPHDATA");
        }

        private Task RunUsbPhaseCsvAsync()
        {
            return RunUsbMultiLineAsync("PHASECSV",
                () => _comm.RequestPhaseCsv(),
                "### END_PHASE_CSV");
        }

        private static readonly Color BaselineButtonBlue = Color.FromArgb(41, 128, 185);

        private static Color BaselineButtonColor(string cmd)
        {
            string u = cmd.ToUpperInvariant();
            if (u == STM32TextCommands.Calib || u == STM32TextCommands.CalibSt)
                return Color.FromArgb(39, 174, 96);
            if (u.StartsWith("CLEAR", StringComparison.Ordinal))
                return Color.FromArgb(192, 57, 43);
            return BaselineButtonBlue;
        }

        private async Task RunUsbBaselineCommandAsync(string command)
        {
            if (!EnsureUsbConnected()) return;
            LogUI("USB " + command + " — " + STM32CommandHints.Get(command), Color.Yellow);
            string resp = await _comm.SendMainCTextCommand(command);
            LogMainCTextResponse(command, resp);
        }

        private void LogMainCTextResponse(string command, string resp)
        {
            if (string.IsNullOrEmpty(resp))
            {
                LogUI(command + ": no response (timeout)", Color.Red);
                return;
            }
            string trimmed = resp.Trim();
            bool ok = trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
            LogUI(command + " ← " + trimmed, ok ? Color.LimeGreen : Color.OrangeRed);
        }

        /// <summary>
        /// Called when the report parser has a complete spectral frame.
        /// Stores the frame and updates charts for the current technique.
        /// </summary>
        private void ReportParser_OnFrameReady(SpectralFrame frame)
        {
            if (_formClosing) return;

            try
            {
                this.BeginInvoke(new Action(delegate
                {
                    _lastFrame = frame;
                    if (frame.ReportMotorParams != null)
                        PopulateUIFromParameters(frame.ReportMotorParams);

                    if (frame.ReportTelemetry != null &&
                        (frame.ReportTelemetry.BPFO_Hz != 0f ||
                         frame.ReportTelemetry.BPFI_Hz != 0f ||
                         frame.ReportTelemetry.BSF_Hz != 0f ||
                         frame.ReportTelemetry.FTF_Hz != 0f ||
                         frame.ReportTelemetry.FaultIndex != 0f ||
                         frame.ReportTelemetry.Index_Bpfo != 0f ||
                         frame.ReportTelemetry.Stator_NSR != 0f ||
                         frame.ReportTelemetry.HasTemperature))
                    {
                        ApplyTelemetryToUi(frame.ReportTelemetry);
                    }
                    SyncTechniqueFromFirmwareMode(frame.Mode);
                    RedrawChartsForCurrentTechnique();
                    UpdateTechniqueDataLeds(frame);

                    string fin = string.IsNullOrEmpty(frame.FinalReportSummary)
                        ? "(marker not set)"
                        : frame.FinalReportSummary;

                    LogUI("STM32 final report: " + fin, Color.LimeGreen);
                    LogUI("Frame received: Fourier=" + frame.FourierPoints.Count +
                          " MUSIC=" + frame.MusicPoints.Count +
                          " Cyclic2=" + frame.Cyclic2Points.Count +
                          " ESPRIT=" + frame.EspritFrequencies.Count +
                          " Wavelet=" + frame.WaveletPoints.Count +
                          " SK=" + frame.SkPoints.Count + " pts",
                          Color.Cyan);
                    UpdateSmartEngineeringView();
                    UpdateReportPageFromCurrentData();
                }));
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────
        // Settings Tab — STM32 Communication Panel
        // ─────────────────────────────────────────────────────────────

        private void BuildSettingsTab()
        {
            tabSettings.AutoScroll = true;
            tabSettings.Font = new Font("Segoe UI", 9.5F);

            _comm = new STM32CommManager();
            _comm.OnLogMessage += Comm_OnLog;
            _comm.OnTelemetryReceived += Comm_OnTelemetry;
            _comm.OnConnectionChanged += Comm_OnConnectionChanged;
            _comm.OnLineReceived += Comm_OnLineReceived;

            int y = 8;

            // ── Connection ──
            grpConnection = new GroupBox
            {
                Text = "USB Connection (CDC VCP)",
                Location = new Point(10, y),
                Size = new Size(940, 70),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            SettingsMakeLabel("Port:", 12, 28, grpConnection);
            cmbPorts = new ComboBox
            {
                Location = new Point(55, 25),
                Size = new Size(120, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Parent = grpConnection
            };

            btnRefresh = new Button
            {
                Text = "Refresh",
                Location = new Point(180, 24),
                Size = new Size(70, 27),
                Parent = grpConnection
            };
            btnRefresh.Click += delegate { RefreshPorts(); };

            btnConnect = new Button
            {
                Text = "Connect",
                Location = new Point(260, 24),
                Size = new Size(100, 27),
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Parent = grpConnection
            };
            btnConnect.Click += BtnConnect_Click;

            btnPing = new Button
            {
                Text = "Ping",
                Location = new Point(370, 24),
                Size = new Size(60, 27),
                Enabled = false,
                Parent = grpConnection
            };
            btnPing.Click += BtnPing_Click;

            pnlIndicator = new Panel
            {
                Location = new Point(440, 28),
                Size = new Size(18, 18),
                BackColor = Color.Red,
                Parent = grpConnection
            };
            pnlIndicator.Paint += delegate (object s, PaintEventArgs pe)
            {
                pe.Graphics.SmoothingMode =
                    System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush br = new SolidBrush(pnlIndicator.BackColor))
                    pe.Graphics.FillEllipse(br, 0, 0, 17, 17);
            };

            lblConnStatus = new Label
            {
                Text = "Disconnected",
                Location = new Point(465, 28),
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.Red,
                Parent = grpConnection
            };

            chkUseTextProtocol = new CheckBox
            {
                Text = "Use Text Protocol",
                Location = new Point(700, 27),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                Checked = true,
                Enabled = false,
                Parent = grpConnection
            };

            tabSettings.Controls.Add(grpConnection);
            y += 78;

            // ── Motor Parameters ──
            grpMotor = new GroupBox
            {
                Text = "Motor Parameters",
                Location = new Point(10, y),
                Size = new Size(310, 93),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };

            string[] mLabels = { "RPM:", "Slip:" };
            string[] mDefs = { "1500", "0.05" };
            TextBox[] mBoxes = new TextBox[2];
            for (int i = 0; i < 2; i++)
            {
                SettingsMakeLabel(mLabels[i], 10, 28 + i * 35, grpMotor);
                mBoxes[i] = SettingsMakeTextBox(170, 26 + i * 35, 120, mDefs[i], grpMotor);
            }
            txtRPM = mBoxes[0];
            txtSlip = mBoxes[1];
            tabSettings.Controls.Add(grpMotor);

            // ── Bearing Coefficients ──
            grpBearingCoeff = new GroupBox
            {
                Text = "Bearing Coefficients",
                Location = new Point(330, y),
                Size = new Size(620, 93),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };

            string[] bLabels = { "BPFO:", "BPFI:", "FTF:", "BSF:" };
            string[] bDefs = { "3.566", "5.434", "0.383", "0.396" };
            TextBox[] bBoxes = new TextBox[4];
            for (int i = 0; i < 2; i++)
            {
                SettingsMakeLabel(bLabels[i], 10, 28 + i * 35, grpBearingCoeff);
                bBoxes[i] = SettingsMakeTextBox(80, 26 + i * 35, 195, bDefs[i], grpBearingCoeff);
            }
            for (int i = 0; i < 2; i++)
            {
                SettingsMakeLabel(bLabels[i + 2], 310, 28 + i * 35, grpBearingCoeff);
                bBoxes[i + 2] = SettingsMakeTextBox(380, 26 + i * 35, 195, bDefs[i + 2], grpBearingCoeff);
            }
            txtStmBPFO = bBoxes[0]; txtStmBPFI = bBoxes[1];
            txtStmFTF = bBoxes[2]; txtStmBSF = bBoxes[3];
            tabSettings.Controls.Add(grpBearingCoeff);

            // ── Thresholds ──
            y += 100;
            grpThresholds = new GroupBox
            {
                Text = "Fault Thresholds & Supply",
                Location = new Point(10, y),
                Size = new Size(310, 140),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };

            SettingsMakeLabel("Fault (FTH):", 10, 30, grpThresholds);
            txtFaultThresh = SettingsMakeTextBox(130, 30, 155, "4.5", grpThresholds);
            SettingsMakeLabel("Warning (WTH):", 10, 68, grpThresholds);
            txtWarnThresh = SettingsMakeTextBox(130, 68, 155, "2.0", grpThresholds);
            SettingsMakeLabel("LINE (Hz):", 10, 105, grpThresholds);
            txtSupplyLineHz = SettingsMakeTextBox(130, 105, 155, "50.0", grpThresholds);
            tabSettings.Controls.Add(grpThresholds);

            // ── Live Data ──
            // Live Telemetry group box removed from Settings as requested.

            // ── Terminal ──
            SettingsMakeLabel("Terminal:", 330, y + 8, tabSettings, 9F).ForeColor =
                Color.FromArgb(0, 0, 0);

            txtTerminalCmd = new TextBox
            {
                Location = new Point(400, y + 5),
                Size = new Size(450, 26),
                Font = new Font("Consolas", 10F),
                Text = "",
                Enabled = false
            };
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(txtTerminalCmd,
                    "main.c: PING GET REPORT GRAPHDATA CALIB SAVEBASE RPM=1500 LINE=50 DATE 2026-05-30 TIME 14:30:00 HELP");
            txtTerminalCmd.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter)
                {
                    ev.SuppressKeyPress = true;
                    BtnTerminalSend_Click(s, ev);
                }
            };
            tabSettings.Controls.Add(txtTerminalCmd);

            btnTerminalSend = new Button
            {
                Text = "Send",
                Location = new Point(870, y + 4),
                Size = new Size(80, 28),
                BackColor = Color.FromArgb(0, 223, 218),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Enabled = false
            };
            btnTerminalSend.FlatAppearance.BorderSize = 0;
            btnTerminalSend.Click += BtnTerminalSend_Click;
            tabSettings.Controls.Add(btnTerminalSend);

            y += 35;
            // ── Live Monitor (fixed grid — same position, aligned columns) ──
            const int monRow1LabelY = 30;
            const int monRow1CtrlY = 27;
            const int monRow2LabelY = 62;
            const int monRow2CtrlY = 59;
            const int monColTempLbl = 12;
            const int monColTempVal = 92;
            const int monColStLbl = 158;
            const int monColBar = 252;
            const int monBarW = 132;
            const int monColPct = monColBar + monBarW + 10;
            const int monColRawLbl = monColPct + 48;
            const int monColRawVal = monColRawLbl + 34;
            const int monColNsrLbl = 526;
            const int monColNsrVal = 558;
            const int monColZsrLbl = 526;
            const int monColZsrVal = 558;

            grpMonitor = new GroupBox
            {
                Text = "Live Monitor",
                Location = new Point(330, y),
                Size = new Size(620, 96),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 0, 0)
            };
            tabSettings.Controls.Add(grpMonitor);

            SettingsMakeAlignedLabel("Temp (°C):", monColTempLbl, monRow1LabelY, 76, grpMonitor,
                ContentAlignment.MiddleRight);
            txtSettingsTemperature = SettingsMakeMonitorReadout(monColTempVal, monRow1CtrlY, 58, grpMonitor);

            SettingsMakeAlignedLabel("Stator fault:", monColStLbl, monRow1LabelY, 88, grpMonitor,
                ContentAlignment.MiddleRight);
            barSettingsStatorShort = new MyBar
            {
                Location = new Point(monColBar, monRow1CtrlY),
                Size = new Size(monBarW, 22),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            grpMonitor.Controls.Add(barSettingsStatorShort);
            txtSettingsStatorShort = SettingsMakeMonitorReadout(monColPct, monRow1CtrlY, 44, grpMonitor);
            txtSettingsStatorShort.Text = "0%";
            _settingsMonitorLabels["SHORT"] = AddKeyValueMetricAligned(
                grpMonitor, "Short:", monColRawLbl, monColRawVal, monRow1LabelY, 44);

            SettingsMakeAlignedLabel("Ground:", monColStLbl, monRow2LabelY, 88, grpMonitor,
                ContentAlignment.MiddleRight);
            barSettingsStatorGround = new MyBar
            {
                Location = new Point(monColBar, monRow2CtrlY),
                Size = new Size(monBarW, 22),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            grpMonitor.Controls.Add(barSettingsStatorGround);
            txtSettingsStatorGround = SettingsMakeMonitorReadout(monColPct, monRow2CtrlY, 44, grpMonitor);
            txtSettingsStatorGround.Text = "0%";
            _settingsMonitorLabels["GROUND"] = AddKeyValueMetricAligned(
                grpMonitor, "Gnd:", monColRawLbl, monColRawVal, monRow2LabelY, 44);

            _settingsMonitorLabels["NSR"] = AddKeyValueMetricAligned(
                grpMonitor, "NSR:", monColNsrLbl, monColNsrVal, monRow1LabelY, 40);
            _settingsMonitorLabels["ZSR"] = AddKeyValueMetricAligned(
                grpMonitor, "ZSR:", monColZsrLbl, monColZsrVal, monRow2LabelY, 40);

            y += 100;

            if (_mainCToolTip == null)
                _mainCToolTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400 };

            grpBaseline = new GroupBox
            {
                Text = "Baseline — bearing & stator",
                Location = new Point(10, y),
                Size = new Size(940, 58),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            int bxBase = 12;
            int byBase = 24;
            string[] baseCmds =
            {
                STM32TextCommands.Calib, STM32TextCommands.SaveBase, STM32TextCommands.LoadBase,
                STM32TextCommands.ClearBase, STM32TextCommands.CalibSt, STM32TextCommands.SaveStSt,
                STM32TextCommands.LoadStSt, STM32TextCommands.ClearStSt
            };
            foreach (string cmd in baseCmds)
            {
                string captured = cmd;
                Color bg = BaselineButtonColor(captured);
                Button b = SettingsMakeActionButton(captured, bxBase, byBase, 88, bg, grpBaseline);
                b.Click += async (s, e) => await RunUsbBaselineCommandAsync(captured);
                _mainCToolTip.SetToolTip(b, STM32CommandHints.Get(captured));
                bxBase += 96;
            }
            tabSettings.Controls.Add(grpBaseline);
            y += 64;

            // ── Action Buttons ──
            int bx = 10;
            btnSendAll = SettingsMakeActionButton("Send All Parameters", bx, y, 150,
                Color.FromArgb(0, 223, 218));
            btnSendAll.Click += BtnSendAll_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnSendAll,
                    "Writes RPM= SLIP= BPFO= … to MCU RAM (main.c). Not QSPI — use SAVE after.");
            bx += 159;

            btnReadAll = SettingsMakeActionButton("GET", bx, y, 72,
                Color.FromArgb(0, 223, 218));
            btnReadAll.Click += BtnReadAll_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnReadAll, STM32CommandHints.Get(STM32TextCommands.Get));
            bx += 80;

            btnSaveAdc = SettingsMakeActionButton(STM32TextCommands.SaveAdc, bx, y, 88,
                Color.FromArgb(0, 223, 218));
            btnSaveAdc.Click += BtnSaveAdc_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnSaveAdc, STM32CommandHints.Get(STM32TextCommands.SaveAdc));
            bx += 96;

            btnSaveFlash = SettingsMakeActionButton(STM32TextCommands.Save, bx, y, 72,
                Color.FromArgb(0, 223, 218));
            btnSaveFlash.Click += BtnSaveFlash_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnSaveFlash, STM32CommandHints.Get(STM32TextCommands.Save));
            bx += 80;

            btnLoadFlash = SettingsMakeActionButton(STM32TextCommands.Load, bx, y, 72,
                Color.FromArgb(0, 223, 218));
            btnLoadFlash.Click += BtnLoadFlash_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnLoadFlash, STM32CommandHints.Get(STM32TextCommands.Load));
            bx += 80;

            btnResetDefault = SettingsMakeActionButton(STM32TextCommands.Default, bx, y, 88,
                Color.FromArgb(0, 223, 218));
            btnResetDefault.Click += BtnResetDefault_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnResetDefault, STM32CommandHints.Get(STM32TextCommands.Default));
            bx += 96;

            btnGetCoeff = SettingsMakeActionButton("GET COEFF", bx, y, 96,
                Color.FromArgb(0, 223, 218));
            btnGetCoeff.Click += BtnGetCoeff_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnGetCoeff, STM32CommandHints.Get(STM32TextCommands.GetCoeff));
            bx += 104;

            chkSlipAuto = new CheckBox
            {
                Text = "Auto Slip (SLIPAUTO)",
                Location = new Point(bx, y + 4),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(chkSlipAuto, STM32CommandHints.Get(STM32TextCommands.SlipAuto));
            tabSettings.Controls.Add(chkSlipAuto);

            y += 44;

            // ── Device clock (main.c software RTC: DATE yyyy-mm-dd / TIME hh:mm:ss) ──
            SettingsMakeLabel("Device Clock:", 10, y + 6, tabSettings, 9.5F);
            lblDeviceClock = new Label
            {
                Text = "—",
                Location = new Point(110, y + 4),
                Size = new Size(190, 22),
                Font = new Font("Consolas", 9.75F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 90, 60),
                BackColor = Color.FromArgb(252, 253, 255),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleCenter
            };
            tabSettings.Controls.Add(lblDeviceClock);

            SettingsMakeLabel("Set →", 312, y + 6, tabSettings, 9.5F);
            dtpClockDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Location = new Point(360, y + 2),
                Size = new Size(118, 26),
                Font = new Font("Segoe UI", 9F)
            };
            tabSettings.Controls.Add(dtpClockDate);

            dtpClockTime = new DateTimePicker
            {
                Format = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Location = new Point(484, y + 2),
                Size = new Size(92, 26),
                Font = new Font("Segoe UI", 9F)
            };
            tabSettings.Controls.Add(dtpClockTime);

            btnSendClock = SettingsMakeActionButton("SET CLOCK", 584, y + 1, 90, Color.FromArgb(0, 223, 218));
            btnSendClock.Click += BtnSendClock_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnSendClock, "Sends DATE yyyy-mm-dd then TIME hh:mm:ss to the MCU clock (main.c).");

            btnSyncPcClock = SettingsMakeActionButton("SYNC PC", 680, y + 1, 80, Color.FromArgb(41, 128, 185));
            btnSyncPcClock.Click += BtnSyncPcClock_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnSyncPcClock, "Sets the pickers to this PC's current date/time and sends them to the MCU.");

            btnReadClock = SettingsMakeActionButton("READ", 766, y + 1, 64, Color.FromArgb(127, 140, 141));
            btnReadClock.Click += BtnReadClock_Click;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(btnReadClock, "Reads the current MCU clock via GET (DATE= / TIME=).");

            chkLiveStream = new CheckBox
            {
                Text = "Live stream",
                Location = new Point(842, y + 5),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(192, 57, 43)
            };
            chkLiveStream.CheckedChanged += ChkLiveStream_CheckedChanged;
            if (_mainCToolTip != null)
                _mainCToolTip.SetToolTip(chkLiveStream,
                    "STREAM=1/0. OFF = device silent, fast connect/GET/SET. ON = continuous live telemetry + plots.");
            tabSettings.Controls.Add(chkLiveStream);

            y += 44;

            // ── Log ──
            rtbLog = new RichTextBox
            {
                Location = new Point(10, y),
                Size = new Size(940, 300),
                Font = new Font("Consolas", 9F),
                ReadOnly = true,
                BackColor = Color.FromArgb(0, 0, 153),
                ForeColor = Color.LimeGreen,
                WordWrap = false
            };
            tabSettings.Controls.Add(rtbLog);

            // ── Initialize param controls array ──
            _paramControls = new Control[]
            {
                txtRPM, txtSlip,
                txtStmBPFO, txtStmBPFI, txtStmFTF, txtStmBSF,
                txtFaultThresh, txtWarnThresh, txtSupplyLineHz,
                btnPing, btnSendAll, btnReadAll,
                btnSaveFlash, btnLoadFlash, btnResetDefault,
                btnSaveAdc, btnGetCoeff, chkSlipAuto,
                btnSendClock, btnSyncPcClock, btnReadClock, chkLiveStream,
                dtpClockDate, dtpClockTime,
                txtTerminalCmd, btnTerminalSend,
                chkUseTextProtocol,
                btnReportFullReport, btnReportGraphData, btnReportPhaseCsv,
                btnTechSk, btnTechWavelet, btnTechWelch, btnTechCoh
            };

            SetAllParamControlsEnabled(false);
            RefreshPorts();
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — UI Helper Methods
        // ═════════════════════════════════════════════════════════════

        private Label SettingsMakeLabel(string text, int x, int y, Control parent)
        {
            return SettingsMakeLabel(text, x, y, parent, 9.5F);
        }

        private Label SettingsMakeLabel(string text, int x, int y, Control parent, float fontSize)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font("Segoe UI", fontSize),
                ForeColor = Color.Black,
                Parent = parent
            };
        }

        private TextBox SettingsMakeTextBox(int x, int y, int w, string defVal, Control parent)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 26),
                Font = new Font("Consolas", 10F),
                Text = defVal,
                Parent = parent
            };
        }

        private static TextBox SettingsMakeMonitorReadout(int x, int y, int w, Control parent)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 22),
                Font = new Font("Consolas", 9.25F, FontStyle.Bold),
                Text = "----",
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                BackColor = Color.FromArgb(252, 253, 255),
                Parent = parent
            };
        }

        private Label SettingsMakeValueLabel(int x, int y, int w, Control parent)
        {
            return new Label
            {
                Text = "----",
                Location = new Point(x, y),
                Size = new Size(w, 20),
                Font = new Font("Consolas", 9.25F, FontStyle.Bold),
                ForeColor = Color.Black,
                Parent = parent
            };
        }

        private Label AddKeyValueMetric(Control parent, string labelText, int labelX, int valueX, int y, int valueWidth)
        {
            SettingsMakeLabel(labelText, labelX, y, parent, 9F);
            Label valueLabel = SettingsMakeValueLabel(valueX, y, valueWidth, parent);
            return valueLabel;
        }

        private static Label SettingsMakeAlignedLabel(string text, int x, int y, int width, Control parent,
            ContentAlignment align)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 22),
                TextAlign = align,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.Black,
                Parent = parent
            };
        }

        private static Label AddKeyValueMetricAligned(Control parent, string labelText, int labelX, int valueX,
            int labelY, int valueWidth)
        {
            SettingsMakeAlignedLabel(labelText, labelX, labelY, 34, parent, ContentAlignment.MiddleRight);
            return new Label
            {
                Text = "----",
                Location = new Point(valueX, labelY),
                Size = new Size(valueWidth, 22),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Consolas", 9.25F, FontStyle.Bold),
                ForeColor = Color.Black,
                Parent = parent
            };
        }

        private Button SettingsMakeActionButton(string text, int x, int y, int w, Color bgColor)
        {
            return SettingsMakeActionButton(text, x, y, w, bgColor, tabSettings);
        }

        private static Button SettingsMakeActionButton(string text, int x, int y, int w, Color bgColor, Control parent)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 28),
                BackColor = bgColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Enabled = false,
                Parent = parent
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void SetAllParamControlsEnabled(bool enabled)
        {
            if (_paramControls != null)
            {
                for (int i = 0; i < _paramControls.Length; i++)
                {
                    if (_paramControls[i] != null)
                        _paramControls[i].Enabled = enabled;
                }
            }

            if (grpBaseline != null)
            {
                foreach (Control c in grpBaseline.Controls)
                {
                    if (c is Button)
                        c.Enabled = enabled;
                }
            }
        }

        private bool UseTextProtocol
        {
            get { return chkUseTextProtocol != null && chkUseTextProtocol.Checked; }
        }

        private void RefreshPorts()
        {
            cmbPorts.Items.Clear();
            string[] ports = STM32CommManager.GetAvailablePorts();
            cmbPorts.Items.AddRange(ports);
            if (ports.Length > 0)
                cmbPorts.SelectedIndex = 0;
            LogUI("Found " + ports.Length + " COM port(s) — Connect will auto-pick the one that answers PING",
                Color.Cyan);
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — Connection Events
        // ═════════════════════════════════════════════════════════════

        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_comm.IsConnected)
            {
                _comm.StopTelemetryMonitor();
                await _comm.Disconnect();
                return;
            }

            if (cmbPorts.SelectedItem == null)
            {
                MessageBox.Show("Select a COM port first.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnConnect.Enabled = false;
            btnConnect.Text = "Connecting...";

            string preferred = cmbPorts.SelectedItem.ToString();
            LogUI("Connecting (scanning COM ports for PING OK)…", Color.Cyan);
            string portName = await _comm.ConnectFirstResponsivePortAsync(preferred);

            if (portName != null)
            {
                for (int i = 0; i < cmbPorts.Items.Count; i++)
                {
                    if (cmbPorts.Items[i].ToString() == portName)
                    {
                        cmbPorts.SelectedIndex = i;
                        break;
                    }
                }
                _comm.StartTelemetryMonitor();
            }
            else
            {
                MessageBox.Show(
                    "No COM port answered PING.\n\n" +
                    "You had " + cmbPorts.Items.Count + " port(s) — often one is ST-Link VCP (wrong) " +
                    "and one is USB-CDC from the MCU (correct).\n\n" +
                    "Check:\n" +
                    "  - USB cable to the MCU (not only ST-Link)\n" +
                    "  - Firmware rebuilt and flashed\n" +
                    "  - Replug the board after flash\n" +
                    "  - Try each COM port manually if needed",
                    "Connection Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            btnConnect.Enabled = true;
        }

        private async void BtnPing_Click(object sender, EventArgs e)
        {
            await BtnPing_ClickAsync();
        }

        private void Comm_OnConnectionChanged(bool connected)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(Comm_OnConnectionChanged), connected);
                return;
            }

            if (connected)
            {
                lblConnStatus.Text = "Connected: " + cmbPorts.SelectedItem;
                lblConnStatus.ForeColor = Color.Green;
                pnlIndicator.BackColor = Color.LimeGreen;
                pnlIndicator.Invalidate();
                btnConnect.Text = "Disconnect";
                btnConnect.BackColor = Color.FromArgb(192, 57, 43);
                cmbPorts.Enabled = false;
                btnRefresh.Enabled = false;
                SetAllParamControlsEnabled(true);
                LogUI("All controls enabled", Color.LimeGreen);
                UpdateReportConnectionBadge(true);
                ResetTelemetryDisplays();
                UpdateAlarmMonitorDashboard(null);
                ApplyFrequencyLeds(3, true);

                // Only sync STREAM when the link is alive; avoids a 6 s timeout when
                // PING failed (MCU busy or stale USB after flash).
                if (_comm.LastConnectPingOk)
                    SyncStreamStateToDevice();
                else
                    LogUI("Skipped STREAM sync — PING failed", Color.Orange);

                // Auto-switch to the Report tab after successful connection
                if (tabControl1 != null && tabAlarmMonitor != null)
                    tabControl1.SelectedTab = tabAlarmMonitor;
            }
            else
            {
                lblConnStatus.Text = "Disconnected";
                lblConnStatus.ForeColor = Color.Red;
                pnlIndicator.BackColor = Color.Red;
                pnlIndicator.Invalidate();
                btnConnect.Text = "Connect";
                btnConnect.BackColor = Color.FromArgb(39, 174, 96);
                cmbPorts.Enabled = true;
                btnRefresh.Enabled = true;
                SetAllParamControlsEnabled(false);
                UpdateReportConnectionBadge(false);
                ResetTelemetryDisplays();
                UpdateAlarmMonitorDashboard(null);
                ApplyFrequencyLeds(0, false);
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — Send/Read Parameters
        // ═════════════════════════════════════════════════════════════

        private async void BtnSendAll_Click(object sender, EventArgs e)
        {
            MotorParameters p = ParseUIParameters();
            if (p == null) return;

            btnSendAll.Enabled = false;
            // main.c: LINE= is text-only; binary SET has no LINE command (see USB header in main.c).
            LogUI("Sending all parameters (text, main.c key=value)...", Color.Yellow);

            int ok = await _comm.SendAllParametersText(p);

            // 8 core params (RPM, SLIP, BPFO, BPFI, FTF, BSF, FTH, WTH)
            // + SLIPAUTO (always sent), + LINE when in valid range.
            int expected = 9;
            if (p.SupplyLineHz > 1.0f && p.SupplyLineHz < 400.0f)
                expected = 10;

            string msg = "Sent " + ok + " parameter command(s) (text)";
            if (ok == expected)
                LogUI(msg + " — ALL OK", Color.LimeGreen);
            else
                LogUI(msg + " — SOME FAILED", Color.Orange);

            btnSendAll.Enabled = true;
        }

        private MotorParameters ParseUIParameters()
        {
            try
            {
                CultureInfo ci = CultureInfo.InvariantCulture;
                MotorParameters p = new MotorParameters();
                p.MotorRPM = float.Parse(txtRPM.Text, ci);
                p.Slip = float.Parse(txtSlip.Text, ci);
                p.BPFO = float.Parse(txtStmBPFO.Text, ci);
                p.BPFI = float.Parse(txtStmBPFI.Text, ci);
                p.FTF = float.Parse(txtStmFTF.Text, ci);
                p.BSF = float.Parse(txtStmBSF.Text, ci);
                p.FaultThreshold = float.Parse(txtFaultThresh.Text, ci);
                p.WarningThreshold = float.Parse(txtWarnThresh.Text, ci);
                if (!string.IsNullOrWhiteSpace(txtSupplyLineHz.Text) &&
                    float.TryParse(txtSupplyLineHz.Text.Trim(), NumberStyles.Float, ci, out float lineHz) &&
                    lineHz > 1.0f && lineHz < 400.0f)
                    p.SupplyLineHz = lineHz;
                p.SlipAuto = chkSlipAuto != null && chkSlipAuto.Checked;
                return p;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Invalid parameter value:\n" + ex.Message,
                    "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private async void BtnReadAll_Click(object sender, EventArgs e)
        {
            await BtnReadAll_ClickAsync();
        }

        private void PopulateUIFromParameters(MotorParameters p)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            txtRPM.Text = p.MotorRPM.ToString("F1", ci);
            txtSlip.Text = p.Slip.ToString("F4", ci);
            txtStmBPFO.Text = p.BPFO.ToString("F3", ci);
            txtStmBPFI.Text = p.BPFI.ToString("F3", ci);
            txtStmFTF.Text = p.FTF.ToString("F3", ci);
            txtStmBSF.Text = p.BSF.ToString("F3", ci);
            txtFaultThresh.Text = p.FaultThreshold.ToString("F2", ci);
            txtWarnThresh.Text = p.WarningThreshold.ToString("F2", ci);
            txtSupplyLineHz.Text = p.SupplyLineHz > 1.0f
                ? p.SupplyLineHz.ToString("F2", ci)
                : "50.0";
            if (chkSlipAuto != null) chkSlipAuto.Checked = p.SlipAuto;
            ApplyDeviceClock(p.ClockDate, p.ClockTime);
        }

        /// <summary>
        /// Updates the device-clock readout label and (when valid) the date/time
        /// pickers from the strings returned by GET (DATE= yyyy-mm-dd, TIME= hh:mm:ss).
        /// </summary>
        private void ApplyDeviceClock(string clockDate, string clockTime)
        {
            if (lblDeviceClock == null) return;

            string shown = ((string.IsNullOrEmpty(clockDate) ? "—" : clockDate) + "  " +
                            (string.IsNullOrEmpty(clockTime) ? "" : clockTime)).Trim();
            lblDeviceClock.Text = string.IsNullOrEmpty(shown) ? "—" : shown;

            CultureInfo ci = CultureInfo.InvariantCulture;
            if (dtpClockDate != null && !string.IsNullOrEmpty(clockDate) &&
                DateTime.TryParseExact(clockDate, "yyyy-MM-dd", ci, DateTimeStyles.None, out DateTime d))
            {
                if (d >= dtpClockDate.MinDate && d <= dtpClockDate.MaxDate)
                    dtpClockDate.Value = d;
            }
            if (dtpClockTime != null && !string.IsNullOrEmpty(clockTime) &&
                (DateTime.TryParseExact(clockTime, "HH:mm:ss", ci, DateTimeStyles.None, out DateTime t) ||
                 DateTime.TryParseExact(clockTime, "HH:mm", ci, DateTimeStyles.None, out t)))
            {
                dtpClockTime.Value = new DateTime(2000, 1, 1, t.Hour, t.Minute, t.Second);
            }
        }

        private async void BtnGetCoeff_Click(object sender, EventArgs e)
        {
            if (_comm == null || !_comm.IsConnected)
            {
                LogUI("GET COEFF: not connected", Color.Orange);
                return;
            }

            btnGetCoeff.Enabled = false;
            try
            {
                MotorParameters c = await _comm.GetCoefficientsText();
                if (c == null)
                {
                    LogUI("GET COEFF: no/invalid response", Color.Red);
                    return;
                }

                CultureInfo ci = CultureInfo.InvariantCulture;
                txtStmBPFO.Text = c.BPFO.ToString("F3", ci);
                txtStmBPFI.Text = c.BPFI.ToString("F3", ci);
                txtStmFTF.Text = c.FTF.ToString("F3", ci);
                txtStmBSF.Text = c.BSF.ToString("F3", ci);
                LogUI("GET COEFF: BPFO/BPFI/BSF/FTF updated from main.c", Color.LimeGreen);
            }
            finally
            {
                btnGetCoeff.Enabled = true;
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — Device clock (main.c DATE / TIME software RTC)
        // ═════════════════════════════════════════════════════════════

        /// <summary>Sends the picker date+time to the MCU (DATE then TIME), then reads it back.</summary>
        private async void BtnSendClock_Click(object sender, EventArgs e)
        {
            await SendClockAsync(dtpClockDate.Value, dtpClockTime.Value);
        }

        /// <summary>Loads this PC's current date/time into the pickers and sends it to the MCU.</summary>
        private async void BtnSyncPcClock_Click(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            if (dtpClockDate != null) dtpClockDate.Value = now.Date;
            if (dtpClockTime != null) dtpClockTime.Value = new DateTime(2000, 1, 1, now.Hour, now.Minute, now.Second);
            await SendClockAsync(now, now);
        }

        /// <summary>Reads the current MCU clock via GET and updates the readout + pickers.</summary>
        private async void BtnReadClock_Click(object sender, EventArgs e)
        {
            if (!EnsureUsbConnected()) return;
            btnReadClock.Enabled = false;
            try
            {
                LogUI("READ CLOCK (GET DATE=/TIME=)…", Color.Cyan);
                MotorParameters p = await _comm.GetAllParametersText();
                if (p != null)
                {
                    ApplyDeviceClock(p.ClockDate, p.ClockTime);
                    LogUI("Device clock: " + lblDeviceClock.Text, Color.LimeGreen);
                }
                else
                {
                    LogUI("READ CLOCK: no response", Color.Red);
                }
            }
            finally
            {
                btnReadClock.Enabled = true;
            }
        }

        private async Task SendClockAsync(DateTime date, DateTime time)
        {
            if (!EnsureUsbConnected()) return;
            CultureInfo ci = CultureInfo.InvariantCulture;
            string dateArg = date.ToString("yyyy-MM-dd", ci);
            string timeArg = time.ToString("HH:mm:ss", ci);

            if (btnSendClock != null) btnSendClock.Enabled = false;
            if (btnSyncPcClock != null) btnSyncPcClock.Enabled = false;
            try
            {
                LogUI("SET CLOCK → DATE " + dateArg + " / TIME " + timeArg, Color.Yellow);

                string rd = await _comm.SendClockDateAsync(dateArg);
                bool dateOk = rd != null && rd.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
                LogUI("DATE: " + (rd ?? "(no response)"), dateOk ? Color.LimeGreen : Color.Red);

                string rt = await _comm.SendClockTimeAsync(timeArg);
                bool timeOk = rt != null && rt.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
                LogUI("TIME: " + (rt ?? "(no response)"), timeOk ? Color.LimeGreen : Color.Red);

                if (dateOk && timeOk)
                {
                    await Task.Delay(120);
                    MotorParameters p = await _comm.GetAllParametersText();
                    if (p != null) ApplyDeviceClock(p.ClockDate, p.ClockTime);
                    LogUI("Clock updated — device now reads " + lblDeviceClock.Text, Color.LimeGreen);
                }
            }
            finally
            {
                if (btnSendClock != null) btnSendClock.Enabled = true;
                if (btnSyncPcClock != null) btnSyncPcClock.Enabled = true;
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — Live stream toggle (main.c STREAM=0/1)
        // ═════════════════════════════════════════════════════════════

        private bool _suppressStreamEvent;

        private async void ChkLiveStream_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppressStreamEvent) return;

            if (_comm == null || !_comm.IsConnected)
            {
                _suppressStreamEvent = true;
                chkLiveStream.Checked = false;
                _suppressStreamEvent = false;
                LogUI("Live stream: connect first", Color.Orange);
                return;
            }

            bool on = chkLiveStream.Checked;
            bool ok = await _comm.SetStreamText(on);
            LogUI(ok
                ? ("Live stream " + (on ? "ON (STREAM=1) — live telemetry/plots" : "OFF (STREAM=0) — device quiet"))
                : "STREAM toggle failed",
                ok ? Color.LimeGreen : Color.Red);
        }

        /// <summary>
        /// After (re)connecting, makes the device's streaming state match the
        /// Live-stream checkbox. Default is OFF so connect/GET/SET stay instant,
        /// and it resyncs a board that still had streaming enabled.
        /// </summary>
        private async void SyncStreamStateToDevice()
        {
            if (_comm == null || !_comm.IsConnected) return;
            bool want = chkLiveStream != null && chkLiveStream.Checked;
            try { await _comm.SetStreamText(want); } catch { }
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — ADC / Flash / Reset
        // ═════════════════════════════════════════════════════════════

        private async void BtnSaveAdc_Click(object sender, EventArgs e)
        {
            await BtnSaveAdc_ClickAsync();
        }

        private async void BtnSaveFlash_Click(object sender, EventArgs e)
        {
            await BtnSaveFlash_ClickAsync();
        }

        private async void BtnLoadFlash_Click(object sender, EventArgs e)
        {
            await BtnLoadFlash_ClickAsync();
        }

        private async void BtnResetDefault_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                "Reset all STM32 parameters to factory defaults?\n\n" +
                "This changes RAM only. Use main.c SAVE (motor params) to store in QSPI.",
                "Confirm Reset",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;
            await BtnResetDefault_ClickAsync();
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — Terminal
        // ═════════════════════════════════════════════════════════════

        private async void BtnTerminalSend_Click(object sender, EventArgs e)
        {
            string cmd = txtTerminalCmd.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            if (!EnsureUsbConnected()) return;

            txtTerminalCmd.Text = "";
            string low = cmd.ToUpperInvariant();

            if (low == STM32TextCommands.Ping)
            {
                await BtnPing_ClickAsync();
                return;
            }
            if (low == STM32TextCommands.Get)
            {
                await BtnReadAll_ClickAsync();
                return;
            }
            if (low == STM32TextCommands.Report || low == STM32TextCommands.FullReport)
            {
                await RunUsbFullReportAsync();
                return;
            }
            if (low == STM32TextCommands.GraphData || low == STM32TextCommands.Graphs)
            {
                await RunUsbGraphDataAsync();
                return;
            }
            if (low == STM32TextCommands.PhaseCsv || low == STM32TextCommands.Phase3)
            {
                await RunUsbPhaseCsvAsync();
                return;
            }
            if (low.EndsWith("CSV") && low.Length > 3)
            {
                await RunUsbMultiLineAsync(cmd, () => _comm.RequestTechniqueCsv(cmd), "### END_EXPORT");
                return;
            }
            if (low == STM32TextCommands.Calib || low == STM32TextCommands.SaveBase ||
                low == STM32TextCommands.LoadBase || low == STM32TextCommands.ClearBase ||
                low == STM32TextCommands.CalibSt || low == STM32TextCommands.SaveStSt ||
                low == STM32TextCommands.LoadStSt || low == STM32TextCommands.ClearStSt)
            {
                await RunUsbBaselineCommandAsync(cmd);
                return;
            }
            if (low == STM32TextCommands.Save)
            {
                await BtnSaveFlash_ClickAsync();
                return;
            }
            if (low == STM32TextCommands.Load)
            {
                await BtnLoadFlash_ClickAsync();
                return;
            }
            if (low == STM32TextCommands.Default)
            {
                await BtnResetDefault_ClickAsync();
                return;
            }
            if (low == STM32TextCommands.SaveAdc)
            {
                await BtnSaveAdc_ClickAsync();
                return;
            }
            if (low == STM32TextCommands.Help || low == "?")
            {
                string help = await _comm.RequestHelp();
                if (!string.IsNullOrEmpty(help))
                    LogUI("HELP (main.c):\r\n" + help.Trim(), Color.Cyan);
                return;
            }
            if (low.StartsWith(STM32TextCommands.DatePrefix + " "))
            {
                string dateArg = cmd.Substring(STM32TextCommands.DatePrefix.Length).Trim();
                LogUI("USB DATE " + dateArg + "…", Color.Yellow);
                LogMainCTextResponse("DATE", await _comm.SendClockDateAsync(dateArg));
                return;
            }
            if (low.StartsWith(STM32TextCommands.TimePrefix + " "))
            {
                string timeArg = cmd.Substring(STM32TextCommands.TimePrefix.Length).Trim();
                LogUI("USB TIME " + timeArg + "…", Color.Yellow);
                LogMainCTextResponse("TIME", await _comm.SendClockTimeAsync(timeArg));
                return;
            }

            // key=value (RPM= SLIP= BPFO= … LINE= FTH= WTH=) or unknown command
            LogUI("TX> " + cmd + " — " + STM32CommandHints.Get(cmd.Split('=')[0]), Color.White);
            string resp = await _comm.SendMainCTextCommand(cmd);
            LogMainCTextResponse(cmd, resp);

            if (low.StartsWith("RPM=") || low.StartsWith("SLIP=") || low.StartsWith("BPFO=") ||
                low.StartsWith("BPFI=") || low.StartsWith("FTF=") || low.StartsWith("BSF=") ||
                low.StartsWith("FTH=") || low.StartsWith("WTH=") || low.StartsWith("LINE="))
            {
                // Optional: refresh UI from device after parameter write
            }

            txtTerminalCmd.Focus();
        }

        private async Task BtnPing_ClickAsync()
        {
            if (!EnsureUsbConnected()) return;
            btnPing.Enabled = false;
            bool ok = UseTextProtocol ? await _comm.PingText() : await _comm.Ping();
            LogUI(ok ? "PING OK (main.c)" : "PING failed", ok ? Color.LimeGreen : Color.Red);
            btnPing.Enabled = true;
        }

        private async Task BtnReadAll_ClickAsync()
        {
            if (!EnsureUsbConnected()) return;
            btnReadAll.Enabled = false;
            LogUI("GET (main.c)…", Color.Cyan);
            MotorParameters p = await _comm.GetAllParametersText();
            if (p != null)
            {
                PopulateUIFromParameters(p);
                LogUI("GET OK:\r\n" + p.ToString(), Color.LimeGreen);
            }
            else
                LogUI("GET failed", Color.Red);
            btnReadAll.Enabled = true;
        }

        private async Task BtnSaveFlash_ClickAsync()
        {
            if (!EnsureUsbConnected()) return;
            btnSaveFlash.Enabled = false;
            LogUI("SAVE (main.c QSPI params)…", Color.Yellow);
            bool ok = UseTextProtocol ? await _comm.SaveToFlashText() : await _comm.SaveToFlash();
            LogUI(ok ? "SAVE OK" : "SAVE failed", ok ? Color.LimeGreen : Color.Red);
            btnSaveFlash.Enabled = true;
        }

        private async Task BtnLoadFlash_ClickAsync()
        {
            if (!EnsureUsbConnected()) return;
            btnLoadFlash.Enabled = false;
            LogUI("LOAD (main.c QSPI params)…", Color.Yellow);
            bool ok = UseTextProtocol ? await _comm.LoadFromFlashText() : await _comm.LoadFromFlash();
            if (ok)
            {
                await Task.Delay(150);
                MotorParameters p = await _comm.GetAllParametersText();
                if (p != null) PopulateUIFromParameters(p);
                LogUI("LOAD OK — UI updated from GET", Color.LimeGreen);
            }
            else
                LogUI("LOAD failed", Color.Red);
            btnLoadFlash.Enabled = true;
        }

        private async Task BtnResetDefault_ClickAsync()
        {
            if (!EnsureUsbConnected()) return;
            btnResetDefault.Enabled = false;
            LogUI("DEFAULT (main.c)…", Color.Yellow);
            bool ok = UseTextProtocol ? await _comm.ResetToDefaultText() : await _comm.ResetToDefault();
            if (ok)
            {
                await Task.Delay(150);
                MotorParameters p = await _comm.GetAllParametersText();
                if (p != null) PopulateUIFromParameters(p);
                LogUI("DEFAULT OK", Color.LimeGreen);
            }
            else
                LogUI("DEFAULT failed", Color.Red);
            btnResetDefault.Enabled = true;
        }

        private async Task BtnSaveAdc_ClickAsync()
        {
            if (!EnsureUsbConnected()) return;
            btnSaveAdc.Enabled = false;
            LogUI("SAVEADC (main.c → SPI1 NOR on next capture)…", Color.Yellow);
            bool ok = UseTextProtocol ? await _comm.SaveAdcSnapshotText() : await _comm.SaveAdcSnapshot();
            LogUI(ok ? "SAVEADC OK" : "SAVEADC failed", ok ? Color.LimeGreen : Color.Red);
            btnSaveAdc.Enabled = true;
        }

        // ═════════════════════════════════════════════════════════════
        // STM32 Communication Events
        // ═════════════════════════════════════════════════════════════

        private void Comm_OnLineReceived(string line)
        {
            if (_formClosing || _reportParser == null) return;
            _reportParser.ParseLine(line);
        }

        private void Comm_OnLog(string msg)
        {
            LogUI(msg, Color.Gray);
        }

        private void ApplyTelemetryToUi(TelemetryData t)
        {
            if (t == null) return;
            _lastTelemetryForReport = t;

            UpdateLiveTelemetryLabels(t);
            UpdateSettingsMonitorGroup(t);
            UpdateAlarmMonitorDashboard(t);

            ApplyFrequencyLeds(t.FaultLevel, true);
        }

        private void Comm_OnTelemetry(TelemetryData t)
        {
            if (_formClosing) return;

            try
            {
                this.BeginInvoke(new Action(delegate
                {
                    ApplyTelemetryToUi(t);
                }));
            }
            catch { }
        }

        /// <summary>
        /// Bearing LEDs on Harmonics tab — main.c PF O/I/B/T vs FTH/WTH (or global LV if PF not yet received).
        /// Level 0=green, 1=yellow, 2=red, 3=gray (idle).
        /// </summary>
        private void ApplyFrequencyLeds(byte faultLevel, bool connected)
        {
            if (!connected)
            {
                SetBearingChipLevel(chipBpfo, 3);
                SetBearingChipLevel(chipBpfi, 3);
                SetBearingChipLevel(chipBsf, 3);
                SetBearingChipLevel(chipFtf, 3);
                return;
            }

            float warnTh = 2.0f;
            float faultTh = 4.5f;
            TryGetFaultThresholds(out warnTh, out faultTh);

            TelemetryData t = _lastTelemetryForReport;
            if (t != null && (t.Index_Bpfo > 0f || t.Index_Bpfi > 0f || t.Index_Bsf > 0f || t.Index_Ftf > 0f))
            {
                SetBearingChipLevel(chipBpfo, PfIndexToLedLevel(t.Index_Bpfo, warnTh, faultTh));
                SetBearingChipLevel(chipBpfi, PfIndexToLedLevel(t.Index_Bpfi, warnTh, faultTh));
                SetBearingChipLevel(chipBsf, PfIndexToLedLevel(t.Index_Bsf, warnTh, faultTh));
                SetBearingChipLevel(chipFtf, PfIndexToLedLevel(t.Index_Ftf, warnTh, faultTh));

                // main.c DOM: 1=BPFO 2=BPFI 3=BSF 4=FTF — emphasize dominant channel
                switch (t.DominantFault)
                {
                    case 1: if (chipBpfo != null) chipBpfo.Level = Math.Max(chipBpfo.Level, (byte)1); break;
                    case 2: if (chipBpfi != null) chipBpfi.Level = Math.Max(chipBpfi.Level, (byte)1); break;
                    case 3: if (chipBsf != null) chipBsf.Level = Math.Max(chipBsf.Level, (byte)1); break;
                    case 4: if (chipFtf != null) chipFtf.Level = Math.Max(chipFtf.Level, (byte)1); break;
                }
            }
            else
            {
                byte lv = faultLevel > 2 ? (byte)2 : faultLevel;
                SetBearingChipLevel(chipBpfo, lv);
                SetBearingChipLevel(chipBpfi, lv);
                SetBearingChipLevel(chipBsf, lv);
                SetBearingChipLevel(chipFtf, lv);
            }
        }

        private static void SetBearingChipLevel(HarmonicsStatusChip chip, byte level)
        {
            if (chip != null) chip.Level = level;
        }

        private static byte PfIndexToLedLevel(float pfIndex, float warnTh, float faultTh)
        {
            if (pfIndex >= faultTh) return 2;
            if (pfIndex >= warnTh) return 1;
            return 0;
        }

        private void TryGetFaultThresholds(out float warnTh, out float faultTh)
        {
            warnTh = 2.0f;
            faultTh = 4.5f;
            CultureInfo ci = CultureInfo.InvariantCulture;
            if (txtWarnThresh != null &&
                float.TryParse(txtWarnThresh.Text.Trim(), NumberStyles.Float, ci, out float w))
                warnTh = w;
            if (txtFaultThresh != null &&
                float.TryParse(txtFaultThresh.Text.Trim(), NumberStyles.Float, ci, out float f))
                faultTh = f;
        }

        /// <summary>Green = spectral data cached for that technique; gray = not yet received.</summary>
        private void UpdateTechniqueDataLeds(SpectralFrame frame)
        {
            SetTechDataChip(chipFft, frame, HasSpectrumData(
                frame?.FourierPhase1Points, frame?.FourierPhase2Points, frame?.FourierPhase3Points, frame?.FourierPoints));
            SetTechDataChip(chipMusic, frame, HasSpectrumData(
                frame?.MusicPhase1Points, frame?.MusicPhase2Points, frame?.MusicPhase3Points, frame?.MusicPoints));
            SetTechDataChip(chipEsprit, frame, frame != null &&
                (frame.EspritFrequencies.Count > 0 ||
                 frame.EspritPhase1Frequencies.Count > 0 ||
                 frame.EspritPhase2Frequencies.Count > 0 ||
                 frame.EspritPhase3Frequencies.Count > 0));
            SetTechDataChip(chipCyclic2, frame, HasSpectrumData(
                frame?.Cyclic2Phase1Points, frame?.Cyclic2Phase2Points, frame?.Cyclic2Phase3Points, frame?.Cyclic2Points));
            SetTechDataChip(chipSk, frame, HasSpectrumData(
                frame?.SkPhase1Points, frame?.SkPhase2Points, frame?.SkPhase3Points, frame?.SkPoints));
            SetTechDataChip(chipWavelet, frame, HasSpectrumData(
                frame?.WaveletPhase1Points, frame?.WaveletPhase2Points, frame?.WaveletPhase3Points, frame?.WaveletPoints));
            SetTechDataChip(chipWelch, frame, HasSpectrumData(
                null, null, null, frame?.WelchPoints));
            SetTechDataChip(chipCoh, frame, HasSpectrumData(
                null, null, null, frame?.CoherencePoints));
        }

        private static bool HasSpectrumData(
            List<PointF> p1, List<PointF> p2, List<PointF> p3, List<PointF> fallback)
        {
            return (p1 != null && p1.Count > 0) ||
                   (p2 != null && p2.Count > 0) ||
                   (p3 != null && p3.Count > 0) ||
                   (fallback != null && fallback.Count > 0);
        }

        private static void SetTechDataChip(HarmonicsStatusChip chip, SpectralFrame frame, bool hasData)
        {
            if (chip == null) return;
            chip.Level = hasData ? (byte)0 : (byte)3;
        }

        private static Color AlarmSeverityAccentColor(byte faultLevel)
        {
            switch (faultLevel)
            {
                case 0: return Color.FromArgb(39, 174, 96);
                case 1: return Color.FromArgb(243, 156, 18);
                case 2: return Color.FromArgb(192, 57, 43);
                default: return Color.FromArgb(127, 140, 141);
            }
        }

        private static Color AlarmOverallAccentColor(int overall)
        {
            switch (overall)
            {
                case 0: return Color.FromArgb(46, 204, 113);
                case 1: return Color.FromArgb(241, 196, 15);
                default: return Color.FromArgb(192, 57, 43);
            }
        }

        private static int LevelToPercent(int level)
        {
            if (level <= 0) return 0;
            if (level >= 3) return 100;
            return level * 33;
        }

        private static string DominantFaultToText(byte dominantFault)
        {
            switch (dominantFault)
            {
                case 1: return "BPFO (outer)";
                case 2: return "BPFI (inner)";
                case 3: return "BSF (ball)";
                case 4: return "FTF (cage)";
                default: return "none";
            }
        }

        private static string FormatTemperature(TelemetryData t)
        {
            return (t != null && t.HasTemperature)
                ? t.TemperatureC.ToString("F1") + " °C"
                : "--";
        }

        private static void SetMetricLabel(Dictionary<string, Label> target, string key, string value)
        {
            if (target == null || !target.ContainsKey(key) || target[key] == null) return;
            target[key].Text = value;
        }

        private void UpdateLiveTelemetryLabels(TelemetryData t)
        {
            if (t == null)
            {
                foreach (KeyValuePair<string, Label> entry in _liveTelemetryLabels)
                {
                    if (entry.Value != null) entry.Value.Text = "----";
                }
                return;
            }

            SetMetricLabel(_liveTelemetryLabels, "BPFO_HZ", t.BPFO_Hz.ToString("F2") + " Hz");
            SetMetricLabel(_liveTelemetryLabels, "BPFI_HZ", t.BPFI_Hz.ToString("F2") + " Hz");
            SetMetricLabel(_liveTelemetryLabels, "BSF_HZ", t.BSF_Hz.ToString("F2") + " Hz");
            SetMetricLabel(_liveTelemetryLabels, "FTF_HZ", t.FTF_Hz.ToString("F2") + " Hz");
            SetMetricLabel(_liveTelemetryLabels, "FI", t.FaultIndex.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "LV", t.FaultLevelString);
            SetMetricLabel(_liveTelemetryLabels, "LS", t.Index_LS.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "MI", t.Index_Music.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "ES", t.Index_Esprit.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "TK", t.Index_Teager.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "SK", t.Index_SK.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "WV", t.Index_Wavelet.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "CY", t.Index_Cyclic.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "SB", t.Index_Sideband.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "ACF", t.Index_EnvAcf.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "SKPK", t.SkPeak.ToString("F4") + " @ " + t.SkPeakHz.ToString("F2") + " Hz");
            SetMetricLabel(_liveTelemetryLabels, "KB", t.KurtBandHz.ToString(CultureInfo.InvariantCulture));
            SetMetricLabel(_liveTelemetryLabels, "CUSUM", t.CusumScore.ToString("F2"));
            SetMetricLabel(_liveTelemetryLabels, "EMA", t.FaultIndex_Ema.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "PF_O", t.Index_Bpfo.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "PF_I", t.Index_Bpfi.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "PF_B", t.Index_Bsf.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "PF_T", t.Index_Ftf.ToString("F4"));
            SetMetricLabel(_liveTelemetryLabels, "DOM", DominantFaultToText(t.DominantFault));
        }

        private void UpdateSettingsMonitorGroup(TelemetryData t)
        {
            if (txtSettingsTemperature == null) return;

            if (t == null)
            {
                txtSettingsTemperature.Text = "--";
                if (txtSettingsStatorShort != null) txtSettingsStatorShort.Text = "0%";
                if (txtSettingsStatorGround != null) txtSettingsStatorGround.Text = "0%";
                if (barSettingsStatorShort != null) barSettingsStatorShort.Value = 0;
                if (barSettingsStatorGround != null) barSettingsStatorGround.Value = 0;
                foreach (KeyValuePair<string, Label> entry in _settingsMonitorLabels)
                {
                    if (entry.Value != null) entry.Value.Text = "----";
                }
                return;
            }

            int statorFaultPercent = LevelToPercent(t.Stator_FaultLevel);
            int statorGroundPercent = LevelToPercent(t.Stator_GndLevel);

            txtSettingsTemperature.Text = FormatTemperature(t);
            if (txtSettingsStatorShort != null)
                txtSettingsStatorShort.Text = statorFaultPercent.ToString(CultureInfo.InvariantCulture) + "%";
            if (txtSettingsStatorGround != null)
                txtSettingsStatorGround.Text = statorGroundPercent.ToString(CultureInfo.InvariantCulture) + "%";
            if (barSettingsStatorShort != null)
                barSettingsStatorShort.Value = statorFaultPercent;
            if (barSettingsStatorGround != null)
                barSettingsStatorGround.Value = statorGroundPercent;

            SetMetricLabel(_settingsMonitorLabels, "SHORT", t.Stator_ShortLevel.ToString(CultureInfo.InvariantCulture));
            SetMetricLabel(_settingsMonitorLabels, "GROUND", t.Stator_GndLevel.ToString(CultureInfo.InvariantCulture));
            SetMetricLabel(_settingsMonitorLabels, "NSR", t.Stator_NSR.ToString("F4"));
            SetMetricLabel(_settingsMonitorLabels, "ZSR", t.Stator_ZSR.ToString("F4"));
        }

        private void ResetTelemetryDisplays()
        {
            UpdateLiveTelemetryLabels(null);
            UpdateSettingsMonitorGroup(null);
            UpdateReportPageFromCurrentData();
        }

        /// <summary>
        /// Refreshes Report page with latest telemetry + technique results.
        /// </summary>
        private void UpdateAlarmMonitorDashboard(TelemetryData t)
        {
            UpdateReportPageFromCurrentData();
        }

        private void UpdateReportPageFromCurrentData()
        {
            if (rtbTechniqueReport == null) return;

            bool isConnected = _comm != null && _comm.IsConnected;
            UpdateReportConnectionBadge(isConnected);
            UpdateReportMetricCards(_lastTelemetryForReport);
            UpdateReportTechniqueTiles(_lastTelemetryForReport);
            UpdateReportStatorView(_lastTelemetryForReport);

            UpdateReportHeaderMetadata(isConnected);

            rtbTechniqueReport.Text = BuildDiagnosticReportText(
                chkReportShowReference != null && chkReportShowReference.Checked);
        }

        private void UpdateReportConnectionBadge(bool connected)
        {
            if (pnlReportHeader == null) return;
            string port = cmbPorts != null && cmbPorts.SelectedItem != null
                ? cmbPorts.SelectedItem.ToString() : "USB";
            pnlReportHeader.LinkOnline = connected;
            pnlReportHeader.LinkText = connected ? "LIVE · " + port : "OFFLINE";
            pnlReportHeader.Invalidate();
        }

        /// <summary>Populates the professional report header metadata block.</summary>
        private void UpdateReportHeaderMetadata(bool connected)
        {
            if (pnlReportHeader == null) return;

            string port = cmbPorts != null && cmbPorts.SelectedItem != null
                ? cmbPorts.SelectedItem.ToString() : "—";

            DateTime now = DateTime.Now;
            pnlReportHeader.ReportId = "RPT-" + now.ToString("yyyyMMdd-HHmmss");
            pnlReportHeader.GeneratedText = now.ToString("yyyy-MM-dd  HH:mm:ss");
            pnlReportHeader.DeviceText = connected ? port : "Not connected";
            pnlReportHeader.FirmwareText = "STM32H750 · DSP MCSA";

            // Verdict shown as a compact severity chip in the header band.
            TelemetryData t = _lastTelemetryForReport;
            if (t == null)
            {
                pnlReportHeader.VerdictSeverity = -1;
                pnlReportHeader.VerdictText = "AWAITING DATA";
            }
            else
            {
                int overall = t.FaultLevel > 2 ? 2 : t.FaultLevel;
                int stator = StatorSeverityFromTelemetry(t);
                int worst = Math.Max(overall, stator);
                pnlReportHeader.VerdictSeverity = worst;
                switch (worst)
                {
                    case 0: pnlReportHeader.VerdictText = "HEALTHY"; break;
                    case 1: pnlReportHeader.VerdictText = "WARNING"; break;
                    case 2: pnlReportHeader.VerdictText = "FAULT"; break;
                    default: pnlReportHeader.VerdictText = "—"; break;
                }
            }

            pnlReportHeader.Invalidate();
        }

        private void UpdateReportMetricCards(TelemetryData t)
        {
            float warnTh = 2f;
            float faultTh = 4.5f;
            TryGetFaultThresholds(out warnTh, out faultTh);

            if (t == null)
            {
                SetReportCard(cardReportOverall, -1, "—", "No telemetry");
                SetReportCard(cardReportBpfo, -1, "—", "PF_O");
                SetReportCard(cardReportBpfi, -1, "—", "PF_I");
                SetReportCard(cardReportBsf, -1, "—", "PF_B / PF_T");
                SetReportCard(cardReportStator, -1, "—", "NSR / GND");
                return;
            }

            int overall = t.FaultLevel > 2 ? 2 : t.FaultLevel;
            SetReportCard(cardReportOverall, overall, SeverityStatusText(overall),
                "FI " + t.FaultIndex.ToString("F2") + "  ·  " + DominantFaultToText(t.DominantFault));

            int bpfo = PfIndexToLedLevel(t.Index_Bpfo, warnTh, faultTh);
            int bpfi = PfIndexToLedLevel(t.Index_Bpfi, warnTh, faultTh);
            int bsf = PfIndexToLedLevel(t.Index_Bsf, warnTh, faultTh);
            int ftf = PfIndexToLedLevel(t.Index_Ftf, warnTh, faultTh);
            int bsfWorst = (byte)Math.Max(bsf, ftf);

            SetReportCard(cardReportBpfo, bpfo, SeverityStatusText(bpfo),
                "PF " + t.Index_Bpfo.ToString("F2") + "  ·  " + t.BPFO_Hz.ToString("F1") + " Hz");
            SetReportCard(cardReportBpfi, bpfi, SeverityStatusText(bpfi),
                "PF " + t.Index_Bpfi.ToString("F2") + "  ·  " + t.BPFI_Hz.ToString("F1") + " Hz");
            SetReportCard(cardReportBsf, bsfWorst, SeverityStatusText(bsfWorst),
                "B " + t.Index_Bsf.ToString("F2") + "  T " + t.Index_Ftf.ToString("F2"));

            int stator = StatorSeverityFromTelemetry(t);
            SetReportCard(cardReportStator, stator, StatorFirmwareLevelText(t.Stator_FaultLevel),
                "SLV " + t.Stator_FaultLevel + "  ·  SH " + t.Stator_ShortLevel + "  GD " + t.Stator_GndLevel);
        }

        private void UpdateReportStatorView(TelemetryData t)
        {
            if (t == null)
            {
                if (pnlStatorHeader != null)
                {
                    pnlStatorHeader.VerdictSeverity = -1;
                    pnlStatorHeader.VerdictText = "NO DATA";
                    pnlStatorHeader.SummaryText = "Connect USB and wait for the [STATOR] telemetry line.";
                    pnlStatorHeader.FortescueText = "I0 —   I1 —   I2 —";
                    pnlStatorHeader.Invalidate();
                }
                if (cardReportStatorShort != null) cardReportStatorShort.SetNoData();
                if (cardReportStatorGround != null) cardReportStatorGround.SetNoData();
                if (_reportStatorTiles != null)
                {
                    foreach (var tile in _reportStatorTiles)
                    {
                        if (tile == null) continue;
                        tile.SetNoData();
                    }
                }
                return;
            }

            // ── Section header: overall stator verdict + Fortescue summary ──
            int overallStator = StatorSeverityFromTelemetry(t);
            if (pnlStatorHeader != null)
            {
                pnlStatorHeader.VerdictSeverity = overallStator;
                pnlStatorHeader.VerdictText = StatorVerdictText(overallStator);
                pnlStatorHeader.SummaryText = string.Format(CultureInfo.InvariantCulture,
                    "Overall SLV {0}   ·   Short {1}   ·   Ground {2}   ·   f = {3:F1} Hz",
                    t.Stator_FaultLevel,
                    StatorFirmwareLevelText(t.Stator_ShortLevel),
                    StatorFirmwareLevelText(t.Stator_GndLevel),
                    t.Stator_FrequencyHz);
                pnlStatorHeader.FortescueText = string.Format(CultureInfo.InvariantCulture,
                    "Fortescue:  I0 {0:F4}   I1 {1:F4}   I2 {2:F4}",
                    t.Stator_I0Mag, t.Stator_I1Mag, t.Stator_I2Mag);
                pnlStatorHeader.Invalidate();
            }

            // ── Inter-turn short card (CUSUM gauge: early 2.5 / warn 4.0 / alarm 6.5) ──
            int shUi = StatorFirmwareLevelToCardSeverity(t.Stator_ShortLevel);
            if (cardReportStatorShort != null)
            {
                cardReportStatorShort.Severity = shUi;
                cardReportStatorShort.StatusText = StatorFirmwareLevelText(t.Stator_ShortLevel);
                cardReportStatorShort.IsEarly = t.Stator_EarlyShort != 0 && t.Stator_ShortLevel <= 1;
                cardReportStatorShort.PrimaryLabel = "SI EMA";
                cardReportStatorShort.PrimaryValue = t.Stator_ShortIndexEma.ToString("F2", CultureInfo.InvariantCulture);
                cardReportStatorShort.SecondaryText = string.Format(CultureInfo.InvariantCulture,
                    "NSR {0:F4}   ·   SI {1:F2}", t.Stator_NSR, t.Stator_ShortIndex);
                cardReportStatorShort.Cusum = t.Stator_CusumShort;
                cardReportStatorShort.ActionText = StatorActionText(t.Stator_ShortLevel,
                    cardReportStatorShort.IsEarly, "inter-turn short");
                cardReportStatorShort.Invalidate();
            }

            // ── Ground / earth fault card ──
            int gdUi = StatorFirmwareLevelToCardSeverity(t.Stator_GndLevel);
            if (cardReportStatorGround != null)
            {
                cardReportStatorGround.Severity = gdUi;
                cardReportStatorGround.StatusText = StatorFirmwareLevelText(t.Stator_GndLevel);
                cardReportStatorGround.IsEarly = t.Stator_EarlyGnd != 0 && t.Stator_GndLevel <= 1;
                cardReportStatorGround.PrimaryLabel = "GI EMA";
                cardReportStatorGround.PrimaryValue = t.Stator_GndIndexEma.ToString("F2", CultureInfo.InvariantCulture);
                cardReportStatorGround.SecondaryText = string.Format(CultureInfo.InvariantCulture,
                    "ZSR {0:F4}   ·   GI {1:F2}", t.Stator_ZSR, t.Stator_GndIndex);
                cardReportStatorGround.Cusum = t.Stator_CusumGnd;
                cardReportStatorGround.ActionText = StatorActionText(t.Stator_GndLevel,
                    cardReportStatorGround.IsEarly, "ground fault");
                cardReportStatorGround.Invalidate();
            }

            if (_reportStatorTiles == null) return;
            for (int i = 0; i < MainCStatorBaseline.Length && i < _reportStatorTiles.Length; i++)
            {
                StatorBaselineDescriptor f = MainCStatorBaseline[i];
                StatorMetricTile tile = _reportStatorTiles[i];
                float val = f.GetValue(t);
                int fw = StatorMetricFwLevel(val, f.Early, f.Warn, f.Alarm);
                tile.Value = val;
                tile.Severity = StatorFirmwareLevelToCardSeverity(fw);
                tile.StatusText = StatorFirmwareLevelText(fw);
                tile.Invalidate();
            }
        }

        private static string StatorVerdictText(int severity)
        {
            switch (severity)
            {
                case 0: return "HEALTHY";
                case 1: return "WATCH";
                case 2: return "FAULT";
                default: return "NO DATA";
            }
        }

        /// <summary>Recommended maintenance action for the stator fault cards.</summary>
        private static string StatorActionText(int firmwareLevel, bool early, string faultName)
        {
            if (firmwareLevel >= 3)
                return "ALARM — de-energise & inspect winding (" + faultName + "). Megger / surge test.";
            if (firmwareLevel == 2)
                return "WARNING — schedule winding inspection; trend " + faultName + " closely.";
            if (firmwareLevel == 1 || early)
                return "EARLY — incipient " + faultName + " signature; increase monitoring rate.";
            return "Healthy — no " + faultName + " signature above baseline.";
        }

        private void UpdateReportTechniqueTiles(TelemetryData t)
        {
            if (_reportTechniqueTiles == null) return;

            float warnTh = 2f;
            float faultTh = 4.5f;
            TryGetFaultThresholds(out warnTh, out faultTh);

            for (int i = 0; i < MainCTechniqueFeatures.Length && i < _reportTechniqueTiles.Length; i++)
            {
                MainCFeatureDescriptor feat = MainCTechniqueFeatures[i];
                ReportTechniqueTile tile = _reportTechniqueTiles[i];
                tile.BlIndex = feat.BlIndex;
                tile.DetectionPath = feat.DetectionPath;
                tile.StreamKey = feat.StreamKey;
                tile.MethodName = feat.MethodName;
                tile.HasHarmonicsPlot = feat.HasHarmonicsPlot;
                tile.IsBearingPf = feat.IsBearingPf;
                tile.WarnThreshold = warnTh;
                tile.FaultThreshold = faultTh;

                if (t == null)
                {
                    tile.IndexValue = float.NaN;
                    tile.Severity = -1;
                    tile.Subtitle = feat.ShortName;
                    tile.Invalidate();
                    continue;
                }

                float val = feat.GetIndex(t);
                tile.IndexValue = val;
                tile.Severity = PfIndexToLedLevel(val, warnTh, faultTh);
                tile.Subtitle = feat.IsBearingPf
                    ? feat.ShortName + "  PF"
                    : (feat.HasHarmonicsPlot ? feat.ShortName + "  plot" : feat.ShortName + "  idx");
                tile.Invalidate();
            }
        }

        private static void SetReportCard(ReportMetricCard card, int severity, string status, string detail)
        {
            if (card == null) return;
            card.Severity = severity;
            card.StatusText = status;
            card.DetailText = detail;
            card.Invalidate();
        }

        private static string SeverityStatusText(int severity)
        {
            switch (severity)
            {
                case 0: return "OK";
                case 1: return "WARN";
                case 2: return "FAULT";
                default: return "WAIT";
            }
        }

        private static int StatorSeverityFromTelemetry(TelemetryData t)
        {
            if (t == null) return -1;
            return StatorFirmwareLevelToCardSeverity(Math.Max(t.Stator_ShortLevel, Math.Max(t.Stator_GndLevel, t.Stator_FaultLevel)));
        }

        private static int StatorFirmwareLevelToCardSeverity(int firmwareLevel)
        {
            if (firmwareLevel >= 3) return 2;
            if (firmwareLevel >= 1) return 1;
            return 0;
        }

        private static string StatorFirmwareLevelText(int firmwareLevel)
        {
            switch (firmwareLevel)
            {
                case 0: return "OK";
                case 1: return "EARLY";
                case 2: return "WARN";
                case 3: return "ALARM";
                default: return "LV" + firmwareLevel.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static int StatorMetricFwLevel(float v, float early, float warn, float alarm)
        {
            if (v >= alarm) return 3;
            if (v >= warn) return 2;
            if (v >= early) return 1;
            return 0;
        }

        private string BuildDiagnosticReportText(bool includeUsbReference)
        {
            bool isConnected = _comm != null && _comm.IsConnected;
            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("EES MOTOR OVERLOAD — LIVE DIAGNOSTIC REPORT");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine("Generated     : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Connection    : " + (isConnected ? "Connected" : "Disconnected"));
            if (isConnected && cmbPorts != null && cmbPorts.SelectedItem != null)
                sb.AppendLine("Port          : " + cmbPorts.SelectedItem);
            sb.AppendLine("Harmonics view: " + _currentTechnique);
            sb.AppendLine();

            if (_lastTelemetryForReport == null)
            {
                sb.AppendLine(">>> Connect USB and wait for telemetry, then press REPORT.");
                sb.AppendLine();
            }
            else
            {
                TelemetryData t = _lastTelemetryForReport;
                sb.AppendLine("── EXECUTIVE SUMMARY ──");
                sb.AppendLine("  Overall      : " + t.FaultLevelString + "  (FI=" + t.FaultIndex.ToString("F4") + ", EMA=" + t.FaultIndex_Ema.ToString("F4") + ")");
                sb.AppendLine("  Dominant     : " + DominantFaultToText(t.DominantFault));
                sb.AppendLine("  CUSUM        : " + t.CusumScore.ToString("F2"));
                sb.AppendLine("  Temperature  : " + FormatTemperature(t));
                sb.AppendLine();

                sb.AppendLine("── BEARING (Hz / partial fault index) ──");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  BPFO  {0,8:F2} Hz   PF_O {1:F4}", t.BPFO_Hz, t.Index_Bpfo));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  BPFI  {0,8:F2} Hz   PF_I {1:F4}", t.BPFI_Hz, t.Index_Bpfi));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  BSF   {0,8:F2} Hz   PF_B {1:F4}", t.BSF_Hz, t.Index_Bsf));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  FTF   {0,8:F2} Hz   PF_T {1:F4}", t.FTF_Hz, t.Index_Ftf));
                sb.AppendLine();

                float warnTh = 2f;
                float faultTh = 4.5f;
                TryGetFaultThresholds(out warnTh, out faultTh);
                AppendMainCTwelveTechniqueTable(sb, t, warnTh, faultTh);
                sb.AppendLine();

                AppendStatorDiagnosticTable(sb, t);
                sb.AppendLine();
            }

            if (_lastFrame != null && _lastFrame.ReportMotorParams != null)
            {
                MotorParameters mp = _lastFrame.ReportMotorParams;
                sb.AppendLine("── MOTOR PARAMETERS ──");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  RPM {0:F0}   Slip {1:F4}   LINE {2:F1} Hz",
                    mp.MotorRPM, mp.Slip, mp.SupplyLineHz));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  BPFO {0:F4}  BPFI {1:F4}  BSF {2:F4}  FTF {3:F4}",
                    mp.BPFO, mp.BPFI, mp.BSF, mp.FTF));
                sb.AppendLine();
            }

            if (_lastFrame != null)
            {
                sb.AppendLine("── SPECTRAL DATA IN MEMORY ──");
                sb.AppendLine("  Mode / end   : " + (string.IsNullOrEmpty(_lastFrame.Mode) ? "n/a" : _lastFrame.Mode)
                    + "  |  " + (string.IsNullOrEmpty(_lastFrame.FinalReportSummary) ? "n/a" : _lastFrame.FinalReportSummary));
                sb.AppendLine("  Technique      P1      P2      P3     Mean");
                sb.AppendLine("  FOURIER      " + Pad4(_lastFrame.FourierPhase1Points.Count) + Pad4(_lastFrame.FourierPhase2Points.Count)
                    + Pad4(_lastFrame.FourierPhase3Points.Count) + Pad4(_lastFrame.FourierPoints.Count));
                sb.AppendLine("  MUSIC        " + Pad4(_lastFrame.MusicPhase1Points.Count) + Pad4(_lastFrame.MusicPhase2Points.Count)
                    + Pad4(_lastFrame.MusicPhase3Points.Count) + Pad4(_lastFrame.MusicPoints.Count));
                sb.AppendLine("  CYCLIC2      " + Pad4(_lastFrame.Cyclic2Phase1Points.Count) + Pad4(_lastFrame.Cyclic2Phase2Points.Count)
                    + Pad4(_lastFrame.Cyclic2Phase3Points.Count) + Pad4(_lastFrame.Cyclic2Points.Count));
                sb.AppendLine("  SK           " + Pad4(_lastFrame.SkPhase1Points.Count) + Pad4(_lastFrame.SkPhase2Points.Count)
                    + Pad4(_lastFrame.SkPhase3Points.Count) + Pad4(_lastFrame.SkPoints.Count));
                sb.AppendLine("  WAVELET      " + Pad4(_lastFrame.WaveletPhase1Points.Count) + Pad4(_lastFrame.WaveletPhase2Points.Count)
                    + Pad4(_lastFrame.WaveletPhase3Points.Count) + Pad4(_lastFrame.WaveletPoints.Count));
                sb.AppendLine("  ESPRIT freqs : " + _lastFrame.EspritFrequencies.Count);
                sb.AppendLine();
                sb.AppendLine("  Open Harmonics tab for spectrum plots (FFT / MUSIC / … buttons).");
            }
            else
            {
                sb.AppendLine("── SPECTRAL DATA ──");
                sb.AppendLine("  No frame yet. Press REPORT on this page or a technique on Harmonics.");
                sb.AppendLine();
            }

            if (includeUsbReference)
            {
                sb.AppendLine();
                sb.AppendLine(new string('=', 72));
                AppendUsbCommandReference(sb);
            }

            return sb.ToString();
        }

        private static string Pad4(int n)
        {
            return n.ToString(CultureInfo.InvariantCulture).PadLeft(4);
        }

        /// <summary>Stator section: [STATOR] USB line + 10× SB baseline (main.c CALIBST).</summary>
        private static void AppendStatorDiagnosticTable(StringBuilder sb, TelemetryData t)
        {
            sb.AppendLine("── STATOR WINDING (main.c [STATOR] / CALIBST baseline) ──");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Overall SLV {0}   Short SH={1} ({2})   Ground GD={3} ({4})",
                t.Stator_FaultLevel,
                t.Stator_ShortLevel, StatorFirmwareLevelText(t.Stator_ShortLevel),
                t.Stator_GndLevel, StatorFirmwareLevelText(t.Stator_GndLevel)));
            if (t.Stator_EarlyShort != 0 || t.Stator_EarlyGnd != 0)
                sb.AppendLine("  Early flags  : ES=" + t.Stator_EarlyShort + "  EG=" + t.Stator_EarlyGnd);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Fused index  : SI {0:F3}  GI {1:F3}   EMA SI {2:F3}  GI {3:F3}",
                t.Stator_ShortIndex, t.Stator_GndIndex, t.Stator_ShortIndexEma, t.Stator_GndIndexEma));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  CUSUM        : CS {0:F2} (early 2.5 warn 4.0 alarm 6.5)   CG {1:F2}",
                t.Stator_CusumShort, t.Stator_CusumGnd));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Fortescue    : I0 {0:F5}  I1 {1:F5}  I2 {2:F5}",
                t.Stator_I0Mag, t.Stator_I1Mag, t.Stator_I2Mag));
            sb.AppendLine();
            sb.AppendLine("  SB#  Key       Metric           Value      Status   EARLY    WARN     ALARM");
            sb.AppendLine("  ---  --------  ---------------- ---------- -------- -------- -------- --------");

            var ranked = new List<Tuple<int, float>>();
            for (int i = 0; i < MainCStatorBaseline.Length; i++)
            {
                float v = MainCStatorBaseline[i].GetValue(t);
                ranked.Add(Tuple.Create(i, v));
            }
            ranked.Sort((a, b) => b.Item2.CompareTo(a.Item2));

            for (int i = 0; i < MainCStatorBaseline.Length; i++)
            {
                StatorBaselineDescriptor f = MainCStatorBaseline[i];
                float val = f.GetValue(t);
                int fw = StatorMetricFwLevel(val, f.Early, f.Warn, f.Alarm);
                string valStr = f.ValueIsPercent
                    ? val.ToString("F1", CultureInfo.InvariantCulture) + "%"
                    : val.ToString("F4", CultureInfo.InvariantCulture);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,2}  {1,-8}  {2,-16} {3,10}  {4,-7}  {5,7}  {6,7}  {7,7}",
                    f.SbIndex, f.StreamKey, f.MethodName, valStr, StatorFirmwareLevelText(fw),
                    f.Early, f.Warn, f.Alarm));
            }

            sb.AppendLine();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Aux NSR_H5 {0:F4}  (5th-harmonic NSR, short-path metric)", t.Stator_NsrH5));
            sb.AppendLine("  Top stator metrics:");
            for (int r = 0; r < Math.Min(3, ranked.Count); r++)
            {
                int idx = ranked[r].Item1;
                StatorBaselineDescriptor f = MainCStatorBaseline[idx];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0}. {1} ({2}) = {3}  — {4}",
                    r + 1, f.MethodName, f.StreamKey,
                    f.ValueIsPercent ? ranked[r].Item2.ToString("F1", CultureInfo.InvariantCulture) + "%" : ranked[r].Item2.ToString("F4", CultureInfo.InvariantCulture),
                    f.Note));
            }
            sb.AppendLine("  Baseline USB: CALIBST → SAVESTST → LOADSTST / CLEARSTST (QSPI, main.c)");
        }

        /// <summary>Intelligent 15-technique section aligned with main.c baseline + detection paths.</summary>
        private void AppendMainCTwelveTechniqueTable(StringBuilder sb, TelemetryData t, float warnTh, float faultTh)
        {
            sb.AppendLine("── 15 TECHNIQUES (main.c detection paths + BL_* baseline) ──");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Thresholds: WARN ≥ {0:F2}   FAULT ≥ {1:F2}  (partial-fault / method indices)", warnTh, faultTh));
            sb.AppendLine("  BL#  Path  Key    Method        Index    Status   Harmonics USB");
            sb.AppendLine("  ---  ----  ----   ------------- -------- -------- ----------------");

            var ranked = new List<Tuple<int, float>>();
            for (int i = 0; i < MainCTechniqueFeatures.Length; i++)
            {
                float v = MainCTechniqueFeatures[i].GetIndex(t);
                ranked.Add(Tuple.Create(i, v));
            }
            ranked.Sort((a, b) => b.Item2.CompareTo(a.Item2));

            for (int i = 0; i < MainCTechniqueFeatures.Length; i++)
            {
                MainCFeatureDescriptor f = MainCTechniqueFeatures[i];
                float val = f.GetIndex(t);
                int sev = PfIndexToLedLevel(val, warnTh, faultTh);
                string blCol = f.BlIndex >= 0 ? f.BlIndex.ToString(CultureInfo.InvariantCulture).PadLeft(2) : " —";
                string pathCol = f.DetectionPath > 0 ? f.DetectionPath.ToString(CultureInfo.InvariantCulture).PadLeft(4) : "  — ";
                string plotCol = f.HasHarmonicsPlot ? f.UsbCsvCommand : "(stream only)";
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}  {1}  {2,-5}  {3,-13} {4,8:F4}  {5,-7}  {6}",
                    blCol, pathCol, f.StreamKey, f.MethodName, val, SeverityStatusText(sev), plotCol));
            }

            sb.AppendLine();
            sb.AppendLine("  Top contributors (by index):");
            for (int r = 0; r < Math.Min(3, ranked.Count); r++)
            {
                int idx = ranked[r].Item1;
                MainCFeatureDescriptor f = MainCTechniqueFeatures[idx];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0}. {1} ({2}) = {3:F4}  — {4}",
                    r + 1, f.MethodName, f.StreamKey, ranked[r].Item2, f.MainCPathNote));
            }

            sb.AppendLine();
            sb.AppendLine("── FUSION & AUXILIARY (main.c paths 11–12, not BL slots) ──");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  FI {0:F4}   EMA {1:F4}   CUSUM {2:F2}   DOM {3}",
                t.FaultIndex, t.FaultIndex_Ema, t.CusumScore, DominantFaultToText(t.DominantFault)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  SK peak {0:F4} @ {1:F1} Hz   Kurt band {2} Hz",
                t.SkPeak, t.SkPeakHz, t.KurtBandHz));
            sb.AppendLine("  Path (11): adaptive baseline CALIB / SAVEBASE / LOADBASE (QSPI)");
            sb.AppendLine("  Path (12): EMA + CUSUM on fused index → WARN / ALARM");
        }

        private void ExportDiagnosticReport()
        {
            try
            {
                SaveFileDialog dlg = new SaveFileDialog
                {
                    Filter = "Text Report (*.txt)|*.txt",
                    FileName = "H750_Diagnostic_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                    DefaultExt = "txt",
                    Title = "Export diagnostic report"
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                bool inclRef = chkReportShowReference != null && chkReportShowReference.Checked;
                File.WriteAllText(dlg.FileName, BuildDiagnosticReportText(inclRef), Encoding.UTF8);
                LogUI("Report exported: " + dlg.FileName, Color.LimeGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n" + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyDiagnosticReportToClipboard()
        {
            try
            {
                Clipboard.SetText(rtbTechniqueReport != null ? rtbTechniqueReport.Text : BuildDiagnosticReportText(false));
                LogUI("Report copied to clipboard", Color.LimeGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Copy failed:\n" + ex.Message, "Copy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>USB text/binary commands from H750 main.c HELP (USB_Process_TextLine).</summary>
        private static void AppendUsbCommandReference(StringBuilder sb)
        {
            sb.AppendLine("┌──────────────────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│  6. USB COMMAND REFERENCE (H750 main.c)                                 │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────────────────┤");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  ── Text Commands ─────────────────────────────────────────────────     │");
            sb.AppendLine("│  +──────────────────────┬────────────────────────────────────────────+  │");
            sb.AppendLine("│  │ Command              │ Description                                │  │");
            sb.AppendLine("│  +──────────────────────┼────────────────────────────────────────────+  │");
            sb.AppendLine("│  │ PING                 │ Connection test — returns OK                │  │");
            sb.AppendLine("│  │ GET                  │ Read all parameters (RPM,SLIP,BPFO…)       │  │");
            sb.AppendLine("│  │ REPORT / FULLREPORT  │ Full diagnostic: scalars + spectral + graph│  │");
            sb.AppendLine("│  │ GRAPHDATA / GRAPHS   │ Phase ABC + FFT + envelope data            │  │");
            sb.AppendLine("│  │ PHASE3 / PHASECSV    │ Raw 3-phase current CSV export             │  │");
            sb.AppendLine("│  │ FFTCSV               │ FFT spectral data export                   │  │");
            sb.AppendLine("│  │ MUSICCSV             │ MUSIC spectral data export                 │  │");
            sb.AppendLine("│  │ ESPRITCSV            │ ESPRIT frequency estimation export         │  │");
            sb.AppendLine("│  │ CYCLIC2CSV           │ Cyclostationary analysis export             │  │");
            sb.AppendLine("│  │ SKCSV                │ Spectral Kurtosis data export              │  │");
            sb.AppendLine("│  │ WAVELETCSV           │ Wavelet decomposition export               │  │");
            sb.AppendLine("│  │ SAVE / LOAD / DEFAULT│ Flash parameter storage operations         │  │");
            sb.AppendLine("│  │ SAVEADC              │ ADC snapshot to SPI1 NOR flash             │  │");
            sb.AppendLine("│  │ CALIB / SAVEBASE     │ Bearing baseline calibration               │  │");
            sb.AppendLine("│  │ CALIBST / SAVESTST   │ Stator baseline calibration                │  │");
            sb.AppendLine("│  │ RPM= SLIP= LINE=    │ Set individual parameter (text protocol)   │  │");
            sb.AppendLine("│  +──────────────────────┴────────────────────────────────────────────+  │");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  ── Binary Commands ───────────────────────────────────────────────     │");
            sb.AppendLine("│  +──────────────────────┬────────────────────────────────────────────+  │");
            sb.AppendLine("│  │ Code                 │ Description                                │  │");
            sb.AppendLine("│  +──────────────────────┼────────────────────────────────────────────+  │");
            sb.AppendLine("│  │ 0x04 – 0x0B          │ Set float parameters (framed)              │  │");
            sb.AppendLine("│  │ 0x80                 │ GET_ALL_PARAMS (full parameter dump)       │  │");
            sb.AppendLine("│  │ 0x30 – 0x32          │ Flash save / load / reset to default       │  │");
            sb.AppendLine("│  │ 0x40                 │ SAVE_ADC to SPI1 NOR flash                 │  │");
            sb.AppendLine("│  │ 0xFE                 │ PING (binary connection test)              │  │");
            sb.AppendLine("│  +──────────────────────┴────────────────────────────────────────────+  │");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("└──────────────────────────────────────────────────────────────────────────┘");
        }

        private static void AppendUsbCmdRow(StringBuilder sb, string cmd, string fw, string pc)
        {
            sb.Append("  | ");
            sb.Append((cmd ?? "").PadRight(16));
            sb.Append(" | ");
            sb.Append((fw ?? "").PadRight(30));
            sb.Append(" | ");
            sb.Append((pc ?? "").PadRight(25));
            sb.AppendLine(" |");
        }

        private static void AppendReportRow(StringBuilder sb, string metric, string value)
        {
            sb.Append("  | ");
            sb.Append((metric ?? "").PadRight(14));
            sb.Append(" | ");
            sb.Append((value ?? "").PadRight(23));
            sb.AppendLine(" |");
        }

        private void LogUI(string message, Color color)
        {
            if (_formClosing) return;

            try
            {
                if (rtbLog == null) return;

                if (rtbLog.InvokeRequired)
                {
                    rtbLog.BeginInvoke(new Action(delegate { LogUI(message, color); }));
                    return;
                }

                string time = DateTime.Now.ToString("HH:mm:ss.fff");
                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.SelectionColor = Color.DarkGray;
                rtbLog.AppendText("[" + time + "] ");
                rtbLog.SelectionColor = color;
                rtbLog.AppendText(message + "\n");
                rtbLog.ScrollToCaret();

                if (rtbLog.TextLength > 50000)
                {
                    rtbLog.SelectionStart = 0;
                    rtbLog.SelectionLength = rtbLog.TextLength / 2;
                    rtbLog.SelectedText = "";
                }
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════
        // Shared UI Helpers
        // ═════════════════════════════════════════════════════════════

        private Label CreateLabel(string text, int x, int y, Control parent, float fontSize = 10F)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font("Tahoma", fontSize),
                ForeColor = Color.Black
            };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private void InitializeServices()
        {
            try
            {
                _dbService = new DatabaseService(DB_CONNECTION_STRING);
                Logger.Info("Services initialized");
            }
            catch (Exception ex)
            {
                Logger.Error($"InitializeServices error: {ex.Message}", ex);
            }
        }

        private void LoadApplicationData()
        {
            try
            {
                LoadComboBoxAsync();
                Logger.Info("Application data loaded");
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadApplicationData error: {ex.Message}", ex);
            }
        }

        private async void LoadComboBoxAsync()
        {
            try
            {
                var bearings = await _dbService.GetAllBearingsAsync();
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() =>
                    {
                        cmbBearingSelect.DataSource = bearings;
                        cmbBearingSelect.DisplayMember = "BearNB";
                        cmbBearingSelect.ValueMember = "ID";
                        if (dgvBearings != null)
                            dgvBearings.DataSource = bearings;
                    }));
                }
                else
                {
                    cmbBearingSelect.DataSource = bearings;
                    cmbBearingSelect.DisplayMember = "BearNB";
                    cmbBearingSelect.ValueMember = "ID";
                    if (dgvBearings != null)
                        dgvBearings.DataSource = bearings;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadComboBoxAsync error: {ex.Message}", ex);
            }
        }

        private async void ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (cmbBearingSelect.SelectedValue == null ||
                    !(cmbBearingSelect.SelectedValue is int))
                    return;

                int bearingId = (int)cmbBearingSelect.SelectedValue;
                var bearing = await _dbService.GetBearingByIdAsync(bearingId);

                if (bearing != null)
                {
                    txtID.Text = bearing.ID?.ToString() ?? "";
                    txtBea.Text = bearing.BearNB?.ToString() ?? "";
                    txtNB.Text = bearing.NB?.ToString() ?? "";
                    txtBD.Text = bearing.BD?.ToString() ?? "";
                    txtPD.Text = bearing.PD?.ToString() ?? "";
                    txtPHI.Text = bearing.PHI?.ToString() ?? "";
                    txtBPFO.Text = bearing.BPFO?.ToString() ?? "";
                    txtBPFI.Text = bearing.BPFI?.ToString() ?? "";
                    txtFTF.Text = bearing.FTF?.ToString() ?? "";
                    txtBSF.Text = bearing.BSF?.ToString() ?? "";

                    txtStmBPFO.Text = bearing.BPFO?.ToString() ?? "";
                    txtStmBPFI.Text = bearing.BPFI?.ToString() ?? "";
                    txtStmFTF.Text = bearing.FTF?.ToString() ?? "";
                    txtStmBSF.Text = bearing.BSF?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ComboBox SelectedIndexChanged error: {ex.Message}", ex);
            }
        }

        private void BtnEnableBearing_Click(object sender, EventArgs e)
        {
            bool isReadOnly = txtID.ReadOnly;
            cmbBearingSelect.Enabled = !cmbBearingSelect.Enabled;
            txtID.ReadOnly = !isReadOnly;
            txtBea.ReadOnly = !isReadOnly;
            txtNB.ReadOnly = !isReadOnly;
            txtBD.ReadOnly = !isReadOnly;
            txtPD.ReadOnly = !isReadOnly;
            txtPHI.ReadOnly = !isReadOnly;
            txtBPFO.ReadOnly = !isReadOnly;
            txtBPFI.ReadOnly = !isReadOnly;
            txtFTF.ReadOnly = !isReadOnly;
            txtBSF.ReadOnly = !isReadOnly;

            btnEnableBearing.Text = isReadOnly ? "Lock Fields" : "Enable Edit";
            btnEnableBearing.BackColor = isReadOnly ? Color.DarkGreen : Color.IndianRed;
        }

        private async void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (_comm != null && _comm.IsConnected)
                {
                    if (e.CloseReason == CloseReason.UserClosing)
                    {
                        DialogResult result = MessageBox.Show(
                            "Disconnect from STM32 and close?",
                            "Confirm",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        if (result == DialogResult.No)
                        {
                            e.Cancel = true;
                            return;
                        }
                    }

                    _comm.StopTelemetryMonitor();
                    await _comm.Disconnect();
                }

                _formClosing = true;

                _xyChart1?.Stop();
                _xyChart2?.Stop();
                _xyChart3?.Stop();
                _xyChart1?.Dispose();
                _xyChart2?.Dispose();
                _xyChart3?.Dispose();

                _dbService?.Dispose();
                _comm?.Dispose();
                Logger.Info("Form1 closing — cleanup completed");
            }
            catch (Exception ex)
            {
                Logger.Error($"Form1_FormClosing error: {ex.Message}", ex);
            }
        }

        /// <summary>Compact tile for one of 12 main.c BL_* features on the Report tab.</summary>
        private sealed class ReportTechniqueTile : Panel
        {
            private static readonly Color BearingTeal = Color.FromArgb(22, 160, 133);

            public int FeatureIndex { get; set; }
            public int BlIndex { get; set; }
            public int DetectionPath { get; set; }
            public string StreamKey { get; set; } = "";
            public string MethodName { get; set; } = "";
            public string Subtitle { get; set; } = "";
            public float IndexValue { get; set; } = float.NaN;
            public int Severity { get; set; } = -1;
            public bool HasHarmonicsPlot { get; set; }
            public bool IsBearingPf { get; set; }
            public float WarnThreshold { get; set; } = 2f;
            public float FaultThreshold { get; set; } = 4.5f;

            public ReportTechniqueTile()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle r = ClientRectangle;
                r.Inflate(-1, -1);
                Color accent = StatorGaugePainter.SeverityColor(Severity);
                Color border, bg;
                switch (Severity)
                {
                    case 0: border = Color.FromArgb(120, 200, 160); bg = Color.FromArgb(246, 252, 248); break;
                    case 1: border = Color.FromArgb(241, 196, 15); bg = Color.FromArgb(255, 251, 235); break;
                    case 2: border = Color.FromArgb(231, 76, 60); bg = Color.FromArgb(254, 242, 240); break;
                    default: border = Color.FromArgb(205, 211, 219); bg = Color.FromArgb(249, 250, 252); break;
                }

                using (GraphicsPath path = RoundedRectPath(r, 6))
                using (var br = new SolidBrush(bg))
                using (var pen = new Pen(border, 1.2f))
                {
                    g.FillPath(br, path);
                    g.DrawPath(pen, path);
                }

                // Left accent stripe — teal flags the bearing partial-fault group.
                Color stripe = IsBearingPf ? BearingTeal : accent;
                using (var sbr = new SolidBrush(stripe))
                    g.FillRectangle(sbr, new Rectangle(r.Left + 3, r.Top + 6, 4, r.Height - 12));

                string slot = IsBearingPf
                    ? "BL" + BlIndex.ToString(CultureInfo.InvariantCulture)
                    : (BlIndex >= 0
                        ? "BL" + BlIndex.ToString(CultureInfo.InvariantCulture)
                        : "P" + DetectionPath.ToString(CultureInfo.InvariantCulture));
                string head = slot + " · " + (StreamKey ?? "") + (IsBearingPf ? " · BRG" : "");
                string value = float.IsNaN(IndexValue) ? "—" : IndexValue.ToString("F2", CultureInfo.InvariantCulture);
                string status = Severity < 0 ? "…" : (Severity == 0 ? "OK" : (Severity == 1 ? "WARN" : "FAULT"));

                using (var small = new Font("Segoe UI", 6.75F, FontStyle.Bold))
                using (var nameFont = new Font("Segoe UI", 8.75F, FontStyle.Bold))
                using (var valFont = new Font("Segoe UI", 11.5F, FontStyle.Bold))
                using (var pillFont = new Font("Segoe UI", 6.75F, FontStyle.Bold))
                using (var muted = new SolidBrush(Color.FromArgb(110, 120, 134)))
                using (var headBr = new SolidBrush(IsBearingPf ? BearingTeal : Color.FromArgb(110, 120, 134)))
                using (var accentBr = new SolidBrush(accent))
                using (var textBr = new SolidBrush(Color.FromArgb(45, 55, 65)))
                using (var whiteBr = new SolidBrush(Color.White))
                {
                    int tx = r.Left + 12;   // text inset clears the accent stripe

                    g.DrawString(head, small, headBr, tx, r.Top + 3);

                    // Status pill (top-right).
                    SizeF psz = g.MeasureString(status, pillFont);
                    Rectangle pill = new Rectangle(r.Right - (int)psz.Width - 14, r.Top + 3, (int)psz.Width + 10, 14);
                    using (GraphicsPath pp = RoundedRectPath(pill, 6))
                        g.FillPath(accentBr, pp);
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(status, pillFont, whiteBr, pill, sf);

                    // Method name (left) and the index value (right) share one row so
                    // the content stays compact and never overlaps the gauge below.
                    int midY = r.Top + 17;
                    using (var sfFar = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near })
                        g.DrawString(value, valFont, accentBr,
                            new RectangleF(r.Left, midY - 1, r.Width - 11, 20), sfFar);

                    SizeF vsz = g.MeasureString(value, valFont);
                    using (var nameFmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                        g.DrawString(MethodName ?? "", nameFont, textBr,
                            new RectangleF(tx, midY + 1, r.Width - 12 - (int)vsz.Width - 14, 14), nameFmt);

                    // Subtitle only when the tile is tall enough to fit it without collision.
                    if (!string.IsNullOrEmpty(Subtitle) && r.Height >= 62)
                        g.DrawString(Subtitle, small, muted, tx, midY + 16);
                }

                // Severity gauge: value vs WARN / FAULT thresholds (always pinned to bottom).
                Rectangle bar = new Rectangle(r.Left + 12, r.Bottom - 10, r.Width - 22, 6);
                float warn = WarnThreshold > 0 ? WarnThreshold : 2f;
                float fault = FaultThreshold > warn ? FaultThreshold : warn * 2f;
                StatorGaugePainter.Draw(g, bar, IndexValue, warn * 0.55f, warn, fault);
            }

            private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
            {
                int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
                if (d < 2) d = 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>Status card on Report tab (title, OK/WARN/FAULT, detail line).</summary>
        // ─────────────────────────────────────────────────────────────
        // Stator diagnostic report — professional engineering view
        // ─────────────────────────────────────────────────────────────

        /// <summary>Shared painter for a value-vs-threshold gauge (EARLY / WARN / ALARM zones).</summary>
        private static class StatorGaugePainter
        {
            public static Color SeverityColor(int sev)
            {
                switch (sev)
                {
                    case 0: return Color.FromArgb(39, 174, 96);
                    case 1: return Color.FromArgb(243, 156, 18);
                    case 2: return Color.FromArgb(231, 76, 60);
                    default: return Color.FromArgb(127, 140, 141);
                }
            }

            public static void Draw(Graphics g, Rectangle bar,
                float value, float early, float warn, float alarm)
            {
                float max = alarm * 1.25f;
                if (max <= 0f) max = 1f;

                using (var track = new SolidBrush(Color.FromArgb(236, 239, 243)))
                    g.FillRectangle(track, bar);

                FillZone(g, bar, 0f, early, max, Color.FromArgb(206, 236, 216));
                FillZone(g, bar, early, warn, max, Color.FromArgb(252, 238, 196));
                FillZone(g, bar, warn, alarm, max, Color.FromArgb(250, 216, 184));
                FillZone(g, bar, alarm, max, max, Color.FromArgb(245, 205, 200));

                DrawTick(g, bar, early, max);
                DrawTick(g, bar, warn, max);
                DrawTick(g, bar, alarm, max);

                if (!float.IsNaN(value))
                {
                    float v = value;
                    if (v < 0f) v = 0f;
                    if (v > max) v = max;
                    int mx = bar.Left + (int)(bar.Width * (v / max));
                    using (var pen = new Pen(Color.FromArgb(40, 50, 60), 2f))
                        g.DrawLine(pen, mx, bar.Top - 2, mx, bar.Bottom + 2);
                    using (var dot = new SolidBrush(Color.FromArgb(40, 50, 60)))
                        g.FillEllipse(dot, mx - 3, bar.Top - 5, 6, 6);
                }

                using (var pen = new Pen(Color.FromArgb(198, 205, 214), 1f))
                    g.DrawRectangle(pen, bar);
            }

            private static void FillZone(Graphics g, Rectangle bar, float from, float to, float max, Color c)
            {
                if (to <= from) return;
                int x0 = bar.Left + (int)(bar.Width * (from / max));
                int x1 = bar.Left + (int)(bar.Width * (to / max));
                if (x1 <= x0) return;
                using (var br = new SolidBrush(c))
                    g.FillRectangle(br, x0, bar.Top, x1 - x0, bar.Height);
            }

            private static void DrawTick(Graphics g, Rectangle bar, float at, float max)
            {
                int x = bar.Left + (int)(bar.Width * (at / max));
                using (var pen = new Pen(Color.FromArgb(150, 160, 172), 1f))
                    g.DrawLine(pen, x, bar.Top, x, bar.Bottom);
            }
        }

        /// <summary>Section banner for the stator diagnosis: title + overall verdict + Fortescue summary.</summary>
        private sealed class StatorSectionHeaderPanel : Panel
        {
            public int VerdictSeverity { get; set; } = -1;
            public string VerdictText { get; set; } = "NO DATA";
            public string SummaryText { get; set; } = "";
            public string FortescueText { get; set; } = "";

            public StatorSectionHeaderPanel()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle r = ClientRectangle;
                r.Inflate(-1, -1);
                using (GraphicsPath path = RoundedRectPath(r, 8))
                using (var br = new LinearGradientBrush(r,
                           Color.FromArgb(247, 244, 251), Color.FromArgb(238, 232, 246),
                           LinearGradientMode.Vertical))
                using (var pen = new Pen(Color.FromArgb(214, 205, 228), 1f))
                {
                    g.FillPath(br, path);
                    g.DrawPath(pen, path);
                }

                using (var bbr = new SolidBrush(Color.FromArgb(142, 68, 173)))
                    g.FillRectangle(bbr, new Rectangle(r.Left + 6, r.Top + 8, 5, r.Height - 16));

                using (var titleFont = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                using (var subFont = new Font("Segoe UI", 8F))
                using (var tbr = new SolidBrush(Color.FromArgb(90, 50, 130)))
                using (var sbr = new SolidBrush(Color.FromArgb(90, 100, 115)))
                {
                    g.DrawString("STATOR WINDING DIAGNOSIS", titleFont, tbr, r.Left + 18, r.Top + 4);
                    g.DrawString(string.IsNullOrEmpty(SummaryText)
                            ? "Fortescue symmetrical components · CALIBST baseline · EMA + CUSUM"
                            : SummaryText,
                        subFont, sbr, r.Left + 18, r.Top + 23);
                }

                int chipW = 96;
                int chipX = r.Right - 16 - chipW;
                Rectangle chip = new Rectangle(chipX, r.Top + 9, chipW, r.Height - 18);
                Color accent = StatorGaugePainter.SeverityColor(VerdictSeverity);
                using (GraphicsPath cp = RoundedRectPath(chip, 7))
                using (var cbr = new SolidBrush(accent))
                    g.FillPath(cbr, cp);
                using (var vFont = new Font("Segoe UI", 9.5F, FontStyle.Bold))
                using (var vbr = new SolidBrush(Color.White))
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                    g.DrawString(VerdictText ?? "—", vFont, vbr, chip, sf);

                if (!string.IsNullOrEmpty(FortescueText))
                {
                    using (var fFont = new Font("Consolas", 8.25F, FontStyle.Bold))
                    using (var fbr = new SolidBrush(Color.FromArgb(110, 80, 140)))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                        g.DrawString(FortescueText, fFont, fbr,
                            new Rectangle(r.Left + 18, r.Top + 2, chipX - r.Left - 28, r.Height - 4), sf);
                }
            }

            private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
            {
                int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
                if (d < 2) d = 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>Large stator fault card (short / ground) with a CUSUM gauge and recommended action.</summary>
        private sealed class StatorFaultCard : Panel
        {
            private const float CusumEarly = 2.5f;
            private const float CusumWarn = 4.0f;
            private const float CusumAlarm = 6.5f;

            public string CardTitle { get; set; } = "";
            public int Severity { get; set; } = -1;
            public string StatusText { get; set; } = "—";
            public bool IsEarly { get; set; }
            public string PrimaryLabel { get; set; } = "";
            public string PrimaryValue { get; set; } = "—";
            public string SecondaryText { get; set; } = "";
            public float Cusum { get; set; } = float.NaN;
            public string ActionText { get; set; } = "";

            public StatorFaultCard()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }

            public void SetNoData()
            {
                Severity = -1;
                StatusText = "—";
                IsEarly = false;
                PrimaryValue = "—";
                SecondaryText = "Connect USB for the [STATOR] line";
                Cusum = float.NaN;
                ActionText = "Awaiting telemetry…";
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle r = ClientRectangle;
                r.Inflate(-1, -1);
                Color accent = StatorGaugePainter.SeverityColor(Severity);
                Color bg1, bg2, borderC;
                switch (Severity)
                {
                    case 0: bg1 = Color.FromArgb(244, 252, 247); bg2 = Color.FromArgb(228, 246, 235); borderC = Color.FromArgb(39, 174, 96); break;
                    case 1: bg1 = Color.FromArgb(255, 251, 238); bg2 = Color.FromArgb(255, 244, 212); borderC = Color.FromArgb(241, 196, 15); break;
                    case 2: bg1 = Color.FromArgb(254, 240, 238); bg2 = Color.FromArgb(250, 220, 216); borderC = Color.FromArgb(231, 76, 60); break;
                    default: bg1 = Color.FromArgb(248, 249, 251); bg2 = Color.FromArgb(237, 240, 245); borderC = Color.FromArgb(189, 195, 199); break;
                }

                using (GraphicsPath path = RoundedRectPath(r, 10))
                using (var br = new LinearGradientBrush(r, bg1, bg2, LinearGradientMode.Vertical))
                using (var pen = new Pen(borderC, 1.4f))
                {
                    g.FillPath(br, path);
                    g.DrawPath(pen, path);
                }

                using (var bbr = new SolidBrush(accent))
                    g.FillRectangle(bbr, new Rectangle(r.Left + 8, r.Top + 12, 5, r.Height - 24));

                // Title + status pill.
                using (var titleFont = new Font("Segoe UI", 9.5F, FontStyle.Bold))
                using (var tbr = new SolidBrush(Color.FromArgb(55, 65, 78)))
                    g.DrawString(CardTitle ?? "", titleFont, tbr, r.Left + 20, r.Top + 8);

                string statusFull = (StatusText ?? "—") + (IsEarly ? " · EARLY" : "");
                using (var stFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(statusFull, stFont);
                    Rectangle pill = new Rectangle(r.Right - (int)sz.Width - 28, r.Top + 8, (int)sz.Width + 18, 22);
                    using (GraphicsPath pp = RoundedRectPath(pill, 7))
                    using (var pbr = new SolidBrush(accent))
                        g.FillPath(pbr, pp);
                    using (var pwbr = new SolidBrush(Color.White))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(statusFull, stFont, pwbr, pill, sf);
                }

                // Primary index value.
                using (var lblFont = new Font("Segoe UI", 7.5F, FontStyle.Bold))
                using (var valFont = new Font("Segoe UI", 17F, FontStyle.Bold))
                using (var subFont = new Font("Segoe UI", 8F))
                using (var lblBr = new SolidBrush(Color.FromArgb(110, 120, 134)))
                using (var valBr = new SolidBrush(accent))
                using (var subBr = new SolidBrush(Color.FromArgb(80, 90, 104)))
                {
                    g.DrawString(PrimaryLabel ?? "", lblFont, lblBr, r.Left + 20, r.Top + 34);
                    g.DrawString(PrimaryValue ?? "—", valFont, valBr, r.Left + 18, r.Top + 44);
                    g.DrawString(SecondaryText ?? "", subFont, subBr, r.Left + 110, r.Top + 50);
                }

                // CUSUM gauge (right half).
                int gaugeX = r.Left + 110;
                int gaugeRight = r.Right - 16;
                Rectangle bar = new Rectangle(gaugeX, r.Top + 38, Math.Max(60, gaugeRight - gaugeX), 8);
                StatorGaugePainter.Draw(g, bar, Cusum, CusumEarly, CusumWarn, CusumAlarm);
                using (var gFont = new Font("Segoe UI", 7F, FontStyle.Bold))
                using (var gBr = new SolidBrush(Color.FromArgb(90, 100, 115)))
                {
                    string cText = float.IsNaN(Cusum)
                        ? "CUSUM —"
                        : "CUSUM " + Cusum.ToString("F2", CultureInfo.InvariantCulture)
                          + "   (early 2.5 · warn 4.0 · alarm 6.5)";
                    g.DrawString(cText, gFont, gBr, gaugeX, r.Top + 24);
                }

                // Recommended action.
                using (var actFont = new Font("Segoe UI", 8F, FontStyle.Italic))
                using (var actBr = new SolidBrush(Severity >= 1 ? accent : Color.FromArgb(90, 100, 115)))
                    g.DrawString(ActionText ?? "", actFont, actBr,
                        new RectangleF(r.Left + 20, r.Bottom - 18, r.Width - 32, 16));
            }

            private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
            {
                int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
                if (d < 2) d = 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>Stator baseline metric tile with a value-vs-threshold gauge (SB# slot).</summary>
        private sealed class StatorMetricTile : Panel
        {
            public int SbIndex { get; set; }
            public string StreamKey { get; set; } = "";
            public string MethodName { get; set; } = "";
            public string Note { get; set; } = "";
            public float Early { get; set; }
            public float Warn { get; set; }
            public float Alarm { get; set; }
            public bool ValueIsPercent { get; set; }
            public float Value { get; set; } = float.NaN;
            public int Severity { get; set; } = -1;
            public string StatusText { get; set; } = "…";

            public StatorMetricTile()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }

            public void SetNoData()
            {
                Value = float.NaN;
                Severity = -1;
                StatusText = "…";
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle r = ClientRectangle;
                r.Inflate(-1, -1);
                Color accent = StatorGaugePainter.SeverityColor(Severity);
                Color bg, borderC;
                switch (Severity)
                {
                    case 0: bg = Color.FromArgb(246, 252, 248); borderC = Color.FromArgb(120, 200, 160); break;
                    case 1: bg = Color.FromArgb(255, 251, 238); borderC = Color.FromArgb(241, 196, 15); break;
                    case 2: bg = Color.FromArgb(254, 242, 240); borderC = Color.FromArgb(231, 76, 60); break;
                    default: bg = Color.FromArgb(249, 250, 252); borderC = Color.FromArgb(205, 211, 219); break;
                }

                using (GraphicsPath path = RoundedRectPath(r, 6))
                using (var br = new SolidBrush(bg))
                using (var pen = new Pen(borderC, 1.2f))
                {
                    g.FillPath(br, path);
                    g.DrawPath(pen, path);
                }

                string head = "SB" + SbIndex.ToString(CultureInfo.InvariantCulture) + " · " + (StreamKey ?? "");
                string valStr = float.IsNaN(Value)
                    ? "—"
                    : (ValueIsPercent
                        ? Value.ToString("F1", CultureInfo.InvariantCulture) + "%"
                        : Value.ToString("F4", CultureInfo.InvariantCulture));

                using (var headFont = new Font("Segoe UI", 6.75F, FontStyle.Bold))
                using (var nameFont = new Font("Segoe UI", 8.25F, FontStyle.Bold))
                using (var valFont = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                using (var stFont = new Font("Segoe UI", 6.75F, FontStyle.Bold))
                using (var headBr = new SolidBrush(Color.FromArgb(120, 130, 144)))
                using (var nameBr = new SolidBrush(Color.FromArgb(50, 60, 72)))
                using (var valBr = new SolidBrush(accent))
                using (var sfRight = new StringFormat { Alignment = StringAlignment.Far })
                {
                    g.DrawString(head, headFont, headBr, r.Left + 7, r.Top + 4);
                    g.DrawString(StatusText ?? "", stFont, valBr,
                        new RectangleF(r.Left, r.Top + 4, r.Width - 7, 12), sfRight);
                    g.DrawString(MethodName ?? "", nameFont, nameBr, r.Left + 7, r.Top + 16);
                    g.DrawString(valStr, valFont, valBr,
                        new RectangleF(r.Left, r.Top + 14, r.Width - 7, 18), sfRight);
                }

                Rectangle bar = new Rectangle(r.Left + 7, r.Bottom - 12, r.Width - 14, 7);
                StatorGaugePainter.Draw(g, bar, Value, Early, Warn, Alarm);
            }

            private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
            {
                int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
                if (d < 2) d = 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>
        /// Professional engineering report header band: branded title block plus a
        /// metadata grid (Report ID, Date/Time, Device, Firmware, Motor tag, Operator)
        /// and a color-coded verdict chip. Fully owner-drawn for a document-grade look.
        /// </summary>
        private sealed class ReportHeaderPanel : Panel
        {
            public string ReportId { get; set; } = "RPT-—";
            public string GeneratedText { get; set; } = "—";
            public string DeviceText { get; set; } = "—";
            public string FirmwareText { get; set; } = "STM32H750 · DSP MCSA";
            public string MotorTagText { get; set; } = "—";
            public string OperatorText { get; set; } = "—";
            public string LinkText { get; set; } = "OFFLINE";
            public bool LinkOnline { get; set; } = false;
            public string VerdictText { get; set; } = "AWAITING DATA";
            /// <summary>-1 neutral, 0 healthy, 1 warning, 2 fault.</summary>
            public int VerdictSeverity { get; set; } = -1;

            public ReportHeaderPanel()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }

            private static Color SeverityColor(int sev)
            {
                switch (sev)
                {
                    case 0: return Color.FromArgb(39, 174, 96);
                    case 1: return Color.FromArgb(243, 156, 18);
                    case 2: return Color.FromArgb(231, 76, 60);
                    default: return Color.FromArgb(120, 144, 170);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle r = ClientRectangle;
                r.Inflate(-1, -1);
                Color accent = SeverityColor(VerdictSeverity);

                using (GraphicsPath path = RoundedRectPath(r, 12))
                using (var br = new LinearGradientBrush(r,
                           Color.FromArgb(31, 44, 66), Color.FromArgb(17, 27, 45),
                           LinearGradientMode.Horizontal))
                using (var pen = new Pen(Color.FromArgb(52, 70, 96), 1.2f))
                {
                    g.FillPath(br, path);
                    g.DrawPath(pen, path);
                }

                // Left accent bar (verdict color).
                Rectangle bar = new Rectangle(r.Left + 7, r.Top + 12, 5, r.Height - 24);
                using (GraphicsPath bp = RoundedRectPath(bar, 3))
                using (var bbr = new SolidBrush(accent))
                    g.FillPath(bbr, bp);

                // Logo tile.
                Rectangle logo = new Rectangle(r.Left + 20, r.Top + 20, 56, 56);
                using (GraphicsPath lp = RoundedRectPath(logo, 10))
                using (var lbr = new LinearGradientBrush(logo,
                           Color.FromArgb(41, 128, 185), Color.FromArgb(31, 97, 141),
                           LinearGradientMode.ForwardDiagonal))
                    g.FillPath(lbr, lp);
                using (var logoFont = new Font("Segoe UI", 15F, FontStyle.Bold))
                using (var lf = new SolidBrush(Color.White))
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                    g.DrawString("EES", logoFont, lf, logo, sf);

                // Title + subtitle.
                int tx = logo.Right + 16;
                using (var titleFont = new Font("Segoe UI", 16F, FontStyle.Bold))
                using (var subFont = new Font("Segoe UI", 9F))
                using (var subItalic = new Font("Segoe UI", 8F, FontStyle.Italic))
                using (var tbr = new SolidBrush(Color.White))
                using (var sbr = new SolidBrush(Color.FromArgb(168, 185, 206)))
                using (var s2br = new SolidBrush(Color.FromArgb(128, 146, 170)))
                {
                    g.DrawString("MOTOR DIAGNOSTIC REPORT", titleFont, tbr, tx, r.Top + 14);
                    g.DrawString("Motor Current Signature Analysis (MCSA) · Predictive Maintenance",
                        subFont, sbr, tx + 1, r.Top + 46);
                    g.DrawString("EES Motor Overload Analyzer", subItalic, s2br, tx + 1, r.Top + 66);
                }

                int verdictW = 158;
                int vx = r.Right - 16 - verdictW;
                int vy = r.Top + 16;

                // Verdict chip.
                Rectangle chip = new Rectangle(vx, vy, verdictW, 34);
                using (GraphicsPath cp = RoundedRectPath(chip, 8))
                using (var cbr = new SolidBrush(accent))
                    g.FillPath(cbr, cp);
                using (var vFont = new Font("Segoe UI", 11.5F, FontStyle.Bold))
                using (var vlblFont = new Font("Segoe UI", 6.5F, FontStyle.Bold))
                using (var vbr = new SolidBrush(Color.White))
                using (var vlblBr = new SolidBrush(Color.FromArgb(235, 245, 255)))
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.DrawString("VERDICT", vlblFont, vlblBr,
                        new Rectangle(chip.Left, chip.Top + 2, chip.Width, 12), sf);
                    g.DrawString(VerdictText ?? "—", vFont, vbr,
                        new Rectangle(chip.Left, chip.Top + 10, chip.Width, chip.Height - 10), sf);
                }

                // Link status pill under verdict chip.
                Rectangle link = new Rectangle(vx, vy + 40, verdictW, 22);
                Color linkColor = LinkOnline ? Color.FromArgb(39, 174, 96) : Color.FromArgb(90, 104, 124);
                using (GraphicsPath kp = RoundedRectPath(link, 7))
                using (var kbr = new SolidBrush(Color.FromArgb(LinkOnline ? 40 : 30, linkColor)))
                using (var kpen = new Pen(linkColor, 1.2f))
                {
                    g.FillPath(kbr, kp);
                    g.DrawPath(kpen, kp);
                }
                using (var kFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                using (var kbr2 = new SolidBrush(LinkOnline ? Color.FromArgb(120, 230, 170) : Color.FromArgb(180, 195, 214)))
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                    g.DrawString(LinkText ?? "OFFLINE", kFont, kbr2, link, sf);

                // Metadata grid (2 columns × 3 rows) between subtitle block and verdict chip.
                int gridRight = vx - 18;
                int gridLeft = Math.Max(tx, gridRight - 326);
                int colW = (gridRight - gridLeft) / 2;
                if (colW < 120) colW = 120;
                int[] rowY = { r.Top + 14, r.Top + 41, r.Top + 68 };

                DrawMeta(g, "REPORT ID", ReportId, gridLeft, rowY[0]);
                DrawMeta(g, "DEVICE", DeviceText, gridLeft, rowY[1]);
                DrawMeta(g, "MOTOR TAG", MotorTagText, gridLeft, rowY[2]);
                DrawMeta(g, "GENERATED", GeneratedText, gridLeft + colW, rowY[0]);
                DrawMeta(g, "FIRMWARE", FirmwareText, gridLeft + colW, rowY[1]);
                DrawMeta(g, "OPERATOR", OperatorText, gridLeft + colW, rowY[2]);
            }

            private static void DrawMeta(Graphics g, string label, string value, int x, int y)
            {
                using (var lFont = new Font("Segoe UI", 6.75F, FontStyle.Bold))
                using (var vFont = new Font("Segoe UI", 9F, FontStyle.Bold))
                using (var lbr = new SolidBrush(Color.FromArgb(120, 140, 166)))
                using (var vbr = new SolidBrush(Color.FromArgb(225, 233, 244)))
                {
                    g.DrawString(label, lFont, lbr, x, y);
                    g.DrawString(value ?? "—", vFont, vbr, x, y + 10);
                }
            }

            private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
            {
                int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
                if (d < 2) d = 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        private sealed class ReportMetricCard : Panel
        {
            public string CardTitle { get; set; } = "";
            public string StatusText { get; set; } = "WAIT";
            public string DetailText { get; set; } = "";
            public int Severity { get; set; } = -1;

            public ReportMetricCard()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle r = ClientRectangle;
                r.Inflate(-1, -1);
                Color c1, c2, border, accent;
                switch (Severity)
                {
                    case 0:
                        c1 = Color.FromArgb(242, 252, 246);
                        c2 = Color.FromArgb(220, 245, 232);
                        border = Color.FromArgb(39, 174, 96);
                        accent = Color.FromArgb(39, 174, 96);
                        break;
                    case 1:
                        c1 = Color.FromArgb(255, 251, 235);
                        c2 = Color.FromArgb(255, 243, 205);
                        border = Color.FromArgb(241, 196, 15);
                        accent = Color.FromArgb(243, 156, 18);
                        break;
                    case 2:
                        c1 = Color.FromArgb(254, 237, 236);
                        c2 = Color.FromArgb(250, 215, 212);
                        border = Color.FromArgb(231, 76, 60);
                        accent = Color.FromArgb(192, 57, 43);
                        break;
                    default:
                        c1 = Color.FromArgb(248, 249, 251);
                        c2 = Color.FromArgb(236, 240, 245);
                        border = Color.FromArgb(189, 195, 199);
                        accent = Color.FromArgb(149, 165, 166);
                        break;
                }

                using (GraphicsPath path = RoundedRectPath(r, 10))
                using (var br = new LinearGradientBrush(r, c1, c2, LinearGradientMode.Vertical))
                using (var pen = new Pen(border, 1.5f))
                {
                    g.FillPath(br, path);
                    g.DrawPath(pen, path);
                }

                Rectangle bar = new Rectangle(r.Left + 8, r.Top + 12, 5, r.Height - 24);
                using (GraphicsPath pathBar = RoundedRectPath(bar, 3))
                using (var br = new SolidBrush(accent))
                    g.FillPath(br, pathBar);

                using (var titleFont = new Font("Segoe UI", 8F, FontStyle.Bold))
                using (var statusFont = new Font("Segoe UI", 14F, FontStyle.Bold))
                using (var detailFont = new Font("Segoe UI", 7.5F))
                using (var titleBr = new SolidBrush(Color.FromArgb(90, 100, 115)))
                using (var statusBr = new SolidBrush(accent))
                using (var detailBr = new SolidBrush(Color.FromArgb(70, 78, 90)))
                {
                    g.DrawString(CardTitle ?? "", titleFont, titleBr, r.Left + 18, r.Top + 8);
                    g.DrawString(StatusText ?? "", statusFont, statusBr, r.Left + 18, r.Top + 26);
                    g.DrawString(DetailText ?? "", detailFont, detailBr, r.Left + 18, r.Top + 52);
                }
            }

            private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
            {
                int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
                if (d < 2) d = 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>Rounded gradient card for alarm summary (neutral / green / yellow / red).</summary>
        private sealed class AlarmStatusCardPanel : Panel
        {
            /// <summary>-1 = neutral, 0 = normal, 1 = warning, 2 = alarm</summary>
            public int Severity { get; set; } = -1;

            public AlarmStatusCardPanel()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = ClientRectangle;
                r.Inflate(-1, -1);
                const int rad = 14;
                Color c1, c2, border, accent;
                switch (Severity)
                {
                    case 0:
                        c1 = Color.FromArgb(242, 252, 246);
                        c2 = Color.FromArgb(214, 245, 228);
                        border = Color.FromArgb(39, 174, 96);
                        accent = Color.FromArgb(39, 174, 96);
                        break;
                    case 1:
                        c1 = Color.FromArgb(255, 251, 235);
                        c2 = Color.FromArgb(255, 243, 205);
                        border = Color.FromArgb(230, 185, 60);
                        accent = Color.FromArgb(241, 196, 15);
                        break;
                    case 2:
                        c1 = Color.FromArgb(254, 237, 236);
                        c2 = Color.FromArgb(250, 215, 212);
                        border = Color.FromArgb(231, 76, 60);
                        accent = Color.FromArgb(192, 57, 43);
                        break;
                    default:
                        c1 = Color.FromArgb(248, 249, 250);
                        c2 = Color.FromArgb(236, 240, 241);
                        border = Color.FromArgb(189, 195, 199);
                        accent = Color.FromArgb(149, 165, 166);
                        break;
                }

                using (GraphicsPath path = RoundedRectPath(r, rad))
                using (var br = new LinearGradientBrush(r, c1, c2, LinearGradientMode.Vertical))
                {
                    g.FillPath(br, path);
                    using (var pen = new Pen(border, 1.5f))
                        g.DrawPath(pen, path);
                }

                Rectangle bar = new Rectangle(r.Left + 10, r.Top + 14, 8, r.Height - 28);
                using (GraphicsPath pathBar = RoundedRectPath(bar, 4))
                using (var br = new SolidBrush(accent))
                    g.FillPath(br, pathBar);
            }

            private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
            {
                int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
                if (d < 2) d = 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>Circular severity badge (N / W / A) on the alarm summary card.</summary>
        private sealed class CircularBadgePanel : Panel
        {
            public Color BadgeBackColor { get; set; } = Color.FromArgb(46, 204, 113);

            public CircularBadgePanel()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = ClientRectangle;
                r.Inflate(-4, -4);
                using (var b = new SolidBrush(BadgeBackColor))
                using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), 1f))
                {
                    g.FillEllipse(b, r);
                    g.DrawEllipse(p, r);
                }
            }
        }

        /// <summary>Compact HMI status chip for Harmonics bearing / technique state.</summary>
        private sealed class HarmonicsStatusChip : Panel
        {
            public enum ChipKind { Bearing, Technique }

            private byte _level = 3;

            public string TagCode { get; set; } = "";
            public ChipKind Mode { get; set; } = ChipKind.Bearing;

            /// <summary>0 = OK/READY, 1 = WARN, 2 = FAULT, 3 = OFF/WAIT</summary>
            public byte Level
            {
                get { return _level; }
                set
                {
                    if (_level == value) return;
                    _level = value;
                    Invalidate();
                }
            }

            public HarmonicsStatusChip()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle card = ClientRectangle;
                card.Inflate(-1, -1);
                const int accentW = 5;
                Color accent, statusFore, cardFill = Color.White, cardBorder = Color.FromArgb(200, 206, 214);
                string statusText;

                if (Mode == ChipKind.Technique)
                {
                    switch (Level)
                    {
                        case 0:
                            accent = Color.FromArgb(41, 128, 185);
                            statusText = "READY";
                            statusFore = Color.FromArgb(41, 128, 185);
                            break;
                        default:
                            accent = Color.FromArgb(170, 178, 188);
                            statusText = "WAIT";
                            statusFore = Color.FromArgb(120, 128, 140);
                            break;
                    }
                }
                else
                {
                    switch (Level)
                    {
                        case 0:
                            accent = Color.FromArgb(39, 174, 96);
                            statusText = "OK";
                            statusFore = Color.FromArgb(30, 130, 75);
                            break;
                        case 1:
                            accent = Color.FromArgb(243, 156, 18);
                            statusText = "WARN";
                            statusFore = Color.FromArgb(180, 120, 15);
                            break;
                        case 2:
                            accent = Color.FromArgb(231, 76, 60);
                            statusText = "FAULT";
                            statusFore = Color.FromArgb(180, 50, 40);
                            break;
                        default:
                            accent = Color.FromArgb(170, 178, 188);
                            statusText = "OFF";
                            statusFore = Color.FromArgb(120, 128, 140);
                            break;
                    }
                }

                using (var path = RoundedRect(card, 6))
                using (var fill = new SolidBrush(cardFill))
                using (var border = new Pen(cardBorder))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }

                Rectangle accentRect = new Rectangle(card.Left, card.Top, accentW, card.Height);
                using (var pathAcc = RoundedRectLeft(accentRect, card, 6))
                using (var br = new SolidBrush(accent))
                    g.FillPath(br, pathAcc);

                var textRect = new Rectangle(card.Left + accentW + 6, card.Top + 4, card.Width - accentW - 10, card.Height - 8);
                using (var tagFont = new Font("Segoe UI", 8.25F, FontStyle.Bold))
                using (var statFont = new Font("Segoe UI", 7F, FontStyle.Bold))
                using (var tagBr = new SolidBrush(Color.FromArgb(45, 55, 72)))
                using (var statBr = new SolidBrush(statusFore))
                {
                    g.DrawString(TagCode ?? "", tagFont, tagBr, textRect.Left, textRect.Top);
                    g.DrawString(statusText, statFont, statBr, textRect.Left, textRect.Top + 16);
                }
            }

            private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
            {
                int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
                if (d < 2) d = 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }

            private static GraphicsPath RoundedRectLeft(Rectangle accent, Rectangle card, int radius)
            {
                var path = new GraphicsPath();
                path.AddRectangle(accent);
                return path;
            }
        }
    }
}