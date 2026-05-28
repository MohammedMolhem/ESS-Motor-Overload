using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EES_MotorOverload_V1
{
    // =========================================================================
    // COMMAND CONSTANTS — must match main.c exactly
    // =========================================================================

    public static class STM32Commands
    {
        public const byte CMD_SET_RPM = 0x04;
        public const byte CMD_SET_BPFO = 0x05;
        public const byte CMD_SET_BPFI = 0x06;
        public const byte CMD_SET_FTF = 0x07;
        public const byte CMD_SET_BSF = 0x08;
        public const byte CMD_SET_FAULT_THRESH = 0x09;
        public const byte CMD_SET_WARNING_THRESH = 0x0A;
        public const byte CMD_SET_SLIP = 0x0B;

        public const byte CMD_GET_ALL_PARAMS = 0x80;
        public const byte CMD_SAVE_TO_FLASH = 0x30;
        public const byte CMD_LOAD_FROM_FLASH = 0x31;
        public const byte CMD_RESET_TO_DEFAULT = 0x32;
        /// <summary>Same as firmware CMD_SAVE_ADC_TO_SPI1 (ADC snapshot to SPI1 NOR).</summary>
        public const byte CMD_SAVE_ADC_TO_QSPI = 0x40;
        public const byte CMD_SAVE_ADC_TO_SPI1 = 0x40;
        public const byte CMD_PING = 0xFE;
    }

    /// <summary>
    /// Text USB commands from main.c USB_Process_TextLine / HELP.
    /// REPORT and FULLREPORT both call USB_Send_FullReport() and end with ### END_FULL_REPORT.
    /// </summary>
    public static class STM32TextCommands
    {
        public const string Ping = "PING";
        public const string Get = "GET";
        public const string Report = "REPORT";
        public const string FullReport = "FULLREPORT";
        public const string GraphData = "GRAPHDATA";
        public const string Phase3 = "PHASE3";
        public const string PhaseCsv = "PHASECSV";
        public const string FftCsv = "FFTCSV";
        public const string MusicCsv = "MUSICCSV";
        public const string EspritCsv = "ESPRITCSV";
        public const string Cyclic2Csv = "CYCLIC2CSV";
        public const string SkCsv = "SKCSV";
        public const string WaveletCsv = "WAVELETCSV";
        public const string Save = "SAVE";
        public const string Load = "LOAD";
        public const string Default = "DEFAULT";
        public const string SaveAdc = "SAVEADC";
        public const string Calib = "CALIB";
        public const string SaveBase = "SAVEBASE";
        public const string LoadBase = "LOADBASE";
        public const string ClearBase = "CLEARBASE";
        public const string CalibSt = "CALIBST";
        public const string SaveStSt = "SAVESTST";
        public const string LoadStSt = "LOADSTST";
        public const string ClearStSt = "CLEARSTST";
        public const string Help = "HELP";
    }

    /// <summary>How WaitForMultiLineResponse decides the transfer is complete (main.c end markers).</summary>
    public enum UsbMultiLineEndMode
    {
        /// <summary>### END_EXPORT (FFTCSV, MUSICCSV, …).</summary>
        TechniqueExport,
        /// <summary>### END_REPORT (spectral-only block inside full report).</summary>
        SpectralReport,
        /// <summary>### END_GRAPHDATA (GRAPHDATA / GRAPHS).</summary>
        GraphData,
        /// <summary>### END_FULL_REPORT (REPORT / FULLREPORT — ignore intermediate END_REPORT).</summary>
        FullReport,
        /// <summary>### END_PHASE_CSV (PHASE3 / PHASECSV).</summary>
        PhaseCsv
    }

    // =========================================================================
    // DATA STRUCTURES
    // =========================================================================

    public class MotorParameters
    {
        public float MotorRPM { get; set; }
        public float Slip { get; set; }
        public float BPFO { get; set; }
        public float BPFI { get; set; }
        public float FTF { get; set; }
        public float BSF { get; set; }
        public float FaultThreshold { get; set; }
        public float WarningThreshold { get; set; }

        /// <summary>Supply line frequency (Hz); from GET text LINE= (main.c).</summary>
        public float SupplyLineHz { get; set; }

        /// <summary>Software clock date from GET text DATE= (yyyy-mm-dd).</summary>
        public string ClockDate { get; set; }

        /// <summary>Software clock time from GET text TIME= (hh:mm:ss).</summary>
        public string ClockTime { get; set; }

        public override string ToString()
        {
            string s =
                "  Motor Parameters (from STM32):\r\n" +
                "  RPM:               " + MotorRPM.ToString("F1") + "\r\n" +
                "  Slip:              " + Slip.ToString("F4") + "\r\n" +
                "  BPFO:              " + BPFO.ToString("F3") + "\r\n" +
                "  BPFI:              " + BPFI.ToString("F3") + "\r\n" +
                "  FTF:               " + FTF.ToString("F3") + "\r\n" +
                "  BSF:               " + BSF.ToString("F3") + "\r\n" +
                "  Fault Threshold:   " + FaultThreshold.ToString("F2") + "\r\n" +
                "  Warning Threshold: " + WarningThreshold.ToString("F2") + "\r\n" +
                "  Supply Line (Hz):  " + SupplyLineHz.ToString("F2");
            if (!string.IsNullOrEmpty(ClockDate) || !string.IsNullOrEmpty(ClockTime))
                s += "\r\n" +
                     "  Clock (device):    " +
                     (string.IsNullOrEmpty(ClockDate) ? "—" : ClockDate) + " " +
                     (string.IsNullOrEmpty(ClockTime) ? "" : ClockTime);
            return s;
        }
    }

    public class TelemetryData
    {
        public float BPFO_Hz { get; set; }
        public float BPFI_Hz { get; set; }
        public float BSF_Hz { get; set; }
        public float FTF_Hz { get; set; }
        public float FaultIndex { get; set; }
        public byte FaultLevel { get; set; }
        public DateTime Timestamp { get; set; }

        // Extended telemetry indices
        public float Index_LS { get; set; }
        public float Index_Music { get; set; }
        public float Index_Esprit { get; set; }
        public float Index_Teager { get; set; }
        public float Index_SK { get; set; }
        public float Index_Wavelet { get; set; }
        public float Index_Cyclic { get; set; }
        public float Index_Sideband { get; set; }
        public float Index_EnvAcf { get; set; }
        public float Index_Bpfo { get; set; }
        public float Index_Bpfi { get; set; }
        public float Index_Bsf { get; set; }
        public float Index_Ftf { get; set; }
        public float FaultIndex_Ema { get; set; }
        public float CusumScore { get; set; }
        public float SkPeak { get; set; }
        public float SkPeakHz { get; set; }
        public int KurtBandHz { get; set; }
        public byte DominantFault { get; set; }
        public float TemperatureC { get; set; }
        public bool HasTemperature { get; set; }

        // Stator winding
        public float Stator_FrequencyHz { get; set; }
        public float Stator_NSR { get; set; }
        public float Stator_ZSR { get; set; }
        public float Stator_HarmRatio { get; set; }
        public float Stator_ResidRatio { get; set; }
        public float Stator_ZSR_H3 { get; set; }
        public float Stator_Imbalance { get; set; }
        public int Stator_ShortLevel { get; set; }
        public int Stator_GndLevel { get; set; }
        public int Stator_FaultLevel { get; set; }

        public string FaultLevelString
        {
            get
            {
                switch (FaultLevel)
                {
                    case 0: return "NORMAL";
                    case 1: return "WARNING";
                    case 2: return "FAULT";
                    default: return "?(" + FaultLevel + ")";
                }
            }
        }

        public override string ToString()
        {
            return "BPFO=" + BPFO_Hz.ToString("F2") + "Hz " +
                   "BPFI=" + BPFI_Hz.ToString("F2") + "Hz " +
                   "BSF=" + BSF_Hz.ToString("F2") + "Hz " +
                   "FTF=" + FTF_Hz.ToString("F2") + "Hz " +
                   "FI=" + FaultIndex.ToString("F3") + " " +
                   "LV=" + FaultLevel +
                   " EMA=" + FaultIndex_Ema.ToString("F3") +
                   " CUSUM=" + CusumScore.ToString("F2");
        }
    }

    // =========================================================================
    // COMMUNICATION MANAGER
    // =========================================================================

    public class STM32CommManager : IDisposable
    {
        private SerialPort _port;
        private readonly object _portLock = new object();
        private readonly List<byte> _rxBuffer = new List<byte>();

        // =====================================================================
        // FIX: Unified line queue replaces the old split text/response buffers.
        //
        // OLD DESIGN (BROKEN):
        //   _textBuffer (StringBuilder) accumulated raw chars.
        //   ProcessTextTelemetry() extracted lines and either:
        //     - Matched telemetry regex → fire event
        //     - If _awaitingTextResponse → push to _textResponseLines
        //     - Otherwise → DISCARD (this is where GET responses were lost)
        //
        // RACE CONDITION: ProcessTextTelemetry() runs on a 25ms timer.
        //   SendTextCommand() sets _awaitingTextResponse = true, then sends.
        //   But the response could arrive and be processed by the timer
        //   BEFORE _awaitingTextResponse was set, or the line could fail
        //   the telemetry regex and be discarded.
        //
        // NEW DESIGN (FIXED):
        //   _rawTextBuffer accumulates raw chars from serial.
        //   _completedLines is a queue of ALL complete lines.
        //   ProcessTextTelemetry() only moves chars → lines (no filtering).
        //   WaitForTextResponse() scans _completedLines directly.
        //   Telemetry parsing happens AFTER command responses are extracted.
        // =====================================================================

        private readonly StringBuilder _rawTextBuffer = new StringBuilder();
        private readonly object _rawTextLock = new object();

        private readonly List<string> _completedLines = new List<string>();
        private readonly object _linesLock = new object();

        private CancellationTokenSource _telemetryCts;
        private bool _disposed = false;

        // Set to true while a command is waiting for a response.
        // When true, ProcessTelemetryFromQueue() will NOT consume lines —
        // it leaves them for WaitForTextResponse() to pick up first.
        private volatile bool _commandPending = false;

        private const int CMD_TIMEOUT_MS = 3000;
        private const int PING_TIMEOUT_MS = 1500;
        private const int FLASH_TIMEOUT_MS = 5000;
        private const int INTER_CMD_DELAY = 50;
        /// <summary>REPORT/FULLREPORT: scalars + spectral + GRAPHDATA (main.c USB_Send_FullReport).</summary>
        private const int FULL_REPORT_TIMEOUT_MS = 180000;
        private const int GRAPHDATA_TIMEOUT_MS = 120000;
        private const int TECHNIQUE_EXPORT_TIMEOUT_MS = 30000;
        private const int PHASE_CSV_TIMEOUT_MS = 60000;

        private const byte FRAME_H1 = 0xAA;
        private const byte FRAME_H2 = 0x55;
        private const byte FRAME_F1 = 0x55;
        private const byte FRAME_F2 = 0xAA;

        // GET response: KEY=VALUE pairs (matches main.c GET: LINE/DATE/TIME optional)
        private static readonly Regex _getResponseRegex = new Regex(
            @"RPM=(\S+)\s+SLIP=(\S+)\s+BPFO=(\S+)\s+BPFI=(\S+)\s+FTF=(\S+)\s+BSF=(\S+)\s+FTH=(\S+)\s+WTH=(\S+)" +
            @"(?:\s+LINE=(\S+))?(?:\s+DATE=(\S+))?(?:\s+TIME=(\S+))?",
            RegexOptions.Compiled);

        // Stator line
        private static readonly Regex _statorLineRegex = new Regex(
            @"^ST\s+f=(\S+)\s+SH=(\S+)\s+GD=(\S+)\s+NSR=(\S+)\s+ZSR=(\S+)\s+H=(\S+)\s+R=(\S+)\s+Z3=(\S+)\s+IMB=(\S+)%?\s+LV=(\S+)",
            RegexOptions.Compiled);

        // Latest telemetry for stator line attachment
        private TelemetryData _lastTelemetry;

        public event Action<TelemetryData> OnTelemetryReceived;
        public event Action<string> OnLogMessage;
        public event Action<bool> OnConnectionChanged;
        /// <summary>
        /// Fired for every complete text line received from the STM32.
        /// Used by Form1 to feed lines into STM32ReportParser for spectral CSV.
        /// </summary>
        public event Action<string> OnLineReceived;

        public bool IsConnected
        {
            get { return _port != null && _port.IsOpen; }
        }

        // =====================================================================
        // ROBUST FLOAT PARSER
        // =====================================================================
        // The STM32's fmt_double() may produce non-standard output like
        //   "1u.1500" instead of "1500.000"
        // Strategy: strip non-numeric chars, then parse.
        // =====================================================================

        private static bool TryParseRobustFloat(string token, out float result)
        {
            result = 0f;
            if (string.IsNullOrEmpty(token)) return false;

            CultureInfo ci = CultureInfo.InvariantCulture;

            // 1. Direct parse
            if (float.TryParse(token, NumberStyles.Float, ci, out result))
                return true;

            // 2. Strip trailing non-numeric chars (Hz, %, etc.)
            string cleaned = token.TrimEnd('H', 'h', 'z', 'Z', '%', ' ', '\t');
            if (float.TryParse(cleaned, NumberStyles.Float, ci, out result))
                return true;

            // 3. Remove all non-numeric/non-dot/non-minus/non-e chars
            StringBuilder sb = new StringBuilder(cleaned.Length);
            bool hasDot = false;
            for (int i = 0; i < cleaned.Length; i++)
            {
                char c = cleaned[i];
                if (c >= '0' && c <= '9')
                    sb.Append(c);
                else if (c == '.' && !hasDot)
                { sb.Append(c); hasDot = true; }
                else if (c == '-' && i == 0)
                    sb.Append(c);
                else if ((c == 'e' || c == 'E') && i > 0 && i < cleaned.Length - 1)
                    sb.Append(c);
            }

            string stripped = sb.ToString();
            if (stripped.Length > 0 &&
                float.TryParse(stripped, NumberStyles.Float, ci, out result))
                return true;

            // 4. Regex fallback: extract first valid number
            Match m = Regex.Match(token, @"-?[\d]+\.?[\d]*([eE][+-]?[\d]+)?");
            if (m.Success &&
                float.TryParse(m.Value, NumberStyles.Float, ci, out result))
                return true;

            return false;
        }

        /// <summary>
        /// Extracts KEY=VALUE pairs from a space-separated string.
        /// </summary>
        private static Dictionary<string, string> ParseKeyValuePairs(string line)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(line)) return result;

            MatchCollection matches = Regex.Matches(line,
                @"([A-Za-z_][A-Za-z0-9_]*)=(\S+)");
            foreach (Match m in matches)
            {
                string key = m.Groups[1].Value.ToUpperInvariant();
                string val = m.Groups[2].Value;
                result[key] = val;
            }
            return result;
        }

        // =====================================================================
        // CONNECTION
        // =====================================================================

        public async Task<bool> Connect(string portName)
        {
            if (IsConnected) await Disconnect();

            try
            {
                _port = new SerialPort
                {
                    PortName = portName,
                    BaudRate = 115200,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    ReadBufferSize = 8192,
                    WriteBufferSize = 4096,
                    DtrEnable = true,
                    RtsEnable = false
                };

                _port.DataReceived += Port_DataReceived;
                _port.ErrorReceived += Port_ErrorReceived;
                _port.Open();

                await Task.Delay(500);

                // Flush all buffers
                lock (_rxBuffer) { _rxBuffer.Clear(); }
                lock (_rawTextLock) { _rawTextBuffer.Clear(); }
                lock (_linesLock) { _completedLines.Clear(); }
                if (_port.BytesToRead > 0) _port.DiscardInBuffer();

                bool pingOk = await PingText();
                Log(pingOk
                    ? "Connected to " + portName + " — PING OK"
                    : "Connected to " + portName + " — PING failed (check firmware)");

                OnConnectionChanged?.Invoke(true);
                return true;
            }
            catch (Exception ex)
            {
                Log("Connection failed: " + ex.Message);
                if (_port != null)
                {
                    _port.DataReceived -= Port_DataReceived;
                    _port.ErrorReceived -= Port_ErrorReceived;
                    try { _port.Close(); } catch { }
                    try { _port.Dispose(); } catch { }
                    _port = null;
                }
                return false;
            }
        }

        public async Task Disconnect()
        {
            try
            {
                _telemetryCts?.Cancel();
                await Task.Run(() =>
                {
                    lock (_portLock)
                    {
                        if (_port != null)
                        {
                            _port.DataReceived -= Port_DataReceived;
                            _port.ErrorReceived -= Port_ErrorReceived;
                            try { if (_port.IsOpen) _port.Close(); } catch { }
                            try { _port.Dispose(); } catch { }
                            _port = null;
                        }
                    }
                });
                lock (_rxBuffer) { _rxBuffer.Clear(); }
                lock (_rawTextLock) { _rawTextBuffer.Clear(); }
                lock (_linesLock) { _completedLines.Clear(); }
                OnConnectionChanged?.Invoke(false);
                Log("Disconnected");
            }
            catch (Exception ex)
            {
                Log("Disconnect error: " + ex.Message);
            }
        }

        // =====================================================================
        // RAW SERIAL I/O
        // =====================================================================

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                lock (_portLock)
                {
                    if (_port == null || !_port.IsOpen) return;
                    int count = _port.BytesToRead;
                    if (count <= 0) return;

                    byte[] buf = new byte[count];
                    int read = _port.Read(buf, 0, count);

                    // Binary buffer (for binary protocol)
                    lock (_rxBuffer)
                    {
                        for (int i = 0; i < read; i++)
                            _rxBuffer.Add(buf[i]);
                    }

                    // Text buffer — accumulate printable chars + newlines
                    lock (_rawTextLock)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            char c = (char)buf[i];
                            if ((c >= 0x20 && c <= 0x7E) || c == '\r' || c == '\n')
                                _rawTextBuffer.Append(c);
                        }
                    }
                }
            }
            catch { }
        }

        private void Port_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Log("Serial error: " + e.EventType.ToString());
        }

        private void SendRaw(byte[] data)
        {
            lock (_portLock)
            {
                if (_port == null || !_port.IsOpen)
                    throw new InvalidOperationException("Not connected");
                _port.Write(data, 0, data.Length);
            }
        }

        private void SendTextLine(string line)
        {
            byte[] data = Encoding.ASCII.GetBytes(line + "\r\n");
            SendRaw(data);
        }

        // =====================================================================
        // LINE EXTRACTION FROM RAW TEXT BUFFER
        // =====================================================================
        // Called by the telemetry loop. Moves complete lines from
        // _rawTextBuffer into _completedLines. Does NOT interpret or
        // discard anything — all lines are preserved for consumers.
        // =====================================================================

        private void ExtractCompletedLines()
        {
            lock (_rawTextLock)
            {
                if (_rawTextBuffer.Length == 0) return;

                string full = _rawTextBuffer.ToString();
                int lastNl = full.LastIndexOf('\n');
                if (lastNl < 0) return;

                // Extract everything up to and including the last newline
                string extractable = full.Substring(0, lastNl + 1);
                _rawTextBuffer.Remove(0, lastNl + 1);

                string[] lines = extractable.Split(
                    new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                lock (_linesLock)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string trimmed = lines[i].Trim();
                        if (trimmed.Length > 0)
                            _completedLines.Add(trimmed);
                    }
                }
            }
        }

        // =====================================================================
        // BINARY PACKET EXTRACTION
        // =====================================================================

        private async Task<byte[]> WaitForPacket(int timeoutMs, byte expectedCmd)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                byte[] packet = TryExtractPacket(expectedCmd);
                if (packet != null) return packet;
                await Task.Delay(5);
            }
            return null;
        }

        private byte[] TryExtractPacket(byte expectedCmd)
        {
            lock (_rxBuffer)
            {
                int maxAttempts = 10;
                int attempt = 0;

                while (_rxBuffer.Count >= 6 && attempt < maxAttempts)
                {
                    attempt++;

                    int hdrIdx = -1;
                    for (int i = 0; i < _rxBuffer.Count - 1; i++)
                    {
                        if (_rxBuffer[i] == FRAME_H1 && _rxBuffer[i + 1] == FRAME_H2)
                        { hdrIdx = i; break; }
                    }
                    if (hdrIdx < 0) { _rxBuffer.Clear(); return null; }
                    if (hdrIdx > 0) _rxBuffer.RemoveRange(0, hdrIdx);

                    int ftrIdx = -1;
                    for (int i = 3; i < _rxBuffer.Count - 1; i++)
                    {
                        if (_rxBuffer[i] == FRAME_F1 && _rxBuffer[i + 1] == FRAME_F2)
                        { ftrIdx = i; break; }
                    }
                    if (ftrIdx < 0) return null;

                    int payloadLen = ftrIdx - 2;
                    byte[] payload = new byte[payloadLen];
                    for (int i = 0; i < payloadLen; i++)
                        payload[i] = _rxBuffer[2 + i];

                    _rxBuffer.RemoveRange(0, ftrIdx + 2);

                    if (payload.Length > 0 && payload[0] == expectedCmd)
                        return payload;
                }
                return null;
            }
        }

        // =====================================================================
        // TEXT RESPONSE WAITING
        // =====================================================================
        // FIX: WaitForTextResponse now reads directly from _completedLines.
        // While _commandPending is true, the telemetry loop leaves lines
        // untouched so the command handler gets first pick.
        // =====================================================================

        /// <summary>
        /// Waits for a single text response line that looks like a command
        /// response (starts with "OK", "ERR", or contains key=value data).
        /// Skips telemetry lines (BPFO=...Hz) that may arrive concurrently.
        /// </summary>
        private async Task<string> WaitForTextResponse(int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                // Ensure raw buffer is flushed into line queue
                ExtractCompletedLines();

                lock (_linesLock)
                {
                    for (int i = 0; i < _completedLines.Count; i++)
                    {
                        string line = _completedLines[i];

                        // Skip auto-sent telemetry lines (bearing + stator)
                        // These arrive continuously from USB_Send_FaultSummary()
                        if (IsTelemetryLine(line) || IsStatorLine(line) ||
                            IsPfLine(line) ||
                            IsSpectralLine(line))
                            continue;

                        // This is a command response — take it
                        _completedLines.RemoveAt(i);
                        return line;
                    }
                }

                await Task.Delay(10);
            }
            return null;
        }

        /// <summary>
        /// Waits for a multi-line USB export. Streams each line to OnLineReceived via
        /// ProcessSingleLineForTelemetry (same as the background telemetry loop).
        /// </summary>
        private async Task<List<string>> WaitForMultiLineResponse(
            int timeoutMs,
            UsbMultiLineEndMode endMode,
            int idleMs = 300)
        {
            List<string> result = new List<string>();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            DateTime lastReceived = DateTime.UtcNow;

            while (DateTime.UtcNow < deadline)
            {
                ExtractCompletedLines();

                bool gotLine = false;
                lock (_linesLock)
                {
                    while (_completedLines.Count > 0)
                    {
                        string line = _completedLines[0];
                        _completedLines.RemoveAt(0);

                        if (IsTelemetryLine(line) || IsStatorLine(line) || IsPfLine(line))
                        {
                            ProcessSingleLineForTelemetry(line);
                            continue;
                        }

                        ProcessSingleLineForTelemetry(line);
                        result.Add(line);
                        gotLine = true;
                    }
                }

                if (gotLine)
                {
                    lastReceived = DateTime.UtcNow;
                    if (result.Count > 0 && IsMultiLineTransferComplete(result[result.Count - 1], endMode))
                        break;
                }
                else if (result.Count > 0 &&
                         (DateTime.UtcNow - lastReceived).TotalMilliseconds > idleMs)
                {
                    break;
                }

                await Task.Delay(10);
            }
            return result;
        }

        private static bool IsMultiLineTransferComplete(string line, UsbMultiLineEndMode endMode)
        {
            if (string.IsNullOrEmpty(line)) return false;

            switch (endMode)
            {
                case UsbMultiLineEndMode.FullReport:
                    return line.Contains("### END_FULL_REPORT");
                case UsbMultiLineEndMode.GraphData:
                    return line.Contains("### END_GRAPHDATA");
                case UsbMultiLineEndMode.PhaseCsv:
                    return line.Contains("### END_PHASE_CSV");
                case UsbMultiLineEndMode.SpectralReport:
                    return line.Contains("### END_REPORT");
                case UsbMultiLineEndMode.TechniqueExport:
                default:
                    return line.Contains("### END_EXPORT");
            }
        }

        // =====================================================================
        // LINE CLASSIFICATION HELPERS
        // =====================================================================

        /// <summary>
        /// Returns true if the line is a bearing telemetry line
        /// (auto-sent by USB_Send_FaultSummary line 1).
        /// Pattern: starts with "BPFO=" and contains "FI=" and "LV="
        /// </summary>
        private static bool IsTelemetryLine(string line)
        {
            return line.StartsWith("BPFO=") && line.Contains("FI=") && line.Contains("LV=");
        }

        /// <summary>
        /// Returns true if the line is a stator telemetry line
        /// (auto-sent by USB_Send_FaultSummary lines 2-3).
        /// Pattern: starts with "ST " or "ST2 "
        /// </summary>
        private static bool IsStatorLine(string line)
        {
            return line.StartsWith("ST ") || line.StartsWith("ST2 ");
        }

        private static bool IsPfLine(string line)
        {
            return line.StartsWith("PF ") && line.Contains("DOM=");
        }

        /// <summary>
        /// Returns true if the line is part of the spectral report
        /// (auto-sent when USB_FULL_SPECTRAL_EXPORT=1).
        /// </summary>
        private static bool IsSpectralLine(string line)
        {
            if (line.StartsWith("[FOURIER_CSV]") ||
                line.StartsWith("[MUSIC_CSV]") ||
                line.StartsWith("[CYCLIC2_CSV]") ||
                line.StartsWith("[SK_CSV]") ||
                line.StartsWith("[META]") ||
                line.StartsWith("[MODEL_IDX]") ||
                line.StartsWith("[PARAMS]") ||
                line.StartsWith("[BEARING]") ||
                line.StartsWith("[STATOR]") ||
                line.StartsWith("[ESPRIT_HZ]") ||
                line.StartsWith("[ESPRIT_CSV]") ||
                line.StartsWith("[ESPRIT_TSV]") ||
                line.StartsWith("[MUSIC_EVAL_DESC]") ||
                line.StartsWith("[WAVELET_META]") ||
                line.StartsWith("[WAVELET_CSV]") ||
                line.StartsWith("### H750_DSP") ||
                line.StartsWith("### H750_FULL_REPORT") ||
                line.StartsWith("### BEGIN_GRAPHDATA") ||
                line.StartsWith("### END_GRAPHDATA") ||
                line.StartsWith("### END_FULL_REPORT") ||
                line.StartsWith("### SECTION") ||
                line.StartsWith("### PROGRESS") ||
                line.StartsWith("### END_REPORT") ||
                line.StartsWith("### END_EXPORT") ||
                line.StartsWith("SIGNAL=") ||
                line.StartsWith("FOURIER=") ||
                line.StartsWith("CYCLIC2=") ||
                line.StartsWith("T_MS="))
                return true;

            // PHASE3 / PHASECSV bulk export (main.c USB_Send_PhaseCsv)
            if (line.StartsWith("### H750_PHASE_CSV") ||
                line.StartsWith("### END_PHASE_CSV") ||
                line.StartsWith("[PHASE_") ||
                line.StartsWith("FS_HZ=") ||
                line.StartsWith("sample_idx,"))
                return true;
            if (line.Length > 4 && line[0] >= '0' && line[0] <= '9' &&
                line.Contains(",") && !line.Contains("="))
                return true;

            return false;
        }

        // =====================================================================
        // BINARY COMMAND HELPERS
        // =====================================================================

        private async Task<bool> SendFloatCommand(byte cmd, float value)
        {
            byte[] payload = new byte[5];
            payload[0] = cmd;
            byte[] fb = BitConverter.GetBytes(value);
            payload[1] = fb[0]; payload[2] = fb[1];
            payload[3] = fb[2]; payload[4] = fb[3];
            return await SendAndWaitAck(cmd, payload, CMD_TIMEOUT_MS);
        }

        private async Task<bool> SendSimpleCommand(byte cmd, int timeout)
        {
            return await SendAndWaitAck(cmd, new byte[] { cmd }, timeout);
        }

        private async Task<bool> SendAndWaitAck(byte expectedCmd,
            byte[] txPayload, int timeout)
        {
            try
            {
                lock (_rxBuffer) { _rxBuffer.Clear(); }
                SendRaw(txPayload);

                byte[] response = await WaitForPacket(timeout, expectedCmd);
                if (response == null)
                {
                    Log("Timeout ACK 0x" + expectedCmd.ToString("X2"));
                    return false;
                }
                if (response.Length >= 2 &&
                    response[0] == expectedCmd && response[1] == 0x01)
                    return true;

                if (response.Length >= 2)
                    Log("ACK 0x" + expectedCmd.ToString("X2") +
                        " status=0x" + response[1].ToString("X2"));
                return false;
            }
            catch (Exception ex)
            {
                Log("Cmd 0x" + expectedCmd.ToString("X2") + ": " + ex.Message);
                return false;
            }
        }

        private async Task<byte[]> SendAndWaitResponse(byte cmd, int timeout)
        {
            try
            {
                lock (_rxBuffer) { _rxBuffer.Clear(); }
                SendRaw(new byte[] { cmd });
                return await WaitForPacket(timeout, cmd);
            }
            catch (Exception ex)
            {
                Log("Cmd 0x" + cmd.ToString("X2") + ": " + ex.Message);
                return null;
            }
        }

        // =====================================================================
        // TEXT COMMAND HELPER
        // =====================================================================
        // FIX: Sets _commandPending BEFORE clearing buffers and sending,
        // so there is no window where the telemetry loop can steal the
        // response. _commandPending tells the telemetry loop to stop
        // consuming lines.
        // =====================================================================

        private async Task<string> SendTextCommand(string command, int timeoutMs)
        {
            try
            {
                _commandPending = true;

                // Small delay to let the telemetry loop finish its current cycle
                await Task.Delay(15);

                // Flush any pending raw text into the line queue,
                // then drain telemetry lines that arrived before our command
                ExtractCompletedLines();
                DrainTelemetryLines();

                SendTextLine(command);
                Log("TX> " + command);

                string response = await WaitForTextResponse(timeoutMs);

                _commandPending = false;

                if (response != null)
                    Log("RX< " + response);
                else
                    Log("RX< (timeout after " + timeoutMs + "ms)");

                return response;
            }
            catch (Exception ex)
            {
                _commandPending = false;
                Log("Text cmd '" + command + "': " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Removes telemetry/stator/spectral lines from _completedLines
        /// so they don't pollute the command response. These lines arrived
        /// before our command was sent.
        /// </summary>
        private void DrainTelemetryLines()
        {
            lock (_linesLock)
            {
                // Process these lines for telemetry before removing
                for (int i = _completedLines.Count - 1; i >= 0; i--)
                {
                    string line = _completedLines[i];
                    if (IsTelemetryLine(line) || IsStatorLine(line) ||
                        IsPfLine(line) ||
                        IsSpectralLine(line))
                    {
                        // Fire events for these lines before draining
                        ProcessSingleLineForTelemetry(line);
                        _completedLines.RemoveAt(i);
                    }
                }
            }
        }

        // =====================================================================
        // PUBLIC API — BINARY
        // =====================================================================

        public async Task<bool> Ping()
        {
            return await SendSimpleCommand(STM32Commands.CMD_PING, PING_TIMEOUT_MS);
        }

        public async Task<bool> SetMotorRPM(float v) =>
            await SendFloatCommand(STM32Commands.CMD_SET_RPM, v);
        public async Task<bool> SetMotorSlip(float v) =>
            await SendFloatCommand(STM32Commands.CMD_SET_SLIP, v);
        public async Task<bool> SetBPFO(float v) =>
            await SendFloatCommand(STM32Commands.CMD_SET_BPFO, v);
        public async Task<bool> SetBPFI(float v) =>
            await SendFloatCommand(STM32Commands.CMD_SET_BPFI, v);
        public async Task<bool> SetFTF(float v) =>
            await SendFloatCommand(STM32Commands.CMD_SET_FTF, v);
        public async Task<bool> SetBSF(float v) =>
            await SendFloatCommand(STM32Commands.CMD_SET_BSF, v);
        public async Task<bool> SetFaultThreshold(float v) =>
            await SendFloatCommand(STM32Commands.CMD_SET_FAULT_THRESH, v);
        public async Task<bool> SetWarningThreshold(float v) =>
            await SendFloatCommand(STM32Commands.CMD_SET_WARNING_THRESH, v);

        public async Task<int> SendAllParameters(MotorParameters p)
        {
            int ok = 0;
            if (await SetMotorRPM(p.MotorRPM)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetMotorSlip(p.Slip)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetBPFO(p.BPFO)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetBPFI(p.BPFI)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetFTF(p.FTF)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetBSF(p.BSF)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetFaultThreshold(p.FaultThreshold)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetWarningThreshold(p.WarningThreshold)) ok++;
            return ok;
        }

        public async Task<MotorParameters> GetAllParameters()
        {
            byte[] response = await SendAndWaitResponse(
                STM32Commands.CMD_GET_ALL_PARAMS, CMD_TIMEOUT_MS);
            if (response == null)
            {
                Log("GetAllParameters binary: no response — " +
                    "CMD 0x80 may be mis-framed; use text GET protocol");
                return null;
            }
            if (response.Length < 33)
            {
                Log("GetAllParameters: too short (" + response.Length + ")");
                return null;
            }
            try
            {
                int idx = 1;
                MotorParameters p = new MotorParameters();
                p.MotorRPM = BitConverter.ToSingle(response, idx); idx += 4;
                p.Slip = BitConverter.ToSingle(response, idx); idx += 4;
                p.BPFO = BitConverter.ToSingle(response, idx); idx += 4;
                p.BPFI = BitConverter.ToSingle(response, idx); idx += 4;
                p.FTF = BitConverter.ToSingle(response, idx); idx += 4;
                p.BSF = BitConverter.ToSingle(response, idx); idx += 4;
                p.FaultThreshold = BitConverter.ToSingle(response, idx); idx += 4;
                p.WarningThreshold = BitConverter.ToSingle(response, idx); idx += 4;
                return p;
            }
            catch (Exception ex) { Log("GetAllParameters: " + ex.Message); return null; }
        }

        public async Task<bool> SaveToFlash() =>
            await SendSimpleCommand(STM32Commands.CMD_SAVE_TO_FLASH, FLASH_TIMEOUT_MS);
        public async Task<bool> LoadFromFlash() =>
            await SendSimpleCommand(STM32Commands.CMD_LOAD_FROM_FLASH, FLASH_TIMEOUT_MS);
        public async Task<bool> ResetToDefault() =>
            await SendSimpleCommand(STM32Commands.CMD_RESET_TO_DEFAULT, CMD_TIMEOUT_MS);
        public async Task<bool> SaveAdcSnapshot() =>
            await SendSimpleCommand(STM32Commands.CMD_SAVE_ADC_TO_QSPI, FLASH_TIMEOUT_MS);

        // =====================================================================
        // PUBLIC API — TEXT PROTOCOL
        // =====================================================================

        public async Task<bool> PingText()
        {
            string r = await SendTextCommand("PING", PING_TIMEOUT_MS);
            return r != null && r.StartsWith("OK");
        }

        public async Task<MotorParameters> GetAllParametersText()
        {
            string resp = await SendTextCommand("GET", CMD_TIMEOUT_MS);
            if (resp == null) { Log("GetText: no response"); return null; }
            return ParseGetResponse(resp);
        }

        /// <summary>
        /// Parses the GET response using key=value extraction with robust
        /// float parsing to handle fmt_double output anomalies.
        /// </summary>
        private MotorParameters ParseGetResponse(string resp)
        {
            Log("GetText raw: [" + resp + "]");

            // Try regex first
            Match m = _getResponseRegex.Match(resp);
            if (m.Success)
            {
                MotorParameters p = new MotorParameters();
                float v;
                if (TryParseRobustFloat(m.Groups[1].Value, out v)) p.MotorRPM = v;
                if (TryParseRobustFloat(m.Groups[2].Value, out v)) p.Slip = v;
                if (TryParseRobustFloat(m.Groups[3].Value, out v)) p.BPFO = v;
                if (TryParseRobustFloat(m.Groups[4].Value, out v)) p.BPFI = v;
                if (TryParseRobustFloat(m.Groups[5].Value, out v)) p.FTF = v;
                if (TryParseRobustFloat(m.Groups[6].Value, out v)) p.BSF = v;
                if (TryParseRobustFloat(m.Groups[7].Value, out v)) p.FaultThreshold = v;
                if (TryParseRobustFloat(m.Groups[8].Value, out v)) p.WarningThreshold = v;
                if (m.Groups.Count > 9 && m.Groups[9].Success &&
                    TryParseRobustFloat(m.Groups[9].Value, out v))
                    p.SupplyLineHz = v;
                if (m.Groups.Count > 10 && m.Groups[10].Success)
                    p.ClockDate = m.Groups[10].Value;
                if (m.Groups.Count > 11 && m.Groups[11].Success)
                    p.ClockTime = m.Groups[11].Value;
                return p;
            }

            // Fallback: manual key=value extraction
            Log("GetText: regex didn't match, trying key=value parse");
            Dictionary<string, string> kv = ParseKeyValuePairs(resp);
            if (kv.Count == 0)
            {
                Log("GetText: no key=value pairs found in response");
                return null;
            }

            MotorParameters pm = new MotorParameters();
            float fv;

            if (kv.ContainsKey("RPM") && TryParseRobustFloat(kv["RPM"], out fv))
                pm.MotorRPM = fv;
            if (kv.ContainsKey("SLIP") && TryParseRobustFloat(kv["SLIP"], out fv))
                pm.Slip = fv;
            if (kv.ContainsKey("BPFO") && TryParseRobustFloat(kv["BPFO"], out fv))
                pm.BPFO = fv;
            if (kv.ContainsKey("BPFI") && TryParseRobustFloat(kv["BPFI"], out fv))
                pm.BPFI = fv;
            if (kv.ContainsKey("FTF") && TryParseRobustFloat(kv["FTF"], out fv))
                pm.FTF = fv;
            if (kv.ContainsKey("BSF") && TryParseRobustFloat(kv["BSF"], out fv))
                pm.BSF = fv;
            if (kv.ContainsKey("FTH") && TryParseRobustFloat(kv["FTH"], out fv))
                pm.FaultThreshold = fv;
            if (kv.ContainsKey("WTH") && TryParseRobustFloat(kv["WTH"], out fv))
                pm.WarningThreshold = fv;
            if (kv.ContainsKey("LINE") && TryParseRobustFloat(kv["LINE"], out fv))
                pm.SupplyLineHz = fv;
            if (kv.ContainsKey("DATE"))
                pm.ClockDate = kv["DATE"];
            if (kv.ContainsKey("TIME"))
                pm.ClockTime = kv["TIME"];

            return pm;
        }

        public async Task<bool> SetParameterText(string key, float value)
        {
            string cmd = key.ToUpperInvariant() + "=" +
                value.ToString("G", CultureInfo.InvariantCulture);
            string r = await SendTextCommand(cmd, CMD_TIMEOUT_MS);
            return r != null && r.StartsWith("OK");
        }

        public async Task<int> SendAllParametersText(MotorParameters p)
        {
            int ok = 0;
            if (await SetParameterText("RPM", p.MotorRPM)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetParameterText("SLIP", p.Slip)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetParameterText("BPFO", p.BPFO)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetParameterText("BPFI", p.BPFI)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetParameterText("FTF", p.FTF)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetParameterText("BSF", p.BSF)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetParameterText("FTH", p.FaultThreshold)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            if (await SetParameterText("WTH", p.WarningThreshold)) ok++;
            await Task.Delay(INTER_CMD_DELAY);
            // main.c: LINE= validated (1..400 Hz); round-trip after GET
            if (p.SupplyLineHz > 1.0f && p.SupplyLineHz < 400.0f)
            {
                if (await SetParameterText("LINE", p.SupplyLineHz)) ok++;
            }
            return ok;
        }

        public async Task<bool> SaveToFlashText()
        {
            string r = await SendTextCommand("SAVE", FLASH_TIMEOUT_MS);
            return r != null && r.StartsWith("OK");
        }

        public async Task<bool> LoadFromFlashText()
        {
            string r = await SendTextCommand("LOAD", FLASH_TIMEOUT_MS);
            return r != null && r.StartsWith("OK");
        }

        public async Task<bool> ResetToDefaultText()
        {
            string r = await SendTextCommand("DEFAULT", CMD_TIMEOUT_MS);
            return r != null && r.StartsWith("OK");
        }

        public async Task<bool> SaveAdcSnapshotText()
        {
            string r = await SendTextCommand("SAVEADC", FLASH_TIMEOUT_MS);
            return r != null && r.StartsWith("OK");
        }

        public async Task<string> SendRawTextCommand(string command)
        {
            return await SendTextCommand(command, CMD_TIMEOUT_MS);
        }

        /// <summary>
        /// REPORT or FULLREPORT — main.c USB_Send_FullReport(); ends with ### END_FULL_REPORT.
        /// Intermediate ### END_REPORT (spectral) does not end the wait.
        /// </summary>
        public Task<List<string>> RequestReport()
        {
            return RequestMultiLineExport(STM32TextCommands.Report);
        }

        public Task<List<string>> RequestFullReport()
        {
            return RequestMultiLineExport(STM32TextCommands.FullReport);
        }

        public Task<List<string>> RequestGraphData()
        {
            return RequestMultiLineExport(
                STM32TextCommands.GraphData,
                GRAPHDATA_TIMEOUT_MS,
                UsbMultiLineEndMode.GraphData,
                1500);
        }

        public Task<List<string>> RequestPhaseCsv()
        {
            return RequestMultiLineExport(
                STM32TextCommands.PhaseCsv,
                PHASE_CSV_TIMEOUT_MS,
                UsbMultiLineEndMode.PhaseCsv,
                1500);
        }

        public Task<List<string>> RequestTechniqueCsv(string command)
        {
            return RequestMultiLineExport(
                command,
                TECHNIQUE_EXPORT_TIMEOUT_MS,
                UsbMultiLineEndMode.TechniqueExport,
                500);
        }

        public Task<List<string>> RequestSkCsv()
        {
            return RequestTechniqueCsv(STM32TextCommands.SkCsv);
        }

        public Task<List<string>> RequestWaveletCsv()
        {
            return RequestTechniqueCsv(STM32TextCommands.WaveletCsv);
        }

        public async Task<string> RequestHelp()
        {
            return await SendTextCommand(STM32TextCommands.Help, CMD_TIMEOUT_MS);
        }

        public async Task<string> SendBaselineTextCommand(string command)
        {
            return await SendTextCommand(command, FLASH_TIMEOUT_MS);
        }

        private async Task<List<string>> RequestMultiLineExport(
            string command,
            int timeoutMs = FULL_REPORT_TIMEOUT_MS,
            UsbMultiLineEndMode endMode = UsbMultiLineEndMode.FullReport,
            int idleMs = 2000)
        {
            try
            {
                _commandPending = true;
                await Task.Delay(15);
                ExtractCompletedLines();
                DrainTelemetryLines();

                SendTextLine(command);
                Log("TX> " + command);

                List<string> lines = await WaitForMultiLineResponse(timeoutMs, endMode, idleMs);

                _commandPending = false;
                Log(command + ": received " + lines.Count + " lines");
                return lines;
            }
            catch (Exception ex)
            {
                _commandPending = false;
                Log(command + ": " + ex.Message);
                return new List<string>();
            }
        }

        // =====================================================================
        // TELEMETRY PROCESSING
        // =====================================================================
        // FIX: The telemetry loop now only processes lines when no command
        // is pending. This eliminates the race condition where the loop
        // would consume command responses.
        // =====================================================================

        /// <summary>
        /// Processes a single line for telemetry/events (used during drain).
        /// </summary>
        private void ProcessSingleLineForTelemetry(string line)
        {
            // Fire OnLineReceived for spectral CSV parsing in Form1
            OnLineReceived?.Invoke(line);

            if (IsTelemetryLine(line))
            {
                TelemetryData t = ParseTelemetryLine(line);
                if (t != null)
                {
                    _lastTelemetry = t;
                    OnTelemetryReceived?.Invoke(t);
                }
            }
            else if (IsStatorLine(line) && _lastTelemetry != null)
            {
                ParseStatorLine(line, _lastTelemetry);
                OnTelemetryReceived?.Invoke(_lastTelemetry);
            }
            else if (IsPfLine(line) && _lastTelemetry != null)
            {
                Dictionary<string, string> kv = ParseKeyValuePairs(line);
                float v;
                byte bv;

                if (kv.ContainsKey("O") && TryParseRobustFloat(kv["O"], out v))
                    _lastTelemetry.Index_Bpfo = v;
                if (kv.ContainsKey("I") && TryParseRobustFloat(kv["I"], out v))
                    _lastTelemetry.Index_Bpfi = v;
                if (kv.ContainsKey("B") && TryParseRobustFloat(kv["B"], out v))
                    _lastTelemetry.Index_Bsf = v;
                if (kv.ContainsKey("T") && TryParseRobustFloat(kv["T"], out v))
                    _lastTelemetry.Index_Ftf = v;
                if (kv.ContainsKey("EMA") && TryParseRobustFloat(kv["EMA"], out v))
                    _lastTelemetry.FaultIndex_Ema = v;
                if ((kv.ContainsKey("CU") && TryParseRobustFloat(kv["CU"], out v)) ||
                    (kv.ContainsKey("CUSUM") && TryParseRobustFloat(kv["CUSUM"], out v)))
                    _lastTelemetry.CusumScore = v;
                if (kv.ContainsKey("DOM") && byte.TryParse(kv["DOM"], out bv))
                    _lastTelemetry.DominantFault = bv;

                TryParseTemperature(kv, _lastTelemetry);
                OnTelemetryReceived?.Invoke(_lastTelemetry);
            }
        }

        /// <summary>
        /// Main telemetry processing — called by the background loop.
        /// Only consumes lines when no command is pending.
        /// </summary>
        private void ProcessTelemetryFromQueue()
        {
            // Don't consume lines while a command is waiting for a response
            if (_commandPending) return;

            ExtractCompletedLines();

            lock (_linesLock)
            {
                while (_completedLines.Count > 0)
                {
                    string line = _completedLines[0];
                    _completedLines.RemoveAt(0);

                    ProcessSingleLineForTelemetry(line);
                }
            }
        }

        /// <summary>
        /// Parses a bearing telemetry line (with robust float handling).
        /// </summary>
        private TelemetryData ParseTelemetryLine(string line)
        {
            if (!IsTelemetryLine(line)) return null;

            Dictionary<string, string> kv = ParseKeyValuePairs(line);
            if (kv.Count < 4) return null;

            TelemetryData t = new TelemetryData();
            t.Timestamp = DateTime.Now;

            float v;
            if (kv.ContainsKey("BPFO") && TryParseRobustFloat(kv["BPFO"], out v))
                t.BPFO_Hz = v;
            if (kv.ContainsKey("BPFI") && TryParseRobustFloat(kv["BPFI"], out v))
                t.BPFI_Hz = v;
            if (kv.ContainsKey("BSF") && TryParseRobustFloat(kv["BSF"], out v))
                t.BSF_Hz = v;
            if (kv.ContainsKey("FTF") && TryParseRobustFloat(kv["FTF"], out v))
                t.FTF_Hz = v;
            if (kv.ContainsKey("FI") && TryParseRobustFloat(kv["FI"], out v))
                t.FaultIndex = v;

            byte bv;
            if (kv.ContainsKey("LV") && byte.TryParse(kv["LV"], out bv))
                t.FaultLevel = bv;

            // Extended indices
            if (kv.ContainsKey("LS") && TryParseRobustFloat(kv["LS"], out v))
                t.Index_LS = v;
            if (kv.ContainsKey("MI") && TryParseRobustFloat(kv["MI"], out v))
                t.Index_Music = v;
            if (kv.ContainsKey("ES") && TryParseRobustFloat(kv["ES"], out v))
                t.Index_Esprit = v;
            if (kv.ContainsKey("TK") && TryParseRobustFloat(kv["TK"], out v))
                t.Index_Teager = v;
            if (kv.ContainsKey("SK") && TryParseRobustFloat(kv["SK"], out v))
                t.Index_SK = v;
            if (kv.ContainsKey("WV") && TryParseRobustFloat(kv["WV"], out v))
                t.Index_Wavelet = v;
            if (kv.ContainsKey("CY") && TryParseRobustFloat(kv["CY"], out v))
                t.Index_Cyclic = v;
            if (kv.ContainsKey("SB") && TryParseRobustFloat(kv["SB"], out v))
                t.Index_Sideband = v;
            if (kv.ContainsKey("ACF") && TryParseRobustFloat(kv["ACF"], out v))
                t.Index_EnvAcf = v;
            if (kv.ContainsKey("EMA") && TryParseRobustFloat(kv["EMA"], out v))
                t.FaultIndex_Ema = v;
            if ((kv.ContainsKey("CU") && TryParseRobustFloat(kv["CU"], out v)) ||
                (kv.ContainsKey("CUSUM") && TryParseRobustFloat(kv["CUSUM"], out v)))
                t.CusumScore = v;
            if (kv.ContainsKey("PF_O") && TryParseRobustFloat(kv["PF_O"], out v))
                t.Index_Bpfo = v;
            if (kv.ContainsKey("PF_I") && TryParseRobustFloat(kv["PF_I"], out v))
                t.Index_Bpfi = v;
            if (kv.ContainsKey("PF_B") && TryParseRobustFloat(kv["PF_B"], out v))
                t.Index_Bsf = v;
            if (kv.ContainsKey("PF_T") && TryParseRobustFloat(kv["PF_T"], out v))
                t.Index_Ftf = v;

            int iv;
            if (kv.ContainsKey("KB") && int.TryParse(kv["KB"], out iv))
                t.KurtBandHz = iv;
            else if (kv.ContainsKey("KB_HZ") && int.TryParse(kv["KB_HZ"], out iv))
                t.KurtBandHz = iv;

            if (kv.ContainsKey("DOM") && byte.TryParse(kv["DOM"], out bv))
                t.DominantFault = bv;

            if (kv.ContainsKey("SKPK"))
            {
                string skpk = kv["SKPK"];
                int at = skpk.IndexOf('@');
                if (at > 0)
                {
                    if (TryParseRobustFloat(skpk.Substring(0, at), out v))
                        t.SkPeak = v;
                    string freqPart = skpk.Substring(at + 1);
                    if (TryParseRobustFloat(freqPart, out v))
                        t.SkPeakHz = v;
                }
                else if (TryParseRobustFloat(skpk, out v))
                {
                    t.SkPeak = v;
                }
            }
            if (kv.ContainsKey("SKPK_HZ") && TryParseRobustFloat(kv["SKPK_HZ"], out v))
                t.SkPeakHz = v;

            TryParseTemperature(kv, t);

            return t;
        }

        /// <summary>
        /// Parses stator winding telemetry.
        /// </summary>
        private void ParseStatorLine(string line, TelemetryData target)
        {
            if (target == null) return;

            Dictionary<string, string> kv = ParseKeyValuePairs(line);
            float fv;
            int iv;

            if (kv.ContainsKey("F") && TryParseRobustFloat(kv["F"], out fv))
                target.Stator_FrequencyHz = fv;
            if (kv.ContainsKey("SH") && int.TryParse(kv["SH"], out iv))
                target.Stator_ShortLevel = iv;
            if (kv.ContainsKey("GD") && int.TryParse(kv["GD"], out iv))
                target.Stator_GndLevel = iv;
            if (kv.ContainsKey("NSR") && TryParseRobustFloat(kv["NSR"], out fv))
                target.Stator_NSR = fv;
            if (kv.ContainsKey("ZSR") && TryParseRobustFloat(kv["ZSR"], out fv))
                target.Stator_ZSR = fv;
            if (kv.ContainsKey("H") && TryParseRobustFloat(kv["H"], out fv))
                target.Stator_HarmRatio = fv;
            if (kv.ContainsKey("R") && TryParseRobustFloat(kv["R"], out fv))
                target.Stator_ResidRatio = fv;
            if (kv.ContainsKey("Z3") && TryParseRobustFloat(kv["Z3"], out fv))
                target.Stator_ZSR_H3 = fv;
            if (kv.ContainsKey("IMB") && TryParseRobustFloat(kv["IMB"], out fv))
                target.Stator_Imbalance = fv;
            if (kv.ContainsKey("LV") && int.TryParse(kv["LV"], out iv))
                target.Stator_FaultLevel = iv;

            TryParseTemperature(kv, target);
        }

        private static void TryParseTemperature(Dictionary<string, string> kv, TelemetryData target)
        {
            if (target == null || kv == null) return;

            float fv;
            if ((kv.ContainsKey("TEMP") && TryParseRobustFloat(kv["TEMP"], out fv)) ||
                (kv.ContainsKey("TEMP_C") && TryParseRobustFloat(kv["TEMP_C"], out fv)) ||
                (kv.ContainsKey("TEMPC") && TryParseRobustFloat(kv["TEMPC"], out fv)) ||
                (kv.ContainsKey("TC") && TryParseRobustFloat(kv["TC"], out fv)))
            {
                target.TemperatureC = fv;
                target.HasTemperature = true;
            }
        }

        public void StartTelemetryMonitor()
        {
            if (_telemetryCts != null)
            { _telemetryCts.Cancel(); _telemetryCts.Dispose(); }
            _telemetryCts = new CancellationTokenSource();
            CancellationToken token = _telemetryCts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { ProcessTelemetryFromQueue(); } catch { }
                    await Task.Delay(25);
                }
            }, token);
        }

        public void StopTelemetryMonitor()
        {
            if (_telemetryCts != null)
            { _telemetryCts.Cancel(); _telemetryCts.Dispose(); _telemetryCts = null; }
        }

        // =====================================================================
        // UTILITIES
        // =====================================================================

        private void Log(string msg)
        {
            Debug.WriteLine("[STM32] " + msg);
            OnLogMessage?.Invoke(msg);
        }

        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopTelemetryMonitor();
            lock (_portLock)
            {
                if (_port != null)
                {
                    _port.DataReceived -= Port_DataReceived;
                    _port.ErrorReceived -= Port_ErrorReceived;
                    try { if (_port.IsOpen) _port.Close(); } catch { }
                    try { _port.Dispose(); } catch { }
                    _port = null;
                }
            }
        }
    }
}