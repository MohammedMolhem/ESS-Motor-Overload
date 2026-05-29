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
        private Label lblReportTitle;
        private RichTextBox rtbTechniqueReport;

        // ── Harmonics Tab ──
        private GroupBox grpHarmonicControl;
        private GroupBox grpSmartEngineering;
        private Button btnChartClear, btnChartStop, btnChartContinue;
        private Button btnSmartViewRefresh, btnSmartViewExport;
        private RichTextBox rtbSmartEngineering;

        // Technique selection buttons
        private Button btnTechFourier, btnTechMusic, btnTechEsprit, btnTechCyclo;
        private Button btnTechSk, btnTechWavelet;

        // Report tab USB actions
        private Button btnReportFullReport, btnReportGraphData, btnReportPhaseCsv;

        // Settings — USB data export & baseline (main.c)
        private GroupBox grpBaseline;

        private MyChartClass _xyChart1;
        private MyChartClass _xyChart2;
        private MyChartClass _xyChart3;
        private Label lblFinalReportStatus;
        private Label lblPhase1, lblPhase2, lblPhase3;

        // Current technique selection and last received frame
        private enum SpectralTechnique { Fourier, Music, Esprit, Cyclostationary, Sk, Wavelet }
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

        private TextBox txtTerminalCmd;
        private Button btnTerminalSend;

        private GroupBox grpLive;
        private Label lblBpfoHz, lblBpfiHz, lblBsfHz, lblFtfHz;
        private Label lblFaultIndex, lblFaultLevel;
        private LedIndicatorPanel ledLiveBpfo, ledLiveBpfi, ledLiveBsf, ledLiveFtf;
        private LedIndicatorPanel[] _ledAlarmFrequencies;
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
        // Report Tab
        // ─────────────────────────────────────────────────────────────

        private void BuildAlarmMonitorTab()
        {
            tabAlarmMonitor.AutoScroll = true;
            tabAlarmMonitor.Padding = new Padding(12);

            lblReportTitle = new Label
            {
                Text = "Technique Report Page (Live from H750)",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185),
                AutoSize = false,
                Location = new Point(12, 12),
                Size = new Size(900, 32)
            };

            FlowLayoutPanel reportCmdPanel = new FlowLayoutPanel
            {
                Location = new Point(12, 48),
                Size = new Size(1030, 40),
                AutoSize = false,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            btnReportFullReport = MakeUsbActionButton("Full Report (REPORT)", Color.FromArgb(41, 128, 185));
            btnReportFullReport.Click += async (s, e) => await RunUsbFullReportAsync();
            btnReportGraphData = MakeUsbActionButton("Graph Data", Color.FromArgb(52, 73, 94));
            btnReportGraphData.Click += async (s, e) => await RunUsbGraphDataAsync();
            btnReportPhaseCsv = MakeUsbActionButton("Phase CSV", Color.FromArgb(52, 73, 94));
            btnReportPhaseCsv.Click += async (s, e) => await RunUsbPhaseCsvAsync();

            reportCmdPanel.Controls.Add(btnReportFullReport);
            reportCmdPanel.Controls.Add(btnReportGraphData);
            reportCmdPanel.Controls.Add(btnReportPhaseCsv);

            rtbTechniqueReport = new RichTextBox
            {
                Location = new Point(12, 92),
                Size = new Size(1030, 520),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Consolas", 9.5F),
                ReadOnly = true,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(35, 35, 35),
                WordWrap = false
            };

            tabAlarmMonitor.Controls.Add(lblReportTitle);
            tabAlarmMonitor.Controls.Add(reportCmdPanel);
            tabAlarmMonitor.Controls.Add(rtbTechniqueReport);
            tabAlarmMonitor.Resize += (s, e) =>
            {
                int w = Math.Max(400, tabAlarmMonitor.ClientSize.Width - 24);
                if (reportCmdPanel != null) reportCmdPanel.Width = w;
                if (rtbTechniqueReport != null)
                {
                    rtbTechniqueReport.Width = w;
                    rtbTechniqueReport.Height = Math.Max(200, tabAlarmMonitor.ClientSize.Height - 100);
                }
            };
            UpdateReportPageFromCurrentData();
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
            const int controlBarHeight = 118;
            const int finalReportStripH = 36;
            const int labelHeight = 22;
            const int chartPadding = 4;
            const int topMargin = 5;
            const int rightPanelMinWidth = 280;
            const int rightPanelPreferredWidth = 340;
            const int rightPanelGap = 8;

            Color[] phaseColors = { Color.Black,
                                    Color.FromArgb(41, 128, 185),
                                    Color.FromArgb(192, 57, 43) };

            lblFinalReportStatus = new Label
            {
                Text = "Final report (STM32): connect and use Full Report or a technique button. REPORT ends ### END_FULL_REPORT.",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 100, 100),
                AutoSize = false,
                Location = new Point(chartPadding, topMargin),
                Size = new Size(900, finalReportStripH)
            };

            _xyChart1 = new MyChartClass { PhaseName = "Three-Phase Harmonics Overlay" };
            _xyChart2 = new MyChartClass { PhaseName = "Phase 2" };
            _xyChart3 = new MyChartClass { PhaseName = "Phase 3" };

            lblPhase1 = new Label
            {
                Text = "Combined Engineering Plot: Phase 1 (Black), Phase 2 (Blue), Phase 3 (Red)",
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                ForeColor = phaseColors[0],
                AutoSize = true,
                Location = new Point(chartPadding, topMargin)
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

            tabHarmonics.Controls.Add(lblFinalReportStatus);
            tabHarmonics.Controls.Add(lblPhase1);
            tabHarmonics.Controls.Add(_xyChart1);
            tabHarmonics.Controls.Add(lblPhase2);
            tabHarmonics.Controls.Add(_xyChart2);
            tabHarmonics.Controls.Add(lblPhase3);
            tabHarmonics.Controls.Add(_xyChart3);

            MyChartClass[] charts = { _xyChart1, _xyChart2, _xyChart3 };
            Label[] labels = { lblPhase1, lblPhase2, lblPhase3 };

            Action layoutCharts = () =>
            {
                int tabW = tabHarmonics.ClientSize.Width;
                int tabH = tabHarmonics.ClientSize.Height;
                int chartsW = tabW - 2 * chartPadding;
                if (chartsW < 320) chartsW = 320;

                lblFinalReportStatus.Location = new Point(chartPadding, topMargin);
                lblFinalReportStatus.Width = chartsW;
                int availableHeight = tabH - controlBarHeight - topMargin - finalReportStripH - 4;
                int smartPanelHeight = Math.Max(120, Math.Min(180, availableHeight / 3));
                int chartHeight = availableHeight - labelHeight - chartPadding - chartPadding - smartPanelHeight;
                if (chartHeight < 40) chartHeight = 40;

                int y = topMargin + finalReportStripH + 4;
                labels[0].Location = new Point(chartPadding, y);
                y += labelHeight;
                charts[0].Location = new Point(chartPadding, y);
                charts[0].Size = new Size(chartsW, chartHeight);
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
                Text = "Spectral Display Control (H750 USB)",
                Size = new Size(820, 108),
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
                grpHarmonicControl.Size = new Size(controlW, 108);
            };
            tabHarmonics.Resize += (s, e) => layoutControlBar();
            layoutControlBar();

            btnChartClear = new Button
            {
                Text = "Clear",
                Location = new Point(10, 25),
                Size = new Size(80, 35),
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnChartClear.Click += (s, e) =>
            {
                _xyChart1?.ClearData();
                _xyChart2?.ClearData();
                _xyChart3?.ClearData();
            };

            btnChartStop = new Button
            {
                Text = "Stop",
                Location = new Point(95, 25),
                Size = new Size(80, 35),
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(192, 57, 43),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnChartStop.Click += (s, e) =>
            {
                _xyChart1?.Stop();
                _xyChart2?.Stop();
                _xyChart3?.Stop();
            };

            btnChartContinue = new Button
            {
                Text = "Continue",
                Location = new Point(180, 25),
                Size = new Size(90, 35),
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnChartContinue.Click += (s, e) =>
            {
                _xyChart1?.Start();
                _xyChart2?.Start();
                _xyChart3?.Start();
            };

            // ── Technique buttons — each requests a report then displays ──
            btnTechFourier = new Button
            {
                Text = "Fourier",
                Location = new Point(290, 25),
                Size = new Size(100, 35),
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(41, 128, 185),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnTechFourier.Click += async (s, e) => await SelectTechnique(SpectralTechnique.Fourier);

            btnTechMusic = new Button
            {
                Text = "MUSIC",
                Location = new Point(395, 25),
                Size = new Size(100, 35),
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnTechMusic.Click += async (s, e) => await SelectTechnique(SpectralTechnique.Music);

            btnTechEsprit = new Button
            {
                Text = "ESPRIT",
                Location = new Point(500, 25),
                Size = new Size(100, 35),
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnTechEsprit.Click += async (s, e) => await SelectTechnique(SpectralTechnique.Esprit);

            btnTechCyclo = new Button
            {
                Text = "Cyclo",
                Location = new Point(605, 25),
                Size = new Size(100, 35),
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnTechCyclo.Click += async (s, e) => await SelectTechnique(SpectralTechnique.Cyclostationary);

            btnTechSk = new Button
            {
                Text = "SK",
                Location = new Point(612, 25),
                Size = new Size(78, 35),
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnTechSk.Click += async (s, e) => await SelectTechnique(SpectralTechnique.Sk);

            btnTechWavelet = new Button
            {
                Text = "Wavelet",
                Location = new Point(695, 25),
                Size = new Size(78, 35),
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnTechWavelet.Click += async (s, e) => await SelectTechnique(SpectralTechnique.Wavelet);

            // All buttons on same row (y=25): Clear, Stop, Continue, then techniques
            btnTechFourier.Location = new Point(280, 25);
            btnTechFourier.Size = new Size(78, 35);
            btnTechMusic.Location = new Point(363, 25);
            btnTechMusic.Size = new Size(78, 35);
            btnTechEsprit.Location = new Point(446, 25);
            btnTechEsprit.Size = new Size(78, 35);
            btnTechCyclo.Location = new Point(529, 25);
            btnTechCyclo.Size = new Size(78, 35);

            grpHarmonicControl.Controls.AddRange(new Control[] {
                btnChartClear, btnChartStop, btnChartContinue,
                btnTechFourier, btnTechMusic, btnTechEsprit, btnTechCyclo,
                btnTechSk, btnTechWavelet });
            tabHarmonics.Controls.Add(grpHarmonicControl);

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
        }

        /// <summary>
        /// Requests a fresh spectral report from STM32 (if connected),
        /// selects the technique, and redraws charts.
        /// If already have data and STM32 is not connected, just switches view.
        /// </summary>
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
                    default: cmd = "FFTCSV"; break;
                }

                LogUI("Requesting " + cmd + "...", Color.Yellow);
                if (lblFinalReportStatus != null)
                {
                    lblFinalReportStatus.Text = "Requesting " + cmd + "…";
                    lblFinalReportStatus.ForeColor = Color.DarkGoldenrod;
                }

                List<string> lines = await _comm.RequestTechniqueCsv(cmd);

                if (lines != null && lines.Count > 0)
                {
                    LogUI(cmd + ": " + lines.Count + " lines received (parsed on stream)", Color.LimeGreen);
                }
                else
                {
                    LogUI(cmd + ": no data received", Color.Red);
                    if (lblFinalReportStatus != null)
                    {
                        lblFinalReportStatus.Text = cmd + ": no data (timeout or empty response)";
                        lblFinalReportStatus.ForeColor = Color.OrangeRed;
                    }
                }
            }

            RedrawChartsForCurrentTechnique();
            SetTechniqueButtonsEnabled(true);
        }

        private void SetTechniqueButtonsEnabled(bool enabled)
        {
            if (btnTechFourier != null) btnTechFourier.Enabled = enabled;
            if (btnTechMusic != null) btnTechMusic.Enabled = enabled;
            if (btnTechEsprit != null) btnTechEsprit.Enabled = enabled;
            if (btnTechCyclo != null) btnTechCyclo.Enabled = enabled;
            if (btnTechSk != null) btnTechSk.Enabled = enabled;
            if (btnTechWavelet != null) btnTechWavelet.Enabled = enabled;
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
                lblPhase1.Text = "Combined Engineering Plot — " + techName + " (no data)";
                LogUI(techName + ": no spectral data in last frame", Color.Orange);
                UpdateSmartEngineeringView();
                return;
            }

            List<PointF> dbP1 = ConvertPointsToDb(hasP1 ? phase1 : null);
            List<PointF> dbP2 = ConvertPointsToDb(hasP2 ? phase2 : null);
            List<PointF> dbP3 = ConvertPointsToDb(hasP3 ? phase3 : null);
            _xyChart1?.SetThreePhaseDbData(dbP1, dbP2, dbP3);
            if (dbP1 != null && dbP1.Count > 0)
                _xyChart1?.ShowPeakMarkers(dbP1, 8);
            else
                _xyChart1?.ShowPeakMarkers(new List<PointF>(), 0);

            lblPhase1.Text = "Combined Engineering Plot — " + techName;
            UpdateSmartEngineeringView();
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
                        frame.Cyclic2Points, phaseIndex, true);

                case SpectralTechnique.Sk:
                    return SelectPerPhaseOrFallback(
                        frame.SkPhase1Points, frame.SkPhase2Points, frame.SkPhase3Points,
                        frame.SkPoints, phaseIndex, false);

                case SpectralTechnique.Wavelet:
                    return SelectPerPhaseOrFallback(
                        frame.WaveletPhase1Points, frame.WaveletPhase2Points, frame.WaveletPhase3Points,
                        frame.WaveletPoints, phaseIndex, false);

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
        /// Displays ESPRIT frequency estimates as impulse lines on the three-phase overlay chart.
        /// Each estimated frequency is shown as a vertical spike from -100 dB to 0 dB.
        /// When per-phase ESPRIT data is available, each phase is drawn as a separate series.
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

            // Determine frequency range
            float maxFreq = 100f;
            List<List<float>> allFreqs = new List<List<float>>();
            if (p1Freqs != null) allFreqs.Add(p1Freqs);
            if (p2Freqs != null) allFreqs.Add(p2Freqs);
            if (p3Freqs != null) allFreqs.Add(p3Freqs);
            for (int j = 0; j < allFreqs.Count; j++)
                for (int i = 0; i < allFreqs[j].Count; i++)
                    if (allFreqs[j][i] > maxFreq) maxFreq = allFreqs[j][i];
            maxFreq *= 1.2f;

            if (_lastFrame.FourierPoints != null && _lastFrame.FourierPoints.Count > 0)
            {
                for (int i = 0; i < _lastFrame.FourierPoints.Count; i++)
                    if (_lastFrame.FourierPoints[i].X > maxFreq)
                        maxFreq = _lastFrame.FourierPoints[i].X;
            }
            if (maxFreq < 100f) maxFreq = 2500f;

            // Build impulse points for each phase
            List<PointF> dbP1 = ConvertPointsToDb(BuildEspritImpulsePoints(p1Freqs, maxFreq));
            List<PointF> dbP2 = ConvertPointsToDb(BuildEspritImpulsePoints(p2Freqs, maxFreq));
            List<PointF> dbP3 = ConvertPointsToDb(BuildEspritImpulsePoints(p3Freqs, maxFreq));

            _xyChart1?.SetThreePhaseDbData(dbP1, dbP2, dbP3);
            if (dbP1 != null && dbP1.Count > 0)
                _xyChart1?.ShowPeakMarkers(dbP1, 8);
            else
                _xyChart1?.ShowPeakMarkers(new List<PointF>(), 0);

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

            lblPhase1.Text = "Combined Engineering Plot — ESPRIT (" + sorted.Count + " peaks: " + freqList + ")";
            LogUI("ESPRIT: displaying " + sorted.Count + " frequency estimates: " + freqList, Color.Cyan);
            UpdateSmartEngineeringView();
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

        /// <summary>
        /// Displays ESPRIT frequency estimates as impulse lines on all 3 charts.
        /// Each estimated frequency is shown as a vertical spike from -100 dB to 0 dB.
        /// Uses the Fourier spectrum as a background reference so the impulses are
        /// visible on top of real spectral data.
        /// </summary>
        private void DisplayEspritOnCharts(List<float> espritFreqs)
        {
            if (espritFreqs == null || espritFreqs.Count == 0)
            {
                _xyChart1?.ClearData();
                lblPhase1.Text = "Combined Engineering Plot — ESPRIT (no peaks found)";
                LogUI("ESPRIT: no frequency estimates in last frame (exp_esprit_n=0)", Color.Orange);
                return;
            }

            // Sort frequencies for clean display
            List<float> sorted = new List<float>(espritFreqs);
            sorted.Sort();

            // Determine frequency range: use Fourier data range if available, else use ESPRIT range
            float maxFreq = sorted[sorted.Count - 1] * 1.2f;
            if (_lastFrame != null && _lastFrame.FourierPoints.Count > 0)
            {
                for (int i = 0; i < _lastFrame.FourierPoints.Count; i++)
                {
                    if (_lastFrame.FourierPoints[i].X > maxFreq)
                        maxFreq = _lastFrame.FourierPoints[i].X;
                }
            }
            if (maxFreq < 100f) maxFreq = 2500f;

            // Build display points: impulse spikes at each ESPRIT frequency.
            // Between spikes, draw a floor line at -100 dB so the chart is not empty.
            List<PointF> displayPoints = new List<PointF>();

            // Start at origin floor
            displayPoints.Add(new PointF(0f, 1e-12f));

            for (int i = 0; i < sorted.Count; i++)
            {
                float f = sorted[i];
                float fBefore = f - 0.5f;
                float fAfter = f + 0.5f;
                if (fBefore < 0f) fBefore = 0f;

                // Floor just before the spike
                displayPoints.Add(new PointF(fBefore, 1e-12f));
                // Spike up to full scale (will become 0 dB)
                displayPoints.Add(new PointF(f, 1.0f));
                // Floor just after the spike
                displayPoints.Add(new PointF(fAfter, 1e-12f));
            }

            // End at max frequency floor
            displayPoints.Add(new PointF(maxFreq, 1e-12f));

            UpdateChartFromPoints(_xyChart1, displayPoints, "ESPRIT Ph1");

            // Build frequency list string for labels
            string freqList = "";
            for (int i = 0; i < sorted.Count && i < 6; i++)
            {
                if (i > 0) freqList += ", ";
                freqList += sorted[i].ToString("F1") + " Hz";
            }
            if (sorted.Count > 6) freqList += " ...";

            lblPhase1.Text = "Combined Engineering Plot — ESPRIT (" + sorted.Count + " peaks: " + freqList + ")";

            LogUI("ESPRIT: displaying " + sorted.Count + " frequency estimates: " + freqList, Color.Cyan);
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
            if (lblFinalReportStatus != null)
            {
                lblFinalReportStatus.Text = label + ": receiving… (" + waitMarker + ")";
                lblFinalReportStatus.ForeColor = Color.DarkGoldenrod;
            }

            List<string> lines = await request();

            if (lines != null && lines.Count > 0)
                LogUI(label + ": " + lines.Count + " lines (parsed on stream)", Color.LimeGreen);
            else
            {
                LogUI(label + ": no data (timeout or firmware ERR)", Color.Red);
                if (lblFinalReportStatus != null)
                {
                    lblFinalReportStatus.Text = label + ": no data";
                    lblFinalReportStatus.ForeColor = Color.OrangeRed;
                }
            }

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

        private async Task RunUsbBaselineCommandAsync(string command)
        {
            if (!EnsureUsbConnected()) return;
            LogUI("USB " + command + "…", Color.Yellow);
            string resp = await _comm.SendBaselineTextCommand(command);
            if (!string.IsNullOrEmpty(resp))
                LogUI(command + " ← " + resp.Trim(), resp.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                    ? Color.LimeGreen : Color.OrangeRed);
            else
                LogUI(command + ": no response", Color.Red);
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
                    RedrawChartsForCurrentTechnique();

                    string fin = string.IsNullOrEmpty(frame.FinalReportSummary)
                        ? "(marker not set)"
                        : frame.FinalReportSummary;
                    string mode = string.IsNullOrEmpty(frame.Mode) ? "n/a" : frame.Mode;
                    if (lblFinalReportStatus != null)
                    {
                        lblFinalReportStatus.Text = "Final report: complete — " + fin +
                            " | mode=" + mode +
                            " | FFT=" + frame.FourierPoints.Count +
                            " (P1=" + frame.FourierPhase1Points.Count +
                            " P2=" + frame.FourierPhase2Points.Count +
                            " P3=" + frame.FourierPhase3Points.Count + ")" +
                            " MUSIC=" + frame.MusicPoints.Count +
                            " (P1=" + frame.MusicPhase1Points.Count +
                            " P2=" + frame.MusicPhase2Points.Count +
                            " P3=" + frame.MusicPhase3Points.Count + ")" +
                            " CY=" + frame.Cyclic2Points.Count +
                            " (P1=" + frame.Cyclic2Phase1Points.Count +
                            " P2=" + frame.Cyclic2Phase2Points.Count +
                            " P3=" + frame.Cyclic2Phase3Points.Count + ")" +
                            " ESPRIT=" + frame.EspritFrequencies.Count +
                            " (P1=" + frame.EspritPhase1Frequencies.Count +
                            " P2=" + frame.EspritPhase2Frequencies.Count +
                            " P3=" + frame.EspritPhase3Frequencies.Count + ")" +
                            " SK=" + frame.SkPoints.Count +
                            " (P1=" + frame.SkPhase1Points.Count +
                            " P2=" + frame.SkPhase2Points.Count +
                            " P3=" + frame.SkPhase3Points.Count + ")" +
                            " WAV=" + frame.WaveletPoints.Count +
                            " (P1=" + frame.WaveletPhase1Points.Count +
                            " P2=" + frame.WaveletPhase2Points.Count +
                            " P3=" + frame.WaveletPhase3Points.Count + ")" +
                            " — " + DateTime.Now.ToString("HH:mm:ss");
                        lblFinalReportStatus.ForeColor = Color.ForestGreen;
                    }

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

        /// <summary>
        /// Replaces all data in a chart with the given (freq, mag) points.
        /// Converts magnitude to dB scale for display.
        /// </summary>
        private void UpdateChartFromPoints(MyChartClass chart, List<PointF> points, string name)
        {
            if (chart == null || points == null || points.Count == 0) return;

            try
            {
                chart.ClearData();
                List<PointF> dbPoints = new List<PointF>(points.Count);

                float maxFreq = 0f;
                for (int i = 0; i < points.Count; i++)
                {
                    if (points[i].X > maxFreq) maxFreq = points[i].X;
                }

                if (maxFreq > 0f)
                    chart.SetXRange(0, maxFreq);

                for (int i = 0; i < points.Count; i++)
                {
                    float freq = points[i].X;
                    float mag = points[i].Y;

                    double mag_db;
                    if (mag > 1e-12f)
                        mag_db = 20.0 * Math.Log10(mag);
                    else
                        mag_db = -240.0;

                    if (mag_db < -100.0) mag_db = -100.0;
                    if (mag_db > 0.0) mag_db = 0.0;

                    chart.AddExternalData(freq, mag_db);
                    dbPoints.Add(new PointF(freq, (float)mag_db));
                }

                chart.ShowPeakMarkers(dbPoints, 8);
            }
            catch (Exception ex)
            {
                Logger.Error($"UpdateChartFromPoints ({name}): {ex.Message}", ex);
            }
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
            // ── Live Monitor ──
            grpMonitor = new GroupBox
            {
                Text = "Live Monitor",
                Location = new Point(330, y),
                Size = new Size(620, 93),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 0, 0)
            };
            tabSettings.Controls.Add(grpMonitor);

            SettingsMakeLabel("Temp (°C):", 10, 28, grpMonitor);
            txtSettingsTemperature = SettingsMakeTextBox(90, 26, 55, "--", grpMonitor);
            txtSettingsTemperature.ReadOnly = true;

            SettingsMakeLabel("Stator fault:", 155, 28, grpMonitor);
            barSettingsStatorShort = new MyBar
            {
                Location = new Point(250, 26),
                Size = new Size(140, 22),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            grpMonitor.Controls.Add(barSettingsStatorShort);

            txtSettingsStatorShort = SettingsMakeTextBox(395, 26, 45, "0%", grpMonitor);
            txtSettingsStatorShort.ReadOnly = true;

            SettingsMakeLabel("Ground level:", 155, 60, grpMonitor);
            barSettingsStatorGround = new MyBar
            {
                Location = new Point(250, 58),
                Size = new Size(140, 22),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            grpMonitor.Controls.Add(barSettingsStatorGround);

            txtSettingsStatorGround = SettingsMakeTextBox(395, 58, 45, "0%", grpMonitor);
            txtSettingsStatorGround.ReadOnly = true;

            _settingsMonitorLabels["SHORT"] = AddKeyValueMetric(grpMonitor, "Short:", 450, 500, 28, 50);
            _settingsMonitorLabels["GROUND"] = AddKeyValueMetric(grpMonitor, "Gnd:", 450, 490, 60, 50);
            _settingsMonitorLabels["NSR"] = AddKeyValueMetric(grpMonitor, "NSR:", 10, 45, 60, 50);
            _settingsMonitorLabels["ZSR"] = AddKeyValueMetric(grpMonitor, "ZSR:", 550, 585, 28, 30);
            _settingsMonitorLabels["TEMP"] = AddKeyValueMetric(grpMonitor, "Temp:", 550, 585, 60, 30);

            y += 100;

            grpBaseline = new GroupBox
            {
                Text = "Bearing / Stator Baseline (main.c)",
                Location = new Point(10, y),
                Size = new Size(940, 52),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            int bxBase = 12;
            string[] baseCmds = { "CALIB", "SAVEBASE", "LOADBASE", "CLEARBASE", "CALIBST", "SAVESTST", "LOADSTST", "CLEARSTST" };
            foreach (string cmd in baseCmds)
            {
                string captured = cmd;
                Button b = SettingsMakeActionButton(cmd, bxBase, 22, 88, Color.FromArgb(127, 140, 141));
                b.Click += async (s, e) => await RunUsbBaselineCommandAsync(captured);
                grpBaseline.Controls.Add(b);
                bxBase += 96;
            }
            tabSettings.Controls.Add(grpBaseline);
            y += 58;

            // ── Action Buttons ──
            int bx = 10;
            btnSendAll = SettingsMakeActionButton("Send All Parameters", bx, y, 150,
                Color.FromArgb(0, 223, 218));
            btnSendAll.Click += BtnSendAll_Click;
            bx += 159;

            btnReadAll = SettingsMakeActionButton("Read All Parameters", bx, y, 150,
                Color.FromArgb(0, 223, 218));
            btnReadAll.Click += BtnReadAll_Click;
            bx += 159;

            btnSaveAdc = SettingsMakeActionButton("Save ADC Snapshot", bx, y, 150,
                Color.FromArgb(0, 223, 218));
            btnSaveAdc.Click += BtnSaveAdc_Click;
            bx += 159;

            btnSaveFlash = SettingsMakeActionButton("Save to QSPI Flash", bx, y, 150,
                Color.FromArgb(0, 223, 218));
            btnSaveFlash.Click += BtnSaveFlash_Click;
            bx += 159;

            btnLoadFlash = SettingsMakeActionButton("Load from QSPI Flash", bx, y, 150,
                Color.FromArgb(0, 223, 218));
            btnLoadFlash.Click += BtnLoadFlash_Click;
            bx += 159;

            btnResetDefault = SettingsMakeActionButton("Reset to Default", bx, y, 150,
                Color.FromArgb(0, 223, 218));
            btnResetDefault.Click += BtnResetDefault_Click;


            y += 50;

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
                btnSaveAdc,
                txtTerminalCmd, btnTerminalSend,
                chkUseTextProtocol,
                btnReportFullReport, btnReportGraphData, btnReportPhaseCsv,
                btnTechSk, btnTechWavelet
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

        private Button SettingsMakeActionButton(string text, int x, int y, int w, Color bgColor)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 40),
                BackColor = bgColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Enabled = false
            };
            btn.FlatAppearance.BorderSize = 0;
            tabSettings.Controls.Add(btn);
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
                    c.Enabled = enabled;
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
                cmbPorts.SelectedIndex = ports.Length - 1;
            LogUI("Found " + ports.Length + " COM port(s)", Color.Cyan);
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

            string portName = cmbPorts.SelectedItem.ToString();
            bool ok = await _comm.Connect(portName);

            if (ok)
            {
                _comm.StartTelemetryMonitor();
            }
            else
            {
                MessageBox.Show(
                    "Failed to connect to STM32H750.\n\n" +
                    "Check:\n" +
                    "  - USB cable plugged in\n" +
                    "  - STM32 firmware flashed (main.c V6)\n" +
                    "  - Correct COM port selected\n" +
                    "  - USB clock = 48 MHz (PLL3Q)",
                    "Connection Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            btnConnect.Enabled = true;
        }

        private async void BtnPing_Click(object sender, EventArgs e)
        {
            btnPing.Enabled = false;
            bool ok;

            if (UseTextProtocol)
            {
                LogUI("PING (text)...", Color.Cyan);
                ok = await _comm.PingText();
            }
            else
            {
                LogUI("PING (binary)...", Color.Cyan);
                ok = await _comm.Ping();
            }

            LogUI(ok ? "PING OK" : "PING FAILED",
                  ok ? Color.LimeGreen : Color.Red);
            btnPing.Enabled = true;
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
                if (lblFinalReportStatus != null)
                {
                    lblFinalReportStatus.Text =
                        "Final report (STM32): waiting — REPORT/FULLREPORT ends ### END_FULL_REPORT; techniques ### END_EXPORT.";
                    lblFinalReportStatus.ForeColor = Color.FromArgb(100, 100, 100);
                }
                ResetTelemetryDisplays();
                UpdateAlarmMonitorDashboard(null);
                ApplyFrequencyLeds(3, true);

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
                if (lblFinalReportStatus != null)
                {
                    lblFinalReportStatus.Text =
                        "Final report (STM32): not connected — use Full Report or technique buttons when connected.";
                    lblFinalReportStatus.ForeColor = Color.FromArgb(100, 100, 100);
                }
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
            string proto = UseTextProtocol ? "text" : "binary";
            LogUI("Sending all parameters (" + proto + ")...", Color.Yellow);

            int ok;
            if (UseTextProtocol)
                ok = await _comm.SendAllParametersText(p);
            else
                ok = await _comm.SendAllParameters(p);

            int expected = 8;
            if (UseTextProtocol && p.SupplyLineHz > 1.0f && p.SupplyLineHz < 400.0f)
                expected = 9;

            string msg = "Sent " + ok + " parameter command(s) (" + proto + ")";
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
            btnReadAll.Enabled = false;

            LogUI("Reading parameters (text)...", Color.Cyan);

            MotorParameters p = await _comm.GetAllParametersText();

            if (p != null)
            {
                PopulateUIFromParameters(p);
                LogUI("Parameters received:\r\n" + p.ToString(), Color.LimeGreen);
            }
            else
            {
                LogUI("Failed to read parameters", Color.Red);
            }

            btnReadAll.Enabled = true;
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
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — ADC / Flash / Reset
        // ═════════════════════════════════════════════════════════════

        private async void BtnSaveAdc_Click(object sender, EventArgs e)
        {
            btnSaveAdc.Enabled = false;
            LogUI("Requesting ADC snapshot save...", Color.Yellow);

            bool ok;
            if (UseTextProtocol)
                ok = await _comm.SaveAdcSnapshotText();
            else
                ok = await _comm.SaveAdcSnapshot();

            LogUI(ok ? "ADC snapshot save requested — saves on next cycle"
                     : "ADC snapshot save request FAILED",
                  ok ? Color.LimeGreen : Color.Red);
            btnSaveAdc.Enabled = true;
        }

        private async void BtnSaveFlash_Click(object sender, EventArgs e)
        {
            btnSaveFlash.Enabled = false;
            LogUI("Saving to QSPI flash...", Color.Yellow);

            bool ok;
            if (UseTextProtocol)
                ok = await _comm.SaveToFlashText();
            else
                ok = await _comm.SaveToFlash();

            LogUI(ok ? "Saved to QSPI flash" : "QSPI save FAILED",
                  ok ? Color.LimeGreen : Color.Red);
            btnSaveFlash.Enabled = true;
        }

        private async void BtnLoadFlash_Click(object sender, EventArgs e)
        {
            btnLoadFlash.Enabled = false;
            LogUI("Loading from QSPI flash...", Color.Yellow);

            bool ok;
            if (UseTextProtocol)
                ok = await _comm.LoadFromFlashText();
            else
                ok = await _comm.LoadFromFlash();

            if (ok)
            {
                LogUI("Loaded from QSPI flash — reading back...", Color.LimeGreen);
                await Task.Delay(150);

                MotorParameters p = await _comm.GetAllParametersText();

                if (p != null)
                {
                    PopulateUIFromParameters(p);
                    LogUI("UI updated with loaded parameters", Color.Cyan);
                }
            }
            else
            {
                LogUI("QSPI load FAILED", Color.Red);
            }

            btnLoadFlash.Enabled = true;
        }

        private async void BtnResetDefault_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                "Reset all STM32 parameters to factory defaults?\n\n" +
                "This changes RAM only. Use 'Save to QSPI Flash' to make permanent.",
                "Confirm Reset",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            btnResetDefault.Enabled = false;
            LogUI("Resetting to defaults...", Color.Yellow);

            bool ok;
            if (UseTextProtocol)
                ok = await _comm.ResetToDefaultText();
            else
                ok = await _comm.ResetToDefault();

            if (ok)
            {
                LogUI("Reset OK — reading back...", Color.LimeGreen);
                await Task.Delay(150);

                MotorParameters p = await _comm.GetAllParametersText();

                if (p != null)
                {
                    PopulateUIFromParameters(p);
                    LogUI("UI updated with default parameters", Color.Cyan);
                }
            }
            else
            {
                LogUI("Reset FAILED", Color.Red);
            }

            btnResetDefault.Enabled = true;
        }

        // ═════════════════════════════════════════════════════════════
        // Settings Tab — Terminal
        // ═════════════════════════════════════════════════════════════

        private async void BtnTerminalSend_Click(object sender, EventArgs e)
        {
            string cmd = txtTerminalCmd.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            string low = cmd.ToUpperInvariant();
            if (low == "REPORT" || low == "FULLREPORT")
            {
                txtTerminalCmd.Text = "";
                await RunUsbFullReportAsync();
                return;
            }
            if (low == "GRAPHDATA" || low == "GRAPHS")
            {
                txtTerminalCmd.Text = "";
                await RunUsbGraphDataAsync();
                return;
            }
            if (low == "PHASECSV" || low == "PHASE3")
            {
                txtTerminalCmd.Text = "";
                await RunUsbPhaseCsvAsync();
                return;
            }
            if (low.EndsWith("CSV") && low.Length > 3)
            {
                txtTerminalCmd.Text = "";
                await RunUsbMultiLineAsync(cmd, () => _comm.RequestTechniqueCsv(cmd), "### END_EXPORT");
                return;
            }
            if (low == "CALIB" || low == "SAVEBASE" || low == "LOADBASE" || low == "CLEARBASE" ||
                low == "CALIBST" || low == "SAVESTST" || low == "LOADSTST" || low == "CLEARSTST")
            {
                txtTerminalCmd.Text = "";
                await RunUsbBaselineCommandAsync(cmd);
                return;
            }

            btnTerminalSend.Enabled = false;
            txtTerminalCmd.Enabled = false;
            LogUI("TX> " + cmd, Color.White);

            string resp = await _comm.SendRawTextCommand(cmd);

            if (resp != null)
                LogUI("RX< " + resp, Color.LimeGreen);
            else
                LogUI("RX< (no response / timeout)", Color.Orange);

            txtTerminalCmd.Text = "";
            txtTerminalCmd.Enabled = true;
            btnTerminalSend.Enabled = true;
            txtTerminalCmd.Focus();
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

            if (lblFaultLevel != null)
            {
                lblFaultLevel.Text = t.FaultLevelString;
                switch (t.FaultLevel)
                {
                    case 0: lblFaultLevel.ForeColor = Color.Green; break;
                    case 1: lblFaultLevel.ForeColor = Color.Orange; break;
                    case 2: lblFaultLevel.ForeColor = Color.Red; break;
                    default: lblFaultLevel.ForeColor = Color.Gray; break;
                }
            }

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
        /// Green / yellow / red LEDs beside each frequency readout.
        /// Level 3 = dim gray when disconnected or waiting for data.
        /// </summary>
        private void ApplyFrequencyLeds(byte faultLevel, bool connected)
        {
            byte lv;
            if (!connected) lv = 3;
            else if (faultLevel >= 3) lv = 3;
            else lv = faultLevel > 2 ? (byte)2 : faultLevel;

            if (ledLiveBpfo != null) ledLiveBpfo.Level = lv;
            if (ledLiveBpfi != null) ledLiveBpfi.Level = lv;
            if (ledLiveBsf != null) ledLiveBsf.Level = lv;
            if (ledLiveFtf != null) ledLiveFtf.Level = lv;
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
            SetMetricLabel(_settingsMonitorLabels, "TEMP", FormatTemperature(t));
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
            StringBuilder sb = new StringBuilder(8192);

            // ══════════════════════════════════════════════════════════════════════
            // HEADER
            // ══════════════════════════════════════════════════════════════════════
            sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║       EES MOTOR OVERLOAD — COMPREHENSIVE DIAGNOSTIC REPORT             ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════╝");
            sb.AppendLine();
            sb.AppendLine("  Report Generated : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("  Connection Status: " + (isConnected ? "● CONNECTED" : "○ DISCONNECTED"));
            if (isConnected && cmbPorts != null && cmbPorts.SelectedItem != null)
                sb.AppendLine("  COM Port         : " + cmbPorts.SelectedItem.ToString());
            sb.AppendLine();

            // ══════════════════════════════════════════════════════════════════════
            // SECTION 1: COMMUNICATION PROTOCOL INFORMATION
            // ══════════════════════════════════════════════════════════════════════
            sb.AppendLine("┌──────────────────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│  1. COMMUNICATION PROTOCOL                                              │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────────────────┤");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  Interface     : USB CDC Virtual COM Port (VCP)                          │");
            sb.AppendLine("│  MCU           : STM32H750 (Cortex-M7, 480 MHz)                         │");
            sb.AppendLine("│  USB Clock     : 48 MHz (PLL3Q)                                         │");
            sb.AppendLine("│  Protocol Modes: Text (ASCII line-based) / Binary (framed packets)      │");
            sb.AppendLine("│  Current Mode  : " + (UseTextProtocol ? "TEXT PROTOCOL" : "BINARY PROTOCOL").PadRight(55) + "│");
            sb.AppendLine("│  Baud Rate     : Native USB Full-Speed (12 Mbit/s)                      │");
            sb.AppendLine("│  Data Flow     : Half-duplex command/response + async telemetry          │");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  Text Commands : PING, GET, REPORT, FULLREPORT, GRAPHDATA, PHASECSV     │");
            sb.AppendLine("│                  FFTCSV, MUSICCSV, ESPRITCSV, CYCLIC2CSV, SKCSV,        │");
            sb.AppendLine("│                  WAVELETCSV, SAVE, LOAD, DEFAULT, SAVEADC               │");
            sb.AppendLine("│  Binary Cmds   : 0x04-0x0B (set params), 0x80 (get all), 0xFE (ping)   │");
            sb.AppendLine("│                  0x30-0x32 (flash ops), 0x40 (ADC snapshot)             │");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  End Markers   : ### END_FULL_REPORT  (REPORT/FULLREPORT)               │");
            sb.AppendLine("│                  ### END_EXPORT       (FFTCSV, MUSICCSV, etc.)          │");
            sb.AppendLine("│                  ### END_GRAPHDATA    (GRAPHDATA/GRAPHS)                 │");
            sb.AppendLine("│                  ### END_PHASE_CSV    (PHASE3/PHASECSV)                  │");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("└──────────────────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // ══════════════════════════════════════════════════════════════════════
            // SECTION 2: HARMONICS & SPECTRAL TECHNIQUES
            // ══════════════════════════════════════════════════════════════════════
            sb.AppendLine("┌──────────────────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│  2. HARMONICS & SPECTRAL ANALYSIS TECHNIQUES                            │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────────────────┤");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  ┌─────────────────┬───────────────────────────────────────────────┐    │");
            sb.AppendLine("│  │ Technique       │ Description                                   │    │");
            sb.AppendLine("│  ├─────────────────┼───────────────────────────────────────────────┤    │");
            sb.AppendLine("│  │ FFT (Fourier)   │ Fast Fourier Transform — frequency spectrum   │    │");
            sb.AppendLine("│  │                 │ decomposition for bearing fault detection.    │    │");
            sb.AppendLine("│  │                 │ 3-phase analysis (P1, P2, P3 + Mean).        │    │");
            sb.AppendLine("│  ├─────────────────┼───────────────────────────────────────────────┤    │");
            sb.AppendLine("│  │ MUSIC           │ MUltiple SIgnal Classification — sub-space    │    │");
            sb.AppendLine("│  │                 │ method for high-resolution frequency est.     │    │");
            sb.AppendLine("│  │                 │ Resolves closely spaced spectral peaks.       │    │");
            sb.AppendLine("│  ├─────────────────┼───────────────────────────────────────────────┤    │");
            sb.AppendLine("│  │ ESPRIT          │ Estimation of Signal Parameters via Rotational│    │");
            sb.AppendLine("│  │                 │ Invariance — parametric frequency estimation. │    │");
            sb.AppendLine("│  │                 │ Extracts dominant harmonic frequencies.       │    │");
            sb.AppendLine("│  ├─────────────────┼───────────────────────────────────────────────┤    │");
            sb.AppendLine("│  │ Cyclostationary  │ Second-order cyclostationary analysis —      │    │");
            sb.AppendLine("│  │                 │ detects periodic modulation patterns in       │    │");
            sb.AppendLine("│  │                 │ bearing defect signals. 3-phase support.      │    │");
            sb.AppendLine("│  ├─────────────────┼───────────────────────────────────────────────┤    │");
            sb.AppendLine("│  │ Spectral Kurtosis│ SK — identifies non-Gaussian transients     │    │");
            sb.AppendLine("│  │ (SK)            │ in frequency bands; optimal demodulation      │    │");
            sb.AppendLine("│  │                 │ band selection for envelope analysis.         │    │");
            sb.AppendLine("│  ├─────────────────┼───────────────────────────────────────────────┤    │");
            sb.AppendLine("│  │ Wavelet         │ Wavelet decomposition — time-frequency        │    │");
            sb.AppendLine("│  │                 │ analysis for transient fault detection.       │    │");
            sb.AppendLine("│  │                 │ Multi-resolution signal representation.       │    │");
            sb.AppendLine("│  └─────────────────┴───────────────────────────────────────────────┘    │");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  Current Technique: " + _currentTechnique.ToString().ToUpperInvariant().PadRight(53) + "│");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  Bearing Fault Frequencies:                                              │");
            sb.AppendLine("│    BPFO — Ball Pass Frequency Outer race (outer ring defect)             │");
            sb.AppendLine("│    BPFI — Ball Pass Frequency Inner race (inner ring defect)             │");
            sb.AppendLine("│    BSF  — Ball Spin Frequency (rolling element defect)                   │");
            sb.AppendLine("│    FTF  — Fundamental Train Frequency (cage defect)                      │");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("│  Stator Winding Analysis:                                                │");
            sb.AppendLine("│    NSR  — Negative Sequence Ratio (inter-turn short detection)           │");
            sb.AppendLine("│    ZSR  — Zero Sequence Ratio (ground fault detection)                   │");
            sb.AppendLine("│    HARM — Harmonic Distortion Ratio (winding degradation)                │");
            sb.AppendLine("│    IMB  — Current Imbalance (% deviation between phases)                 │");
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("└──────────────────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // ══════════════════════════════════════════════════════════════════════
            // SECTION 3: LIVE TELEMETRY DATA
            // ══════════════════════════════════════════════════════════════════════
            sb.AppendLine("┌──────────────────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│  3. LIVE TELEMETRY DATA                                                 │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────────────────┤");

            if (_lastTelemetryForReport != null)
            {
                TelemetryData t = _lastTelemetryForReport;
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  ── Bearing Health ──────────────────────────────────────────────────    │");
                sb.AppendLine("│  Fault Level  : " + t.FaultLevelString.PadRight(57) + "│");
                sb.AppendLine("│  Fault Index  : " + t.FaultIndex.ToString("F4").PadRight(57) + "│");
                sb.AppendLine("│  EMA          : " + t.FaultIndex_Ema.ToString("F4").PadRight(57) + "│");
                sb.AppendLine("│  CUSUM Score  : " + t.CusumScore.ToString("F2").PadRight(57) + "│");
                sb.AppendLine("│  Dominant     : " + DominantFaultToText(t.DominantFault).PadRight(57) + "│");
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  ── Bearing Frequencies (Hz) ───────────────────────────────────────    │");
                sb.AppendLine("│  BPFO = " + t.BPFO_Hz.ToString("F2").PadRight(12) +
                              "BPFI = " + t.BPFI_Hz.ToString("F2").PadRight(12) +
                              "BSF = " + t.BSF_Hz.ToString("F2").PadRight(12) +
                              "FTF = " + t.FTF_Hz.ToString("F2").PadRight(10) + "│");
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  ── Per-Technique Indices ──────────────────────────────────────────    │");
                sb.AppendLine("│  +──────────────┬──────────────┬──────────────┬──────────────────────+  │");
                sb.AppendLine("│  │ Technique    │ Index        │ Technique    │ Index                │  │");
                sb.AppendLine("│  +──────────────┼──────────────┼──────────────┼──────────────────────+  │");
                sb.AppendLine("│  │ FFT (LS)     │ " + t.Index_LS.ToString("F4").PadRight(13) +
                              "│ MUSIC (MI)   │ " + t.Index_Music.ToString("F4").PadRight(21) + "│  │");
                sb.AppendLine("│  │ ESPRIT (ES)  │ " + t.Index_Esprit.ToString("F4").PadRight(13) +
                              "│ Teager (TK)  │ " + t.Index_Teager.ToString("F4").PadRight(21) + "│  │");
                sb.AppendLine("│  │ SK           │ " + t.Index_SK.ToString("F4").PadRight(13) +
                              "│ Wavelet (WV) │ " + t.Index_Wavelet.ToString("F4").PadRight(21) + "│  │");
                sb.AppendLine("│  │ Cyclic (CY)  │ " + t.Index_Cyclic.ToString("F4").PadRight(13) +
                              "│ Sideband (SB)│ " + t.Index_Sideband.ToString("F4").PadRight(21) + "│  │");
                sb.AppendLine("│  │ EnvACF       │ " + t.Index_EnvAcf.ToString("F4").PadRight(13) +
                              "│ SK Peak      │ " + (t.SkPeak.ToString("F4") + " @ " + t.SkPeakHz.ToString("F1") + "Hz").PadRight(21) + "│  │");
                sb.AppendLine("│  +──────────────┴──────────────┴──────────────┴──────────────────────+  │");
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  ── Per-Fault Partial Indices ─────────────────────────────────────    │");
                sb.AppendLine("│  PF_Outer = " + t.Index_Bpfo.ToString("F4").PadRight(10) +
                              "PF_Inner = " + t.Index_Bpfi.ToString("F4").PadRight(10) +
                              "PF_Ball = " + t.Index_Bsf.ToString("F4").PadRight(10) +
                              "PF_Cage = " + t.Index_Ftf.ToString("F4").PadRight(6) + "│");
                sb.AppendLine("│  Kurtosis Band: " + t.KurtBandHz.ToString(CultureInfo.InvariantCulture).PadRight(57) + "│");
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  ── Stator Winding Health ─────────────────────────────────────────    │");
                sb.AppendLine("│  Short Level : " + t.Stator_ShortLevel.ToString().PadRight(10) +
                              "Ground Level: " + t.Stator_GndLevel.ToString().PadRight(34) + "│");
                sb.AppendLine("│  NSR = " + t.Stator_NSR.ToString("F4").PadRight(12) +
                              "ZSR = " + t.Stator_ZSR.ToString("F4").PadRight(12) +
                              "HARM = " + t.Stator_HarmRatio.ToString("F4").PadRight(12) +
                              "IMB = " + t.Stator_Imbalance.ToString("F1").PadRight(4) + "%" + " │");
                sb.AppendLine("│  Temperature : " + FormatTemperature(t).PadRight(58) + "│");
            }
            else
            {
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  No telemetry data received yet.                                        │");
                sb.AppendLine("│  Connect to H750 and wait for live telemetry stream.                    │");
            }
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("└──────────────────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // ══════════════════════════════════════════════════════════════════════
            // SECTION 4: MOTOR PARAMETERS
            // ══════════════════════════════════════════════════════════════════════
            sb.AppendLine("┌──────────────────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│  4. MOTOR / CONTROLLER PARAMETERS                                       │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────────────────┤");

            if (_lastFrame != null && _lastFrame.ReportMotorParams != null)
            {
                MotorParameters mp = _lastFrame.ReportMotorParams;
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  Motor RPM       : " + mp.MotorRPM.ToString("F1").PadRight(54) + "│");
                sb.AppendLine("│  Slip            : " + mp.Slip.ToString("F4").PadRight(54) + "│");
                sb.AppendLine("│  Supply Line (Hz): " + mp.SupplyLineHz.ToString("F2").PadRight(54) + "│");
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  Bearing Coefficients:                                                   │");
                sb.AppendLine("│    BPFO = " + mp.BPFO.ToString("F4").PadRight(14) +
                              "BPFI = " + mp.BPFI.ToString("F4").PadRight(14) +
                              "BSF = " + mp.BSF.ToString("F4").PadRight(14) +
                              "FTF = " + mp.FTF.ToString("F4").PadRight(5) + "│");
            }
            else
            {
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  No motor parameters received yet.                                      │");
                sb.AppendLine("│  Use 'Full Report' button to retrieve controller parameters.            │");
            }
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("└──────────────────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // ══════════════════════════════════════════════════════════════════════
            // SECTION 5: SPECTRAL FRAME DATA
            // ══════════════════════════════════════════════════════════════════════
            sb.AppendLine("┌──────────────────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│  5. SPECTRAL FRAME DATA                                                 │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────────────────┤");

            if (_lastFrame != null)
            {
                string mode = string.IsNullOrEmpty(_lastFrame.Mode) ? "n/a" : _lastFrame.Mode;
                string endMark = string.IsNullOrEmpty(_lastFrame.FinalReportSummary) ? "n/a" : _lastFrame.FinalReportSummary;
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  Mode       : " + mode.PadRight(59) + "│");
                sb.AppendLine("│  End Marker : " + endMark.PadRight(59) + "│");
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  +─────────────────┬──────────┬──────────┬──────────┬──────────────+    │");
                sb.AppendLine("│  │ Technique       │ Mean Pts │ Phase 1  │ Phase 2  │ Phase 3      │    │");
                sb.AppendLine("│  +─────────────────┼──────────┼──────────┼──────────┼──────────────+    │");
                sb.AppendLine("│  │ FOURIER         │ " +
                    _lastFrame.FourierPoints.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.FourierPhase1Points.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.FourierPhase2Points.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.FourierPhase3Points.Count.ToString().PadRight(13) + "│    │");
                sb.AppendLine("│  │ MUSIC           │ " +
                    _lastFrame.MusicPoints.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.MusicPhase1Points.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.MusicPhase2Points.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.MusicPhase3Points.Count.ToString().PadRight(13) + "│    │");
                sb.AppendLine("│  │ CYCLOSTATIONARY │ " +
                    _lastFrame.Cyclic2Points.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.Cyclic2Phase1Points.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.Cyclic2Phase2Points.Count.ToString().PadRight(9) + "│ " +
                    _lastFrame.Cyclic2Phase3Points.Count.ToString().PadRight(13) + "│    │");
                sb.AppendLine("│  │ ESPRIT          │ " +
                    _lastFrame.EspritFrequencies.Count.ToString().PadRight(9) + "│    —     │    —     │      —       │    │");
                sb.AppendLine("│  │ SK              │ " +
                    _lastFrame.SkPoints.Count.ToString().PadRight(9) + "│    —     │    —     │      —       │    │");
                sb.AppendLine("│  │ WAVELET         │ " +
                    _lastFrame.WaveletPoints.Count.ToString().PadRight(9) + "│    —     │    —     │      —       │    │");
                sb.AppendLine("│  +─────────────────┴──────────┴──────────┴──────────┴──────────────+    │");
            }
            else
            {
                sb.AppendLine("│                                                                          │");
                sb.AppendLine("│  No spectral frame available.                                           │");
                sb.AppendLine("│  Use a technique button or 'Full Report' to acquire spectral data.      │");
            }
            sb.AppendLine("│                                                                          │");
            sb.AppendLine("└──────────────────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // ══════════════════════════════════════════════════════════════════════
            // SECTION 6: USB COMMAND REFERENCE
            // ══════════════════════════════════════════════════════════════════════
            AppendUsbCommandReference(sb);

            rtbTechniqueReport.Text = sb.ToString();
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

        /// <summary>LED-style indicator: green / yellow / red / gray beside each frequency.</summary>
        private sealed class LedIndicatorPanel : Panel
        {
            private byte _level = 3;

            /// <summary>0 = normal (green), 1 = warning (yellow), 2 = alarm (red), 3 = idle (gray)</summary>
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

            public LedIndicatorPanel()
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
                r.Inflate(-2, -2);
                if (r.Width < 4 || r.Height < 4) return;

                Color mid, dark, light;
                switch (Level)
                {
                    case 0:
                        mid = Color.FromArgb(46, 204, 113);
                        dark = Color.FromArgb(30, 160, 90);
                        light = Color.FromArgb(170, 245, 200);
                        break;
                    case 1:
                        mid = Color.FromArgb(241, 196, 15);
                        dark = Color.FromArgb(200, 155, 15);
                        light = Color.FromArgb(255, 245, 190);
                        break;
                    case 2:
                        mid = Color.FromArgb(231, 76, 60);
                        dark = Color.FromArgb(180, 50, 40);
                        light = Color.FromArgb(255, 190, 185);
                        break;
                    default:
                        mid = Color.FromArgb(189, 195, 199);
                        dark = Color.FromArgb(140, 145, 150);
                        light = Color.FromArgb(235, 238, 240);
                        break;
                }

                using (var br = new LinearGradientBrush(r, light, mid, LinearGradientMode.ForwardDiagonal))
                    g.FillEllipse(br, r);
                using (var p = new Pen(dark, 1.2f))
                    g.DrawEllipse(p, r);

                int gl = Math.Max(2, r.Width / 5);
                Rectangle glint = new Rectangle(r.Left + 2, r.Top + 2, gl, gl);
                using (var hb = new SolidBrush(Color.FromArgb(130, 255, 255, 255)))
                    g.FillEllipse(hb, glint);
            }
        }
    }
}