using EES_MotorOverload_V1.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
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

        // ── Alarm Monitor Tab ──
        private GroupBox grpBearingFault, grpTemperature, grpStatorShort;

        private AlarmStatusCardPanel pnlAlarmStatusCard;
        private Label lblAlarmStatusTitle;
        private Label lblAlarmStatusPrimary;
        private Label lblAlarmStatusBearing;
        private Label lblAlarmStatusStator;
        private Panel pnlAlarmSeverityBadge;
        private Label lblAlarmSeverityLetter;
        private Label lblInner, lblFTFAlarm, lblBSFAlarm, lblOuter;
        private Button btnAlarmStop, btnAlarmCancel, btnAlarmTest;
        private TextBox txtTemperature;
        private Label lblTempUnit;
        private TrackBar trkStatorShort;
        private TextBox txtShortValue;

        // ── Harmonics Tab ──
        private GroupBox grpHarmonicControl;
        private Button btnChartClear, btnChartStop, btnChartContinue;

        // Technique selection buttons
        private Button btnTechFourier, btnTechMusic, btnTechEsprit, btnTechCyclo;

        private MyChartClass _xyChart1;
        private MyChartClass _xyChart2;
        private MyChartClass _xyChart3;
        private Label lblFinalReportStatus;
        private Label lblPhase1, lblPhase2, lblPhase3;

        // Current technique selection and last received frame
        private enum SpectralTechnique { Fourier, Music, Esprit, Cyclostationary }
        private SpectralTechnique _currentTechnique = SpectralTechnique.Fourier;
        private SpectralFrame _lastFrame = null;

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
            tabAlarmMonitor = new TabPage("Alarm Monitor");
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

            string[] names = { "Bearing Select", "Alarm Monitor", "Harmonics",
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
        // Alarm Monitor Tab
        // ─────────────────────────────────────────────────────────────
        
        private void BuildAlarmMonitorTab()
        {
            const int cardTop = 12;
            const int cardHeight = 120;
            const int gapAfterCard = 14;
            int topY = cardTop + cardHeight + gapAfterCard;

            tabAlarmMonitor.AutoScroll = true;
            tabAlarmMonitor.Padding = new Padding(0);

            pnlAlarmStatusCard = new AlarmStatusCardPanel
            {
                Location = new Point(20, cardTop),
                Size = new Size(960, cardHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lblAlarmStatusTitle = new Label
            {
                Text = "CONDITION SUMMARY",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                ForeColor = Color.FromArgb(95, 95, 95),
                AutoSize = false,
                Location = new Point(44, 12),
                Size = new Size(420, 16),
                BackColor = Color.Transparent
            };

            lblAlarmStatusPrimary = new Label
            {
                Text = "Awaiting telemetry…",
                Font = new Font("Segoe UI", 14.25F, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 55, 55),
                AutoSize = false,
                Location = new Point(44, 32),
                Size = new Size(700, 30),
                BackColor = Color.Transparent
            };

            lblAlarmStatusBearing = new Label
            {
                Text = "Bearing path: —",
                Font = new Font("Segoe UI", 9.25F, FontStyle.Regular),
                ForeColor = Color.FromArgb(45, 45, 45),
                AutoSize = false,
                Location = new Point(44, 64),
                Size = new Size(760, 22),
                BackColor = Color.Transparent
            };

            lblAlarmStatusStator = new Label
            {
                Text = "Stator winding: —",
                Font = new Font("Segoe UI", 9.25F, FontStyle.Regular),
                ForeColor = Color.FromArgb(45, 45, 45),
                AutoSize = false,
                Location = new Point(44, 86),
                Size = new Size(760, 22),
                BackColor = Color.Transparent
            };

            pnlAlarmSeverityBadge = new CircularBadgePanel
            {
                Location = new Point(820, 22),
                Size = new Size(76, 76),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            lblAlarmSeverityLetter = new Label
            {
                Text = "—",
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlAlarmSeverityBadge.Controls.Add(lblAlarmSeverityLetter);

            pnlAlarmStatusCard.Controls.Add(lblAlarmStatusTitle);
            pnlAlarmStatusCard.Controls.Add(lblAlarmStatusPrimary);
            pnlAlarmStatusCard.Controls.Add(lblAlarmStatusBearing);
            pnlAlarmStatusCard.Controls.Add(lblAlarmStatusStator);
            pnlAlarmStatusCard.Controls.Add(pnlAlarmSeverityBadge);

            tabAlarmMonitor.Controls.Add(pnlAlarmStatusCard);
            tabAlarmMonitor.Resize += TabAlarmMonitor_Resize;

            grpBearingFault = new GroupBox
            {
                Text = "Bearing Fault Indicators",
                Location = new Point(20, topY),
                Size = new Size(350, 380),
                Font = new Font("Tahoma", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };

            string[] faultNames = { "Inner Race (BPFI)", "FTF (Cage)",
                                     "BSF (Ball)", "Outer Race (BPFO)" };
            Label[] faultLabels = new Label[4];
            _ledAlarmFrequencies = new LedIndicatorPanel[4];

            for (int i = 0; i < 4; i++)
            {
                faultLabels[i] = new Label
                {
                    Text = faultNames[i],
                    Location = new Point(34, 50 + i * 80),
                    Size = new Size(280, 30),
                    Font = new Font("Tahoma", 12F, FontStyle.Bold),
                    ForeColor = Color.Black
                };
                grpBearingFault.Controls.Add(faultLabels[i]);

                _ledAlarmFrequencies[i] = new LedIndicatorPanel
                {
                    Location = new Point(12, 54 + i * 80),
                    Size = new Size(16, 16)
                };
                grpBearingFault.Controls.Add(_ledAlarmFrequencies[i]);
            }

            lblInner = faultLabels[0]; lblFTFAlarm = faultLabels[1];
            lblBSFAlarm = faultLabels[2]; lblOuter = faultLabels[3];
            tabAlarmMonitor.Controls.Add(grpBearingFault);

            int btnY = topY + 390;
            btnAlarmTest = new Button
            {
                Text = "Test All",
                Location = new Point(20, btnY),
                Size = new Size(100, 55),
                Font = new Font("Tahoma", 10F, FontStyle.Bold)
            };

            btnAlarmCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(130, btnY),
                Size = new Size(100, 55),
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                ForeColor = Color.Blue
            };

            btnAlarmStop = new Button
            {
                Text = "Stop",
                Location = new Point(240, btnY),
                Size = new Size(100, 55),
                Font = new Font("Tahoma", 10F, FontStyle.Bold)
            };

            tabAlarmMonitor.Controls.AddRange(new Control[] { btnAlarmTest, btnAlarmCancel, btnAlarmStop });

            grpTemperature = new GroupBox
            {
                Text = "Max. Temperature",
                Location = new Point(400, topY),
                Size = new Size(200, 380),
                Font = new Font("Tahoma", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };

            txtTemperature = new TextBox
            {
                Location = new Point(30, 330),
                Size = new Size(80, 28),
                Font = new Font("Tahoma", 12F)
            };
            lblTempUnit = new Label
            {
                Text = "\u00B0C",
                Location = new Point(115, 333),
                Size = new Size(40, 25),
                Font = new Font("Tahoma", 12F, FontStyle.Bold)
            };

            grpTemperature.Controls.AddRange(new Control[] { txtTemperature, lblTempUnit });
            tabAlarmMonitor.Controls.Add(grpTemperature);

            grpStatorShort = new GroupBox
            {
                Text = "Stator Short Monitoring",
                Location = new Point(620, topY),
                Size = new Size(380, 160),
                Font = new Font("Tahoma", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };

            trkStatorShort = new TrackBar
            {
                Location = new Point(120, 50),
                Size = new Size(240, 50),
                Minimum = 0,
                Maximum = 100
            };

            txtShortValue = new TextBox
            {
                Location = new Point(10, 55),
                Size = new Size(100, 28),
                Font = new Font("Tahoma", 10F)
            };

            trkStatorShort.Scroll += (s, e) =>
            {
                txtShortValue.Text = trkStatorShort.Value.ToString();
            };

            grpStatorShort.Controls.AddRange(new Control[] { trkStatorShort, txtShortValue });
            tabAlarmMonitor.Controls.Add(grpStatorShort);
        }
        
        private void TabAlarmMonitor_Resize(object sender, EventArgs e)
        {
            if (pnlAlarmStatusCard == null || tabAlarmMonitor == null) return;
            int w = tabAlarmMonitor.ClientSize.Width - 40;
            if (w < 320) w = 320;
            pnlAlarmStatusCard.Width = w;
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
            const int controlBarHeight = 80;
            const int finalReportStripH = 36;
            const int labelHeight = 22;
            const int chartPadding = 4;
            const int topMargin = 5;

            Color[] phaseColors = { Color.FromArgb(41, 128, 185),
                                    Color.FromArgb(39, 174, 96),
                                    Color.FromArgb(192, 57, 43) };

            lblFinalReportStatus = new Label
            {
                Text = "Final report (STM32): not received yet — firmware ends each export with ### END_REPORT or ### END_EXPORT.",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 100, 100),
                AutoSize = false,
                Location = new Point(chartPadding, topMargin),
                Size = new Size(900, finalReportStripH)
            };

            _xyChart1 = new MyChartClass { PhaseName = "Phase 1" };
            _xyChart2 = new MyChartClass { PhaseName = "Phase 2" };
            _xyChart3 = new MyChartClass { PhaseName = "Phase 3" };

            lblPhase1 = new Label
            {
                Text = "Phase 1",
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
            _xyChart2.SetSeriesColor(phaseColors[1]);
            _xyChart3.SetSeriesColor(phaseColors[2]);

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
                lblFinalReportStatus.Location = new Point(chartPadding, topMargin);
                lblFinalReportStatus.Width = tabW - 2 * chartPadding;
                int availableHeight = tabH - controlBarHeight - topMargin - finalReportStripH - 4;
                int chartHeight = (availableHeight - 3 * labelHeight - 3 * chartPadding) / 3;
                if (chartHeight < 40) chartHeight = 40;

                int y = topMargin + finalReportStripH + 4;
                for (int i = 0; i < 3; i++)
                {
                    labels[i].Location = new Point(chartPadding, y);
                    y += labelHeight;
                    charts[i].Location = new Point(chartPadding, y);
                    charts[i].Size = new Size(tabW - 2 * chartPadding, chartHeight);
                    y += chartHeight + chartPadding;
                }
            };

            tabHarmonics.Resize += (s, e) => layoutCharts();
            tabHarmonics.Layout += (s, e) => layoutCharts();
            layoutCharts();

            grpHarmonicControl = new GroupBox
            {
                Text = "Spectral Display Control",
                Size = new Size(820, 70),
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            Action layoutControlBar = () =>
            {
                grpHarmonicControl.Location = new Point(
                    10,
                    tabHarmonics.ClientSize.Height - controlBarHeight + 5);
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

            grpHarmonicControl.Controls.AddRange(new Control[] {
                btnChartClear, btnChartStop, btnChartContinue,
                btnTechFourier, btnTechMusic, btnTechEsprit, btnTechCyclo });
            tabHarmonics.Controls.Add(grpHarmonicControl);

            _xyChart1.SetUpdateInterval(50);
            _xyChart2.SetUpdateInterval(50);
            _xyChart3.SetUpdateInterval(50);
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

            // Request fresh data from STM32 if connected
            if (_comm != null && _comm.IsConnected)
            {
                LogUI("Requesting spectral report for " + tech.ToString() + "...", Color.Yellow);
                if (lblFinalReportStatus != null)
                {
                    lblFinalReportStatus.Text = "Final report (STM32): receiving… (waiting for ### END_REPORT)";
                    lblFinalReportStatus.ForeColor = Color.DarkGoldenrod;
                }

                List<string> lines = await _comm.RequestReport();

                if (lines != null && lines.Count > 0)
                {
                    LogUI("Report: " + lines.Count + " lines received", Color.LimeGreen);
                    foreach (string line in lines)
                    {
                        _reportParser.ParseLine(line);
                    }
                }
                else
                {
                    LogUI("Report: no data received", Color.Red);
                    if (lblFinalReportStatus != null)
                    {
                        lblFinalReportStatus.Text = "Final report: no data (timeout or empty response)";
                        lblFinalReportStatus.ForeColor = Color.OrangeRed;
                    }
                }
            }

            // Display using whatever data we have (fresh or cached)
            RedrawChartsForCurrentTechnique();
            SetTechniqueButtonsEnabled(true);
        }

        private void SetTechniqueButtonsEnabled(bool enabled)
        {
            if (btnTechFourier != null) btnTechFourier.Enabled = enabled;
            if (btnTechMusic != null) btnTechMusic.Enabled = enabled;
            if (btnTechEsprit != null) btnTechEsprit.Enabled = enabled;
            if (btnTechCyclo != null) btnTechCyclo.Enabled = enabled;
        }



        private void UpdateTechniqueButtonColors()
        {
            Color active = Color.FromArgb(41, 128, 185);
            Color inactive = Color.FromArgb(52, 73, 94);

            btnTechFourier.BackColor = (_currentTechnique == SpectralTechnique.Fourier) ? active : inactive;
            btnTechMusic.BackColor = (_currentTechnique == SpectralTechnique.Music) ? active : inactive;
            btnTechEsprit.BackColor = (_currentTechnique == SpectralTechnique.Esprit) ? active : inactive;
            btnTechCyclo.BackColor = (_currentTechnique == SpectralTechnique.Cyclostationary) ? active : inactive;
        }


        /// <summary>
        /// Redraws the 3 phase charts using the last received frame
        /// and the currently selected technique.
        /// </summary>
        private void RedrawChartsForCurrentTechnique()
        {
            if (_lastFrame == null)
            {
                LogUI("No spectral frame available — press 'Request Report' first", Color.Orange);
                return;
            }

            List<PointF> points = null;
            string techName = "";

            switch (_currentTechnique)
            {
                case SpectralTechnique.Fourier:
                    points = _lastFrame.FourierPoints;
                    techName = "Fourier";
                    break;
                case SpectralTechnique.Music:
                    points = _lastFrame.MusicPoints;
                    techName = "MUSIC";
                    break;
                case SpectralTechnique.Esprit:
                    DisplayEspritOnCharts(_lastFrame.EspritFrequencies);
                    return;
                case SpectralTechnique.Cyclostationary:
                    points = _lastFrame.Cyclic2Points;
                    techName = "Cyclostationary";
                    break;
            }

            if (points == null || points.Count == 0)
            {
                _xyChart1?.ClearData();
                _xyChart2?.ClearData();
                _xyChart3?.ClearData();
                lblPhase1.Text = "Phase 1 — " + techName + " (no data)";
                lblPhase2.Text = "Phase 2 — " + techName + " (no data)";
                lblPhase3.Text = "Phase 3 — " + techName + " (no data)";
                LogUI(techName + ": no spectral data in last frame", Color.Orange);
                return;
            }

            UpdateChartFromPoints(_xyChart1, points, techName + " Ph1");
            UpdateChartFromPoints(_xyChart2, points, techName + " Ph2");
            UpdateChartFromPoints(_xyChart3, points, techName + " Ph3");

            lblPhase1.Text = "Phase 1 — " + techName;
            lblPhase2.Text = "Phase 2 — " + techName;
            lblPhase3.Text = "Phase 3 — " + techName;
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
                _xyChart2?.ClearData();
                _xyChart3?.ClearData();
                lblPhase1.Text = "Phase 1 — ESPRIT (no peaks found)";
                lblPhase2.Text = "Phase 2 — ESPRIT (no peaks found)";
                lblPhase3.Text = "Phase 3 — ESPRIT (no peaks found)";
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
            UpdateChartFromPoints(_xyChart2, displayPoints, "ESPRIT Ph2");
            UpdateChartFromPoints(_xyChart3, displayPoints, "ESPRIT Ph3");

            // Build frequency list string for labels
            string freqList = "";
            for (int i = 0; i < sorted.Count && i < 6; i++)
            {
                if (i > 0) freqList += ", ";
                freqList += sorted[i].ToString("F1") + " Hz";
            }
            if (sorted.Count > 6) freqList += " ...";

            lblPhase1.Text = "Phase 1 — ESPRIT (" + sorted.Count + " peaks: " + freqList + ")";
            lblPhase2.Text = "Phase 2 — ESPRIT (" + sorted.Count + " peaks)";
            lblPhase3.Text = "Phase 3 — ESPRIT (" + sorted.Count + " peaks)";

            LogUI("ESPRIT: displaying " + sorted.Count + " frequency estimates: " + freqList, Color.Cyan);
        }


        /// <summary>
        /// Sends the REPORT command to the STM32.
        /// </summary>
        private async void BtnRequestReport_Click(object sender, EventArgs e)
        {
            if (_comm == null || !_comm.IsConnected)
            {
                MessageBox.Show("Connect to STM32 first.", "Not Connected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var btn = sender as Button;
            if (btn != null) btn.Enabled = false;
            LogUI("Requesting spectral report...", Color.Yellow);
            if (lblFinalReportStatus != null)
            {
                lblFinalReportStatus.Text = "Final report (STM32): receiving… (waiting for ### END_REPORT)";
                lblFinalReportStatus.ForeColor = Color.DarkGoldenrod;
            }

            List<string> lines = await _comm.RequestReport();

            if (lines != null && lines.Count > 0)
            {
                LogUI("Report: " + lines.Count + " lines received", Color.LimeGreen);
                foreach (string line in lines)
                {
                    _reportParser.ParseLine(line);
                }
            }
            else
            {
                LogUI("Report: no data received", Color.Red);
                if (lblFinalReportStatus != null)
                {
                    lblFinalReportStatus.Text = "Final report: no data (timeout or empty response)";
                    lblFinalReportStatus.ForeColor = Color.OrangeRed;
                }
            }

            if (btn != null) btn.Enabled = true;
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
                    RedrawChartsForCurrentTechnique();

                    string fin = string.IsNullOrEmpty(frame.FinalReportSummary)
                        ? "(marker not set)"
                        : frame.FinalReportSummary;
                    if (lblFinalReportStatus != null)
                    {
                        lblFinalReportStatus.Text = "Final report: complete — " + fin + " — " +
                            DateTime.Now.ToString("HH:mm:ss");
                        lblFinalReportStatus.ForeColor = Color.ForestGreen;
                    }

                    LogUI("STM32 final report: " + fin, Color.LimeGreen);
                    LogUI("Frame received: Fourier=" + frame.FourierPoints.Count +
                          " MUSIC=" + frame.MusicPoints.Count +
                          " Cyclic2=" + frame.Cyclic2Points.Count +
                          " ESPRIT=" + frame.EspritFrequencies.Count +
                          " Wavelet=" + frame.WaveletPoints.Count + " pts",
                          Color.Cyan);
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
                }
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
            grpLive = new GroupBox
            {
                Text = "Live Telemetry from Controller",
                Location = new Point(330, y),
                Size = new Size(620, 105),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(39, 174, 96)
            };

            string[] lvLabels = { "BPFO (Hz):", "BPFI (Hz):", "BSF (Hz):",
                                   "FTF (Hz):", "Fault Index:", "Fault Level:" };
            Label[] lvValues = new Label[6];

            const int ledSz = 20;
            for (int i = 0; i < 3; i++)
            {
                SettingsMakeLabel(lvLabels[i], 10, 24 + i * 25, grpLive, 9F);
                lvValues[i] = new Label
                {
                    Text = "----",
                    Location = new Point(126, 24 + i * 25),
                    Size = new Size(165, 20),
                    Font = new Font("Consolas", 9.5F, FontStyle.Bold),
                    ForeColor = Color.Black,
                    Parent = grpLive
                };
                var led = new LedIndicatorPanel { Location = new Point(104, 22 + i * 25), Size = new Size(ledSz, ledSz) };
                grpLive.Controls.Add(led);
                if (i == 0) ledLiveBpfo = led;
                else if (i == 1) ledLiveBpfi = led;
                else ledLiveBsf = led;
            }

            for (int i = 0; i < 3; i++)
            {
                SettingsMakeLabel(lvLabels[i + 3], 310, 24 + i * 25, grpLive, 9F);
                lvValues[i + 3] = new Label
                {
                    Text = "----",
                    Location = new Point(390, 24 + i * 25),
                    Size = new Size(165, 20),
                    Font = new Font("Consolas", 9.5F, FontStyle.Bold),
                    ForeColor = Color.Black,
                    Parent = grpLive
                };
                if (i == 0)
                {
                    ledLiveFtf = new LedIndicatorPanel { Location = new Point(368, 22), Size = new Size(ledSz, ledSz) };
                    grpLive.Controls.Add(ledLiveFtf);
                }
            }

            lblBpfoHz = lvValues[0]; lblBpfiHz = lvValues[1];
            lblBsfHz = lvValues[2]; lblFtfHz = lvValues[3];
            lblFaultIndex = lvValues[4]; lblFaultLevel = lvValues[5];
            tabSettings.Controls.Add(grpLive);

            y += 110;

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
            // ── Alarm Monitor ──
            grpMonitor = new GroupBox
            {
                Text = "Alarm Monitor",
                Location = new Point(10, y),
                Size = new Size(940, 60),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                 ForeColor = Color.FromArgb(255, 0, 0)
            };
            tabSettings.Controls.Add(grpMonitor);

            string TLabel = "Temperature(°C):";
            TextBox TempBox = new TextBox{}; 
            SettingsMakeLabel(TLabel, 10, 28, grpMonitor);
            TempBox = SettingsMakeTextBox(133, 28 , 100, " ", grpMonitor);
            TempBox.Enabled= false;
            
            int h = 300;
            // Add "Stator Short Monitoring" label
            Label lblStatorShortMonitoring = new Label
            {
                Text = "Stator Short Monitoring:",
                Location = new Point(h, 28),
                Size = new Size(180, 25),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.Black
            };
            grpMonitor.Controls.Add(lblStatorShortMonitoring);

                        
            h += 180;
            // Add MyBar to show progression
            MyBar barStatorShort = new MyBar
            {
                Location = new Point(h, 28),
                Size = new Size(300, 25),
                LowProgressColor = Color.Green,
                MediumProgressColor = Color.Yellow,
                HighProgressColor = Color.Orange,
                CompleteColor = Color.Red,
                Minimum = 0,
                Maximum = 100,
                Value = 50 // Example initial value
            };
            grpMonitor.Controls.Add(barStatorShort);

            h += 310;
            // Add TextBox to show the value
            TextBox txtStatorShort = new TextBox
            {
                Location = new Point(h, 28),
                Size = new Size(80, 25),
                Font = new Font("Segoe UI", 10F),
                Enabled = false // Read-only
            };
            grpMonitor.Controls.Add(txtStatorShort); 

            // Example of setting Stator Short Monitoring value dynamically
            txtStatorShort.Text = "50";       // Bind this to a dynamic source as required
            barStatorShort.Value = 30;       // Adjust dynamically as required




            y += 70;

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
                chkUseTextProtocol
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
            if (_paramControls == null) return;
            for (int i = 0; i < _paramControls.Length; i++)
            {
                if (_paramControls[i] != null)
                    _paramControls[i].Enabled = enabled;
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
                        "Final report (STM32): waiting — stream or Request Report; complete = ### END_REPORT / ### END_EXPORT.";
                    lblFinalReportStatus.ForeColor = Color.FromArgb(100, 100, 100);
                }
                UpdateAlarmMonitorDashboard(null);
                ApplyFrequencyLeds(3, true);
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
                        "Final report (STM32): not connected — connect to receive ### END_REPORT / ### END_EXPORT.";
                    lblFinalReportStatus.ForeColor = Color.FromArgb(100, 100, 100);
                }
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

        private void Comm_OnTelemetry(TelemetryData t)
        {
            if (_formClosing) return;

            try
            {
                this.BeginInvoke(new Action(delegate
                {
                    lblBpfoHz.Text = t.BPFO_Hz.ToString("F2") + " Hz";
                    lblBpfiHz.Text = t.BPFI_Hz.ToString("F2") + " Hz";
                    lblBsfHz.Text = t.BSF_Hz.ToString("F2") + " Hz";
                    lblFtfHz.Text = t.FTF_Hz.ToString("F2") + " Hz";
                    lblFaultIndex.Text = t.FaultIndex.ToString("F4");

                    lblFaultLevel.Text = t.FaultLevelString;
                    switch (t.FaultLevel)
                    {
                        case 0: lblFaultLevel.ForeColor = Color.Green; break;
                        case 1: lblFaultLevel.ForeColor = Color.Orange; break;
                        case 2: lblFaultLevel.ForeColor = Color.Red; break;
                        default: lblFaultLevel.ForeColor = Color.Gray; break;
                    }

                    UpdateAlarmMonitorDashboard(t);

                    Color bearingAccent = AlarmSeverityAccentColor(t.FaultLevel);
                    lblInner.ForeColor = bearingAccent;
                    lblFTFAlarm.ForeColor = bearingAccent;
                    lblBSFAlarm.ForeColor = bearingAccent;
                    lblOuter.ForeColor = bearingAccent;
                    lblInner.Text = "BPFI (inner): " + t.BPFI_Hz.ToString("F2") + " Hz";
                    lblFTFAlarm.Text = "FTF (cage): " + t.FTF_Hz.ToString("F2") + " Hz";
                    lblBSFAlarm.Text = "BSF (ball): " + t.BSF_Hz.ToString("F2") + " Hz";
                    lblOuter.Text = "BPFO (outer): " + t.BPFO_Hz.ToString("F2") + " Hz";

                    ApplyFrequencyLeds(t.FaultLevel, true);
                }));
            }
            catch { }
        }

        /// <summary>
        /// Green / yellow / red LEDs beside each frequency readout (Settings + Alarm Monitor).
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
            if (_ledAlarmFrequencies != null)
            {
                for (int i = 0; i < _ledAlarmFrequencies.Length; i++)
                    if (_ledAlarmFrequencies[i] != null)
                        _ledAlarmFrequencies[i].Level = lv;
            }
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

        /// <summary>
        /// Alarm Monitor tab: professional condition summary (green / yellow / red) from STM32 telemetry.
        /// </summary>
        private void UpdateAlarmMonitorDashboard(TelemetryData t)
        {
            if (pnlAlarmStatusCard == null || lblAlarmStatusPrimary == null) return;

            bool connected = _comm != null && _comm.IsConnected;
            if (!connected)
            {
                pnlAlarmStatusCard.Severity = -1;
                lblAlarmStatusPrimary.Text = "Not connected";
                lblAlarmStatusPrimary.ForeColor = Color.FromArgb(75, 75, 75);
                lblAlarmStatusBearing.Text = "Bearing path: —";
                lblAlarmStatusStator.Text = "Stator winding: —";
                lblAlarmSeverityLetter.Text = "—";
                if (pnlAlarmSeverityBadge is CircularBadgePanel cb0)
                    cb0.BadgeBackColor = Color.FromArgb(149, 165, 166);
                pnlAlarmStatusCard.Invalidate();
                pnlAlarmSeverityBadge?.Invalidate();
                return;
            }

            if (t == null)
            {
                pnlAlarmStatusCard.Severity = -1;
                lblAlarmStatusPrimary.Text = "Connected — waiting for telemetry…";
                lblAlarmStatusPrimary.ForeColor = Color.FromArgb(75, 75, 75);
                lblAlarmStatusBearing.Text = "Bearing path: —";
                lblAlarmStatusStator.Text = "Stator winding: —";
                lblAlarmSeverityLetter.Text = "…";
                if (pnlAlarmSeverityBadge is CircularBadgePanel cb1)
                    cb1.BadgeBackColor = Color.FromArgb(149, 165, 166);
                pnlAlarmStatusCard.Invalidate();
                pnlAlarmSeverityBadge?.Invalidate();
                return;
            }

            int overall = Math.Max((int)t.FaultLevel, t.Stator_FaultLevel);
            if (overall > 2) overall = 2;

            pnlAlarmStatusCard.Severity = overall;
            if (pnlAlarmSeverityBadge is CircularBadgePanel cb)
                cb.BadgeBackColor = AlarmOverallAccentColor(overall);

            lblAlarmSeverityLetter.Text = overall == 0 ? "N" : (overall == 1 ? "W" : "A");
            lblAlarmStatusPrimary.ForeColor = Color.FromArgb(40, 40, 40);

            switch (overall)
            {
                case 0:
                    lblAlarmStatusPrimary.Text = "Normal — all monitored paths within limits";
                    break;
                case 1:
                    lblAlarmStatusPrimary.Text = "Warning — elevated bearing and/or stator indicators";
                    break;
                default:
                    lblAlarmStatusPrimary.Text = "Alarm — fault condition on bearing and/or stator";
                    break;
            }

            lblAlarmStatusBearing.Text =
                "Bearing: " + t.FaultLevelString +
                " | Fault index " + t.FaultIndex.ToString("F4") +
                " | Kurtogram band " + t.KurtBandHz + " Hz";

            lblAlarmStatusStator.Text =
                "Stator: short L" + t.Stator_ShortLevel +
                ", ground L" + t.Stator_GndLevel +
                ", combined LV " + t.Stator_FaultLevel +
                " | NSR " + t.Stator_NSR.ToString("F3") +
                ", ZSR " + t.Stator_ZSR.ToString("F3") +
                ", imbalance " + t.Stator_Imbalance.ToString("F1") + "%";

            pnlAlarmStatusCard.Invalidate();
            pnlAlarmSeverityBadge?.Invalidate();
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