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
using System.Management;

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
        public const byte CMD_SET_STREAM = 0x0C;

        public const byte CMD_GET_ALL_PARAMS = 0x80;
        public const byte CMD_SAVE_TO_FLASH = 0x30;
        public const byte CMD_LOAD_FROM_FLASH = 0x31;
        public const byte CMD_RESET_TO_DEFAULT = 0x32;
        /// <summary>ADC snapshot to SPI1 NOR (main.c CMD_SAVE_ADC_TO_SPI1 / SAVEADC text).</summary>
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
        /// <summary>main.c: READY/STATUS → OK READY PHASE= DSP= CAP=</summary>
        public const string Ready = "READY";
        public const string Get = "GET";
        /// <summary>main.c: GETCOEFF/GETCOEFFS → "COEFF BPFO=.. BPFI=.. BSF=.. FTF=..".</summary>
        public const string GetCoeff = "GETCOEFF";
        public const string Report = "REPORT";
        public const string FullReport = "FULLREPORT";
        public const string GraphData = "GRAPHDATA";
        /// <summary>Alias for GRAPHDATA (main.c USB_Process_TextLine).</summary>
        public const string Graphs = "GRAPHS";
        public const string Phase3 = "PHASE3";
        public const string PhaseCsv = "PHASECSV";
        public const string FftCsv = "FFTCSV";
        public const string MusicCsv = "MUSICCSV";
        public const string EspritCsv = "ESPRITCSV";
        public const string Cyclic2Csv = "CYCLIC2CSV";
        public const string SkCsv = "SKCSV";
        /// <summary>main.c: WELCHCSV → [WELCH_CSV] (Welch PSD, 50%-overlap Hamming segments).</summary>
        public const string WelchCsv = "WELCHCSV";
        /// <summary>main.c: COHCSV → [COH_CSV] (magnitude-squared coherence, 0..1).</summary>
        public const string CohCsv = "COHCSV";
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
        /// <summary>main.c: DATE yyyy-mm-dd (space-separated args, not DATE=).</summary>
        public const string DatePrefix = "DATE";
        /// <summary>main.c: TIME hh:mm or hh:mm:ss (space-separated args).</summary>
        public const string TimePrefix = "TIME";
        /// <summary>main.c: SLIPAUTO=0/1 (auto-slip estimation toggle). Ack: OK SLIPAUTO=0/1.</summary>
        public const string SlipAuto = "SLIPAUTO";
        /// <summary>main.c: STREAM=0/1 (enable continuous telemetry/spectral auto-stream). Ack: OK STREAM=0/1.</summary>
        public const string Stream = "STREAM";
    }

    /// <summary>Maps main.c USB_Process_TextLine command to short UI hint.</summary>
    public static class STM32CommandHints
    {
        public static string Get(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return "";
            switch (command.Trim().ToUpperInvariant())
            {
                case "PING": return "main.c → OK";
                case "GET": return "Read RPM, SLIP, BPFO… LINE, SLIPAUTO, DATE, TIME";
                case "GETCOEFF":
                case "GETCOEFFS": return "Read bearing coefficients (COEFF BPFO/BPFI/BSF/FTF)";
                case "SLIPAUTO": return "Auto-slip estimation toggle (SLIPAUTO=0/1)";
                case "SAVE": return "Params → QSPI flash (OK SAVE / ERR SAVE)";
                case "LOAD": return "Params from QSPI (OK LOAD / ERR LOAD)";
                case "DEFAULT": return "RAM defaults (OK DEFAULT)";
                case "SAVEADC": return "ADC snapshot on next capture → SPI1 NOR";
                case "CALIB": return "Healthy motor ~26 s → bearing baseline stats";
                case "SAVEBASE": return "Write bearing baseline to QSPI";
                case "LOADBASE": return "Load bearing baseline from QSPI";
                case "CLEARBASE": return "Clear bearing baseline + CUSUM";
                case "CALIBST": return "Healthy stator ~26 s → stator baseline";
                case "SAVESTST": return "Write stator baseline to QSPI";
                case "LOADSTST": return "Load stator baseline from QSPI";
                case "CLEARSTST": return "Clear stator baseline + CUSUM";
                case "REPORT":
                case "FULLREPORT": return "Full diagnostic export (END_FULL_REPORT)";
                case "GRAPHDATA":
                case "GRAPHS": return "Plot pack (END_GRAPHDATA)";
                case "PHASECSV":
                case "PHASE3": return "3-phase time CSV (END_PHASE_CSV)";
                case "WELCHCSV": return "Welch PSD spectrum (END_EXPORT)";
                case "COHCSV": return "3-phase coherence 0..1 (END_EXPORT)";
                default:
                    if (command.EndsWith("CSV", StringComparison.OrdinalIgnoreCase))
                        return "Technique export (END_EXPORT)";
                    return "main.c text command";
            }
        }
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

        /// <summary>Auto-slip estimation enabled; from GET text SLIPAUTO= (main.c).</summary>
        public bool SlipAuto { get; set; }

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
                "  Supply Line (Hz):  " + SupplyLineHz.ToString("F2") + "\r\n" +
                "  Slip Auto:         " + (SlipAuto ? "ON" : "OFF");
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
        public float Index_Welch { get; set; }
        public float Index_Coherence { get; set; }
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
        public float Stator_NsrTd { get; set; }
        public float Stator_ZsrTd { get; set; }
        public float Stator_NsrH5 { get; set; }
        public float Stator_OddHarm { get; set; }
        public float Stator_PhaseSpreadDeg { get; set; }
        public float Stator_ShortIndex { get; set; }
        public float Stator_GndIndex { get; set; }
        public float Stator_ShortIndexEma { get; set; }
        public float Stator_GndIndexEma { get; set; }
        public float Stator_CusumShort { get; set; }
        public float Stator_CusumGnd { get; set; }
        public int Stator_EarlyShort { get; set; }
        public int Stator_EarlyGnd { get; set; }
        public float Stator_I0Mag { get; set; }
        public float Stator_I1Mag { get; set; }
        public float Stator_I2Mag { get; set; }
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
    // BINARY FAULT FRAME — matches FaultBinaryFrame_t in main.c
    // =========================================================================
    // Wire format: [0xAA][0x55][0xF0][FaultBinaryFrame_t][0x55][0xAA]
    // All fields little-endian. sizeof(FaultBinaryFrame_t) = 277 bytes on wire
    // (after the 3-byte header, before the 2-byte footer).
    // CRC-16/CCITT covers all struct bytes before the crc16 field.
    // =========================================================================

    public class BinaryFaultFrame
    {
        // Header/footer constants matching main.c
        public const byte CMD_FAULT_BINARY_FRAME = 0xF0;
        public const int WIRE_HEADER_SIZE = 3;    // 0xAA 0x55 0xF0
        public const int WIRE_FOOTER_SIZE = 2;    // 0x55 0xAA

        // Frame field offsets (from start of struct, after 3-byte header)
        // Must exactly match the #pragma pack(push,1) struct in main.c
        public const int STRUCT_SIZE = 277;        // sizeof(FaultBinaryFrame_t) packed
        public const int WIRE_PACKET_SIZE = WIRE_HEADER_SIZE + STRUCT_SIZE + WIRE_FOOTER_SIZE; // 282

        // --- Parsed fields ---
        // Header
        public uint FrameId { get; set; }
        public uint TimestampMs { get; set; }
        public float Rpm { get; set; }
        public float Slip { get; set; }
        public float ShaftHz { get; set; }
        public float SupplyLineHz { get; set; }
        // Shaft / slip tracking
        public float SlipEstimated { get; set; }
        public byte SlipAutoValid { get; set; }
        public byte PolePairs { get; set; }
        // Bearing fault frequencies (Hz)
        public float BpfoHz { get; set; }
        public float BpfiHz { get; set; }
        public float BsfHz { get; set; }
        public float FtfHz { get; set; }
        // 12 MCSA detection method indices
        public float IndexLs { get; set; }
        public float IndexFft { get; set; }
        public float IndexMusic { get; set; }
        public float IndexEsprit { get; set; }
        public float IndexTeager { get; set; }
        public float IndexSk { get; set; }
        public float IndexWavelet { get; set; }
        public float IndexCyclic { get; set; }
        public float IndexSideband { get; set; }
        public float IndexEnvAcf { get; set; }
        public float IndexWelch { get; set; }
        public float IndexCoherence { get; set; }
        // Spectral kurtosis peak
        public float SkPeakKurtosis { get; set; }
        public float SkPeakFHz { get; set; }
        public ushort KurtBandFcHz { get; set; }
        // 4 Bearing partial-fault indices
        public float IndexBpfo { get; set; }
        public float IndexBpfi { get; set; }
        public float IndexBsf { get; set; }
        public float IndexFtf { get; set; }
        // Fused bearing scores
        public float FaultIndex { get; set; }
        public float FaultIndexEma { get; set; }
        public float CusumScore { get; set; }
        public float DetectConfidence { get; set; }
        public byte FaultLevel { get; set; }
        public byte DominantFault { get; set; }
        public byte NpAlarmVotes { get; set; }
        public byte NpAlarmFourier { get; set; }
        public byte NpAlarmMusic { get; set; }
        public byte NpAlarmEsprit { get; set; }
        // NP (Neyman-Pearson) amplitudes
        public float AmpFourierFault { get; set; }
        public float AmpFourierBase { get; set; }
        public float AmpMusicFault { get; set; }
        public float AmpMusicBase { get; set; }
        public float AmpEspritFault { get; set; }
        public float AmpEspritBase { get; set; }
        // NP F-test critical ratios
        public float NpGammaFourier { get; set; }
        public float NpGammaMusic { get; set; }
        public float NpGammaEsprit { get; set; }
        // Stator winding metrics — spectral symmetrical components
        public float StatorI0Mag { get; set; }
        public float StatorI1Mag { get; set; }
        public float StatorI2Mag { get; set; }
        public float StatorNsr { get; set; }
        public float StatorZsr { get; set; }
        public float StatorImbalancePct { get; set; }
        public float StatorHarmRatio { get; set; }
        public float StatorResidGndRatio { get; set; }
        public float StatorZsrH3 { get; set; }
        public float StatorNsrH5 { get; set; }
        // Stator winding metrics — time-domain symmetrical components
        public float StatorI0RmsTd { get; set; }
        public float StatorI1RmsTd { get; set; }
        public float StatorI2RmsTd { get; set; }
        public float StatorNsrTd { get; set; }
        public float StatorZsrTd { get; set; }
        // Stator winding metrics — composite indices
        public float StatorOddHarmIndex { get; set; }
        public float StatorPhaseSpreadDeg { get; set; }
        public float StatorShortIndex { get; set; }
        public float StatorGndIndex { get; set; }
        public float StatorShortIndexEma { get; set; }
        public float StatorGndIndexEma { get; set; }
        public float StatorCusumShort { get; set; }
        public float StatorCusumGnd { get; set; }
        // Stator alarm / incipient flags
        public byte StatorFaultLevel { get; set; }
        public byte StatorFaultShort { get; set; }
        public byte StatorFaultGnd { get; set; }
        public byte StatorEarlyShort { get; set; }
        public byte StatorEarlyGnd { get; set; }
        // CRC
        public ushort Crc16 { get; set; }
        public bool CrcValid { get; set; }

        /// <summary>
        /// Converts this binary frame into a TelemetryData object
        /// for compatibility with existing UI/display code.
        /// </summary>
        public TelemetryData ToTelemetryData()
        {
            var t = new TelemetryData
            {
                Timestamp = DateTime.Now,
                BPFO_Hz = BpfoHz,
                BPFI_Hz = BpfiHz,
                BSF_Hz = BsfHz,
                FTF_Hz = FtfHz,
                FaultIndex = FaultIndex,
                FaultLevel = FaultLevel,
                FaultIndex_Ema = FaultIndexEma,
                CusumScore = CusumScore,
                Index_LS = IndexLs,
                Index_Music = IndexMusic,
                Index_Esprit = IndexEsprit,
                Index_Teager = IndexTeager,
                Index_SK = IndexSk,
                Index_Wavelet = IndexWavelet,
                Index_Cyclic = IndexCyclic,
                Index_Sideband = IndexSideband,
                Index_EnvAcf = IndexEnvAcf,
                Index_Welch = IndexWelch,
                Index_Coherence = IndexCoherence,
                Index_Bpfo = IndexBpfo,
                Index_Bpfi = IndexBpfi,
                Index_Bsf = IndexBsf,
                Index_Ftf = IndexFtf,
                DominantFault = DominantFault,
                SkPeak = SkPeakKurtosis,
                SkPeakHz = SkPeakFHz,
                KurtBandHz = KurtBandFcHz,
                // Stator spectral
                Stator_NSR = StatorNsr,
                Stator_ZSR = StatorZsr,
                Stator_Imbalance = StatorImbalancePct,
                Stator_HarmRatio = StatorHarmRatio,
                Stator_ResidRatio = StatorResidGndRatio,
                Stator_ZSR_H3 = StatorZsrH3,
                Stator_NsrH5 = StatorNsrH5,
                Stator_I0Mag = StatorI0Mag,
                Stator_I1Mag = StatorI1Mag,
                Stator_I2Mag = StatorI2Mag,
                // Stator time-domain
                Stator_NsrTd = StatorNsrTd,
                Stator_ZsrTd = StatorZsrTd,
                // Stator composite
                Stator_OddHarm = StatorOddHarmIndex,
                Stator_PhaseSpreadDeg = StatorPhaseSpreadDeg,
                Stator_ShortIndex = StatorShortIndex,
                Stator_GndIndex = StatorGndIndex,
                Stator_ShortIndexEma = StatorShortIndexEma,
                Stator_GndIndexEma = StatorGndIndexEma,
                Stator_CusumShort = StatorCusumShort,
                Stator_CusumGnd = StatorCusumGnd,
                Stator_FaultLevel = StatorFaultLevel,
                Stator_ShortLevel = StatorFaultShort,
                Stator_GndLevel = StatorFaultGnd,
                Stator_EarlyShort = StatorEarlyShort,
                Stator_EarlyGnd = StatorEarlyGnd,
            };
            return t;
        }

        /// <summary>
        /// Parses a binary fault frame from raw bytes (after header stripped).
        /// Returns null if the data is too short or CRC fails.
        /// </summary>
        public static BinaryFaultFrame Parse(byte[] data, int offset, int count)
        {
            if (data == null || count < STRUCT_SIZE) return null;

            var f = new BinaryFaultFrame();
            int o = offset;

            // Header
            f.FrameId = BitConverter.ToUInt32(data, o); o += 4;
            f.TimestampMs = BitConverter.ToUInt32(data, o); o += 4;
            f.Rpm = BitConverter.ToSingle(data, o); o += 4;
            f.Slip = BitConverter.ToSingle(data, o); o += 4;
            f.ShaftHz = BitConverter.ToSingle(data, o); o += 4;
            f.SupplyLineHz = BitConverter.ToSingle(data, o); o += 4;
            // Shaft / slip tracking
            f.SlipEstimated = BitConverter.ToSingle(data, o); o += 4;
            f.SlipAutoValid = data[o]; o += 1;
            f.PolePairs = data[o]; o += 1;
            // Bearing fault frequencies
            f.BpfoHz = BitConverter.ToSingle(data, o); o += 4;
            f.BpfiHz = BitConverter.ToSingle(data, o); o += 4;
            f.BsfHz = BitConverter.ToSingle(data, o); o += 4;
            f.FtfHz = BitConverter.ToSingle(data, o); o += 4;
            // 12 MCSA detection method indices
            f.IndexLs = BitConverter.ToSingle(data, o); o += 4;
            f.IndexFft = BitConverter.ToSingle(data, o); o += 4;
            f.IndexMusic = BitConverter.ToSingle(data, o); o += 4;
            f.IndexEsprit = BitConverter.ToSingle(data, o); o += 4;
            f.IndexTeager = BitConverter.ToSingle(data, o); o += 4;
            f.IndexSk = BitConverter.ToSingle(data, o); o += 4;
            f.IndexWavelet = BitConverter.ToSingle(data, o); o += 4;
            f.IndexCyclic = BitConverter.ToSingle(data, o); o += 4;
            f.IndexSideband = BitConverter.ToSingle(data, o); o += 4;
            f.IndexEnvAcf = BitConverter.ToSingle(data, o); o += 4;
            f.IndexWelch = BitConverter.ToSingle(data, o); o += 4;
            f.IndexCoherence = BitConverter.ToSingle(data, o); o += 4;
            // Spectral kurtosis peak
            f.SkPeakKurtosis = BitConverter.ToSingle(data, o); o += 4;
            f.SkPeakFHz = BitConverter.ToSingle(data, o); o += 4;
            f.KurtBandFcHz = BitConverter.ToUInt16(data, o); o += 2;
            // 4 Bearing partial-fault indices
            f.IndexBpfo = BitConverter.ToSingle(data, o); o += 4;
            f.IndexBpfi = BitConverter.ToSingle(data, o); o += 4;
            f.IndexBsf = BitConverter.ToSingle(data, o); o += 4;
            f.IndexFtf = BitConverter.ToSingle(data, o); o += 4;
            // Fused bearing scores
            f.FaultIndex = BitConverter.ToSingle(data, o); o += 4;
            f.FaultIndexEma = BitConverter.ToSingle(data, o); o += 4;
            f.CusumScore = BitConverter.ToSingle(data, o); o += 4;
            f.DetectConfidence = BitConverter.ToSingle(data, o); o += 4;
            f.FaultLevel = data[o]; o += 1;
            f.DominantFault = data[o]; o += 1;
            f.NpAlarmVotes = data[o]; o += 1;
            f.NpAlarmFourier = data[o]; o += 1;
            f.NpAlarmMusic = data[o]; o += 1;
            f.NpAlarmEsprit = data[o]; o += 1;
            // NP amplitudes
            f.AmpFourierFault = BitConverter.ToSingle(data, o); o += 4;
            f.AmpFourierBase = BitConverter.ToSingle(data, o); o += 4;
            f.AmpMusicFault = BitConverter.ToSingle(data, o); o += 4;
            f.AmpMusicBase = BitConverter.ToSingle(data, o); o += 4;
            f.AmpEspritFault = BitConverter.ToSingle(data, o); o += 4;
            f.AmpEspritBase = BitConverter.ToSingle(data, o); o += 4;
            // NP F-test critical ratios
            f.NpGammaFourier = BitConverter.ToSingle(data, o); o += 4;
            f.NpGammaMusic = BitConverter.ToSingle(data, o); o += 4;
            f.NpGammaEsprit = BitConverter.ToSingle(data, o); o += 4;
            // Stator spectral symmetrical components
            f.StatorI0Mag = BitConverter.ToSingle(data, o); o += 4;
            f.StatorI1Mag = BitConverter.ToSingle(data, o); o += 4;
            f.StatorI2Mag = BitConverter.ToSingle(data, o); o += 4;
            f.StatorNsr = BitConverter.ToSingle(data, o); o += 4;
            f.StatorZsr = BitConverter.ToSingle(data, o); o += 4;
            f.StatorImbalancePct = BitConverter.ToSingle(data, o); o += 4;
            f.StatorHarmRatio = BitConverter.ToSingle(data, o); o += 4;
            f.StatorResidGndRatio = BitConverter.ToSingle(data, o); o += 4;
            f.StatorZsrH3 = BitConverter.ToSingle(data, o); o += 4;
            f.StatorNsrH5 = BitConverter.ToSingle(data, o); o += 4;
            // Stator time-domain symmetrical components
            f.StatorI0RmsTd = BitConverter.ToSingle(data, o); o += 4;
            f.StatorI1RmsTd = BitConverter.ToSingle(data, o); o += 4;
            f.StatorI2RmsTd = BitConverter.ToSingle(data, o); o += 4;
            f.StatorNsrTd = BitConverter.ToSingle(data, o); o += 4;
            f.StatorZsrTd = BitConverter.ToSingle(data, o); o += 4;
            // Stator composite indices
            f.StatorOddHarmIndex = BitConverter.ToSingle(data, o); o += 4;
            f.StatorPhaseSpreadDeg = BitConverter.ToSingle(data, o); o += 4;
            f.StatorShortIndex = BitConverter.ToSingle(data, o); o += 4;
            f.StatorGndIndex = BitConverter.ToSingle(data, o); o += 4;
            f.StatorShortIndexEma = BitConverter.ToSingle(data, o); o += 4;
            f.StatorGndIndexEma = BitConverter.ToSingle(data, o); o += 4;
            f.StatorCusumShort = BitConverter.ToSingle(data, o); o += 4;
            f.StatorCusumGnd = BitConverter.ToSingle(data, o); o += 4;
            // Stator alarm / incipient flags
            f.StatorFaultLevel = data[o]; o += 1;
            f.StatorFaultShort = data[o]; o += 1;
            f.StatorFaultGnd = data[o]; o += 1;
            f.StatorEarlyShort = data[o]; o += 1;
            f.StatorEarlyGnd = data[o]; o += 1;
            f.Crc16 = BitConverter.ToUInt16(data, o); o += 2;

            // Verify CRC-16/CCITT (poly 0x1021, init 0xFFFF)
            ushort computed = Crc16Ccitt(data, offset, STRUCT_SIZE - 2);
            f.CrcValid = (computed == f.Crc16);

            return f;
        }

        /// <summary>CRC-16/CCITT: poly=0x1021, init=0xFFFF — matches main.c CRC16_CCITT.</summary>
        public static ushort Crc16Ccitt(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= (ushort)(data[offset + i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc = (ushort)(crc << 1);
                }
            }
            return crc;
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

        // The firmware services USB commands once per acquisition/DSP cycle
        // and blocks its main loop while streaming the full spectral export.
        // Timeouts must therefore exceed one worst-case frame cycle so a
        // command issued mid-frame still receives its reply.
        private const int CMD_TIMEOUT_MS = 6000;
        private const int PING_TIMEOUT_MS = 4000;
        private const int FLASH_TIMEOUT_MS = 8000;
        private const int INTER_CMD_DELAY = 25;
        /// <summary>Max time to open a COM port and complete one PING probe (wrong ports must not block ~3 min).</summary>
        private const int PROBE_PORT_TIMEOUT_MS = 6000;
        /// <summary>Max time for full Connect() open + handshake on a single port.</summary>
        private const int CONNECT_PORT_TIMEOUT_MS = 15000;
        /// <summary>REPORT/FULLREPORT: scalars + spectral + GRAPHDATA (main.c USB_Send_FullReport).</summary>
        private const int FULL_REPORT_TIMEOUT_MS = 180000;
        private const int GRAPHDATA_TIMEOUT_MS = 120000;
        /// <summary>Single technique (FFTCSV/MUSICCSV/…) — 2048 bins + phase overlays can take minutes on USB.</summary>
        private const int TECHNIQUE_EXPORT_TIMEOUT_MS = 120000;
        private const int PHASE_CSV_TIMEOUT_MS = 60000;

        private const byte FRAME_H1 = 0xAA;
        private const byte FRAME_H2 = 0x55;
        private const byte FRAME_F1 = 0x55;
        private const byte FRAME_F2 = 0xAA;

        // GET response: KEY=VALUE pairs. main.c emits in order:
        //   RPM SLIP BPFO BPFI BSF FTF FTH WTH LINE SLIPAUTO DATE TIME
        // (LINE/SLIPAUTO/DATE/TIME optional). The robust key=value fallback in
        // ParseGetResponse handles any ordering, but this fast path matches the
        // exact firmware layout.
        private static readonly Regex _getResponseRegex = new Regex(
            @"RPM=(\S+)\s+SLIP=(\S+)\s+BPFO=(\S+)\s+BPFI=(\S+)\s+BSF=(\S+)\s+FTF=(\S+)\s+FTH=(\S+)\s+WTH=(\S+)" +
            @"(?:\s+LINE=(\S+))?(?:\s+SLIPAUTO=(\S+))?(?:\s+DATE=(\S+))?(?:\s+TIME=(\S+))?",
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
        /// <summary>
        /// Fired when a binary fault frame (CMD=0xF0) is received and parsed.
        /// Only fires when the STM32 is in STREAM=1 (binary) mode.
        /// </summary>
        public event Action<BinaryFaultFrame> OnBinaryFrameReceived;

        public bool IsConnected
        {
            get { return _port != null && _port.IsOpen; }
        }

        /// <summary>True if the last Connect() received PING OK from the MCU.</summary>
        public bool LastConnectPingOk { get; private set; }

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

        private static bool IsSerialDriverTimeout(Exception ex)
        {
            if (ex == null) return false;
            string msg = ex.Message ?? "";
            return msg.IndexOf("semaphore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ForceClosePort()
        {
            lock (_portLock)
            {
                if (_port == null) return;
                _port.DataReceived -= Port_DataReceived;
                _port.ErrorReceived -= Port_ErrorReceived;
                try { if (_port.IsOpen) _port.Close(); } catch { }
                try { _port.Dispose(); } catch { }
                _port = null;
            }
        }

        private static SerialPort CreateSerialPort(string portName, int readTimeoutMs, int writeTimeoutMs)
        {
            return new SerialPort
            {
                PortName = portName,
                BaudRate = 115200,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                ReadTimeout = readTimeoutMs,
                WriteTimeout = writeTimeoutMs,
                ReadBufferSize = 65536,
                WriteBufferSize = 4096,
                DtrEnable = true,
                RtsEnable = true
            };
        }

        private static bool IsProbeResponse(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (text.IndexOf("H750", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        public async Task<bool> Connect(string portName)
        {
            if (IsConnected) await Disconnect();

            Task<bool> connectTask = Task.Run(() => ConnectSync(portName));
            Task finished = await Task.WhenAny(connectTask, Task.Delay(CONNECT_PORT_TIMEOUT_MS));
            if (finished != connectTask)
            {
                Log("Connect to " + portName + " timed out after " + CONNECT_PORT_TIMEOUT_MS +
                    " ms (wrong COM port, ST-Link VCP, or MCU USB not running)");
                ForceClosePort();
                return false;
            }

            try
            {
                return await connectTask;
            }
            catch (Exception ex)
            {
                Log("Connection failed: " + ex.Message);
                ForceClosePort();
                return false;
            }
        }

        private bool ConnectSync(string portName)
        {
            try
            {
                _port = CreateSerialPort(portName, 500, 800);
                _port.DataReceived += Port_DataReceived;
                _port.ErrorReceived += Port_ErrorReceived;
                _port.Open();

                Thread.Sleep(1200);

                try
                {
                    SendRaw(Encoding.ASCII.GetBytes("STREAM=0\r\n"));
                    Thread.Sleep(80);
                }
                catch { }

                lock (_rxBuffer) { _rxBuffer.Clear(); }
                lock (_rawTextLock) { _rawTextBuffer.Clear(); }
                lock (_linesLock) { _completedLines.Clear(); }
                if (_port.BytesToRead > 0) _port.DiscardInBuffer();

                LastConnectPingOk = false;
                for (int attempt = 0; attempt < 3 && !LastConnectPingOk; attempt++)
                {
                    if (attempt > 0)
                        Thread.Sleep(400);
                    LastConnectPingOk = PingTextSync();
                }

                Log(LastConnectPingOk
                    ? "Connected to " + portName + " — PING OK"
                    : "No PING OK on " + portName + " (wrong port or firmware not running)");

                if (LastConnectPingOk)
                {
                    OnConnectionChanged?.Invoke(true);
                    return true;
                }

                ForceClosePort();
                return false;
            }
            catch (Exception ex)
            {
                if (IsSerialDriverTimeout(ex))
                    Log(portName + ": USB serial driver timeout — try another COM port or replug the board");
                throw;
            }
        }

        private bool PingTextSync()
        {
            _commandPending = true;
            try
            {
                Thread.Sleep(15);
                ExtractCompletedLines();
                DrainTelemetryLines();
                SendTextLine("PING");
                return WaitForTextResponseSync(PING_TIMEOUT_MS) != null;
            }
            finally
            {
                _commandPending = false;
            }
        }

        private string WaitForTextResponseSync(int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                ExtractCompletedLines();
                lock (_linesLock)
                {
                    for (int i = 0; i < _completedLines.Count; i++)
                    {
                        string line = _completedLines[i];
                        if (IsTelemetryLine(line) || IsStatorLine(line) ||
                            IsPfLine(line) || IsNpLine(line) ||
                            IsSpectralLine(line))
                        {
                            ProcessSingleLineForTelemetry(line);
                            continue;
                        }
                        if (!IsCommandResponseLine(line))
                            continue;
                        _completedLines.RemoveAt(i);
                        return line;
                    }
                }
                Thread.Sleep(10);
            }
            return null;
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
        // BINARY FAULT FRAME EXTRACTION
        // =====================================================================
        // Scans _rxBuffer for [0xAA][0x55][0xF0] header, extracts the packed
        // FaultBinaryFrame_t, verifies footer [0x55][0xAA] and CRC, then fires
        // OnBinaryFrameReceived.  Called from the telemetry processing loop.
        // =====================================================================

        public void ExtractAndDispatchBinaryFrames()
        {
            lock (_rxBuffer)
            {
                while (_rxBuffer.Count >= BinaryFaultFrame.WIRE_PACKET_SIZE)
                {
                    // Find header: 0xAA 0x55 0xF0
                    int hdrIdx = -1;
                    for (int i = 0; i <= _rxBuffer.Count - BinaryFaultFrame.WIRE_PACKET_SIZE; i++)
                    {
                        if (_rxBuffer[i] == 0xAA && _rxBuffer[i + 1] == 0x55 &&
                            _rxBuffer[i + 2] == BinaryFaultFrame.CMD_FAULT_BINARY_FRAME)
                        {
                            hdrIdx = i;
                            break;
                        }
                    }

                    if (hdrIdx < 0)
                    {
                        // No header found — discard bytes that can't be part of a header
                        if (_rxBuffer.Count > 2)
                            _rxBuffer.RemoveRange(0, _rxBuffer.Count - 2);
                        return;
                    }

                    // Discard bytes before the header
                    if (hdrIdx > 0)
                        _rxBuffer.RemoveRange(0, hdrIdx);

                    // Check if we have enough data for a complete packet
                    if (_rxBuffer.Count < BinaryFaultFrame.WIRE_PACKET_SIZE)
                        return;

                    // Verify footer: [0x55][0xAA] at the end of the packet
                    int footIdx = BinaryFaultFrame.WIRE_HEADER_SIZE + BinaryFaultFrame.STRUCT_SIZE;
                    if (_rxBuffer[footIdx] != 0x55 || _rxBuffer[footIdx + 1] != 0xAA)
                    {
                        // Bad footer — skip this header byte and try again
                        _rxBuffer.RemoveAt(0);
                        continue;
                    }

                    // Extract struct bytes (after 3-byte header)
                    byte[] structData = new byte[BinaryFaultFrame.STRUCT_SIZE];
                    for (int i = 0; i < BinaryFaultFrame.STRUCT_SIZE; i++)
                        structData[i] = _rxBuffer[BinaryFaultFrame.WIRE_HEADER_SIZE + i];

                    // Remove the entire packet from the buffer
                    _rxBuffer.RemoveRange(0, BinaryFaultFrame.WIRE_PACKET_SIZE);

                    // Parse the binary frame
                    BinaryFaultFrame frame = BinaryFaultFrame.Parse(structData, 0, structData.Length);
                    if (frame != null && frame.CrcValid)
                    {
                        // Convert to TelemetryData and also fire the binary-specific event
                        TelemetryData td = frame.ToTelemetryData();
                        _lastTelemetry = td;
                        OnTelemetryReceived?.Invoke(td);
                        OnBinaryFrameReceived?.Invoke(frame);
                    }
                }
            }
        }

        /// <summary>
        /// Sends the STREAM=0/1 command to switch between text and binary live streaming.
        /// STREAM=1 = binary frames (~120B), STREAM=0 = text (default, backward-compatible).
        /// </summary>
        public async Task<bool> SetStreamMode(bool binaryEnabled)
        {
            if (!IsConnected) return false;
            string cmd = binaryEnabled ? "STREAM=1" : "STREAM=0";
            string resp = await SendTextCommand(cmd);
            return resp != null && resp.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;
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

                        // Skip auto-sent telemetry / spectral (main.c USB_Send_FaultSummary)
                        if (IsTelemetryLine(line) || IsStatorLine(line) ||
                            IsPfLine(line) || IsNpLine(line) ||
                            IsSpectralLine(line))
                        {
                            ProcessSingleLineForTelemetry(line);
                            continue;
                        }

                        if (!IsCommandResponseLine(line))
                            continue;

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

                        if (IsTelemetryLine(line) || IsStatorLine(line) ||
                            IsPfLine(line) || IsNpLine(line))
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
                // Technique exports always end with ### END_EXPORT — do not cut off early.
                else if (idleMs > 0 && endMode != UsbMultiLineEndMode.TechniqueExport &&
                         result.Count > 0 &&
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
        /// (auto-sent by USB_Send_FaultSummary line 1 in main.c).
        /// Firmware uses Hz keys: BPFO_HZ= BPFI_HZ= BSF_HZ= FTF_HZ= FI= LV=
        /// (legacy builds used BPFO= without _HZ).
        /// </summary>
        private static bool IsTelemetryLine(string line)
        {
            if (string.IsNullOrEmpty(line) || !line.Contains("FI=") || !line.Contains("LV="))
                return false;
            return line.StartsWith("BPFO_HZ=", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("BPFO=", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True for PING/GET/SET ack lines (must not be consumed as GET/PING replies).</summary>
        private static bool IsCommandResponseLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            if (line.StartsWith("OK", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.StartsWith("COEFF", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.StartsWith("RPM=", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
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

        /// <summary>NP technique alarm line from main.c USB_Send_FaultSummary.</summary>
        private static bool IsNpLine(string line)
        {
            return line.StartsWith("NP ") && line.Contains("aF=");
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
                line.StartsWith("[WELCH_CSV]") ||
                line.StartsWith("[COH_CSV]") ||
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
                line.StartsWith("[FOURIER_P1_CSV]") ||
                line.StartsWith("[FOURIER_P2_CSV]") ||
                line.StartsWith("[FOURIER_P3_CSV]") ||
                line.StartsWith("[MUSIC_P1_CSV]") ||
                line.StartsWith("[MUSIC_P2_CSV]") ||
                line.StartsWith("[MUSIC_P3_CSV]") ||
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
            _commandPending = true;
            try
            {
                // Small delay to let the telemetry loop finish its current cycle
                await Task.Delay(15);

                // Flush any pending raw text into the line queue,
                // then drain telemetry lines that arrived before our command
                ExtractCompletedLines();
                DrainTelemetryLines();

                SendTextLine(command);
                Log("TX> " + command);

                string response = await WaitForTextResponse(timeoutMs);

                if (response != null)
                    Log("RX< " + response);
                else
                    Log("RX< (timeout after " + timeoutMs + "ms)");

                return response;
            }
            catch (Exception ex)
            {
                Log("Text cmd '" + command + "': " + ex.Message);
                return null;
            }
            finally
            {
                _commandPending = false;
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
                        IsPfLine(line) || IsNpLine(line) ||
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
            await SendSimpleCommand(STM32Commands.CMD_SAVE_ADC_TO_SPI1, FLASH_TIMEOUT_MS);

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
                if (TryParseRobustFloat(m.Groups[5].Value, out v)) p.BSF = v;
                if (TryParseRobustFloat(m.Groups[6].Value, out v)) p.FTF = v;
                if (TryParseRobustFloat(m.Groups[7].Value, out v)) p.FaultThreshold = v;
                if (TryParseRobustFloat(m.Groups[8].Value, out v)) p.WarningThreshold = v;
                if (m.Groups.Count > 9 && m.Groups[9].Success &&
                    TryParseRobustFloat(m.Groups[9].Value, out v))
                    p.SupplyLineHz = v;
                if (m.Groups.Count > 10 && m.Groups[10].Success)
                    p.SlipAuto = ParseBoolFlag(m.Groups[10].Value);
                if (m.Groups.Count > 11 && m.Groups[11].Success)
                    p.ClockDate = m.Groups[11].Value;
                if (m.Groups.Count > 12 && m.Groups[12].Success)
                    p.ClockTime = m.Groups[12].Value;
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
            if (kv.ContainsKey("SLIPAUTO"))
                pm.SlipAuto = ParseBoolFlag(kv["SLIPAUTO"]);
            if (kv.ContainsKey("DATE"))
                pm.ClockDate = kv["DATE"];
            if (kv.ContainsKey("TIME"))
                pm.ClockTime = kv["TIME"];

            return pm;
        }

        /// <summary>Parses a 0/1 flag (also accepts ON/OFF, TRUE/FALSE) from main.c.</summary>
        private static bool ParseBoolFlag(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            string t = token.Trim();
            if (t == "1") return true;
            if (t == "0") return false;
            return t.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                   t.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   t.Equals("yes", StringComparison.OrdinalIgnoreCase);
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
            await Task.Delay(INTER_CMD_DELAY);
            // main.c: SLIPAUTO=0/1 (auto-slip estimation toggle)
            if (await SetSlipAutoText(p.SlipAuto)) ok++;
            return ok;
        }

        /// <summary>main.c: SLIPAUTO=0/1 — ack "OK SLIPAUTO=0/1".</summary>
        public async Task<bool> SetSlipAutoText(bool enabled)
        {
            string r = await SendTextCommand(
                STM32TextCommands.SlipAuto + "=" + (enabled ? "1" : "0"), CMD_TIMEOUT_MS);
            return r != null && r.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// main.c: STREAM=0/1 — enables/disables continuous telemetry+spectral
        /// auto-streaming. Off keeps the device silent so connect/GET/SET are
        /// instant; on resumes the live data feed. Ack: "OK STREAM=0/1".
        /// </summary>
        public async Task<bool> SetStreamText(bool enabled)
        {
            string r = await SendTextCommand(
                STM32TextCommands.Stream + "=" + (enabled ? "1" : "0"), CMD_TIMEOUT_MS);
            return r != null && r.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// main.c GETCOEFF → "COEFF BPFO=.. BPFI=.. BSF=.. FTF=..".
        /// Returns a MotorParameters with only the bearing coefficient fields set.
        /// </summary>
        public async Task<MotorParameters> GetCoefficientsText()
        {
            string resp = await SendTextCommand(STM32TextCommands.GetCoeff, CMD_TIMEOUT_MS);
            if (resp == null) { Log("GetCoeff: no response"); return null; }

            Dictionary<string, string> kv = ParseKeyValuePairs(resp);
            if (kv.Count == 0) { Log("GetCoeff: no key=value pairs in [" + resp + "]"); return null; }

            MotorParameters p = new MotorParameters();
            float fv;
            if (kv.ContainsKey("BPFO") && TryParseRobustFloat(kv["BPFO"], out fv)) p.BPFO = fv;
            if (kv.ContainsKey("BPFI") && TryParseRobustFloat(kv["BPFI"], out fv)) p.BPFI = fv;
            if (kv.ContainsKey("BSF") && TryParseRobustFloat(kv["BSF"], out fv)) p.BSF = fv;
            if (kv.ContainsKey("FTF") && TryParseRobustFloat(kv["FTF"], out fv)) p.FTF = fv;
            return p;
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

        /// <summary>Same payload as GRAPHDATA (main.c accepts graphs alias).</summary>
        public Task<List<string>> RequestGraphs()
        {
            return RequestMultiLineExport(
                STM32TextCommands.Graphs,
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

        /// <summary>
        /// Polls main.c READY until DSP=1 (first Fault_Detect_Bearing export done).
        /// First frame is ~1.6 s capture + several seconds DSP on H750.
        /// </summary>
        public async Task<bool> WaitUntilDspReadyAsync(int timeoutMs = 90000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            bool logged = false;

            while (DateTime.UtcNow < deadline)
            {
                string r = await SendTextCommand(STM32TextCommands.Ready, PING_TIMEOUT_MS);
                if (r != null &&
                    r.IndexOf("DSP=1", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (r != null &&
                    r.IndexOf("DMA=0", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log("READY: DMA=0 — ADC DMA failed at boot (reflash firmware)");
                    return false;
                }

                if (!logged)
                {
                    Log("Waiting for capture + DSP (expect CAP=1 then PHASE=1 DSP=1, ~2–20 s)…");
                    logged = true;
                }
                await Task.Delay(500);
            }

            Log("READY timeout — DSP export not ready");
            return false;
        }

        public static bool IsNotReadyError(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            if (line.IndexOf("ERR", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return line.IndexOf("wait for capture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("DSP still processing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("DSP (~5", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// On-demand technique export (main.c ends with ### END_EXPORT MODE=…).
        /// Pauses STREAM so live spectral flood does not block the MCU USB handler.
        /// </summary>
        public async Task<List<string>> RequestTechniqueCsv(string command, bool waitForDsp = true)
        {
            if (IsConnected)
            {
                try { await SetStreamText(false); } catch { }
                await Task.Delay(200);
            }

            if (waitForDsp && IsConnected)
                await WaitUntilDspReadyAsync();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                List<string> lines = await RequestMultiLineExport(
                    command,
                    TECHNIQUE_EXPORT_TIMEOUT_MS,
                    UsbMultiLineEndMode.TechniqueExport,
                    0);

                if (lines == null || lines.Count == 0)
                {
                    await Task.Delay(800);
                    continue;
                }

                bool notReady = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (IsNotReadyError(lines[i]))
                    {
                        notReady = true;
                        break;
                    }
                }

                if (!notReady)
                    return lines;

                Log(command + ": not ready, retry " + (attempt + 2) + "/3…");
                await Task.Delay(1000);
                await WaitUntilDspReadyAsync(15000);
            }

            return new List<string>();
        }

        public Task<List<string>> RequestSkCsv()
        {
            return RequestTechniqueCsv(STM32TextCommands.SkCsv);
        }

        public Task<List<string>> RequestWaveletCsv()
        {
            return RequestTechniqueCsv(STM32TextCommands.WaveletCsv);
        }

        /// <summary>main.c WELCHCSV → [WELCH_CSV] (Welch PSD); ends with ### END_EXPORT MODE=WELCH.</summary>
        public Task<List<string>> RequestWelchCsv()
        {
            return RequestTechniqueCsv(STM32TextCommands.WelchCsv);
        }

        /// <summary>main.c COHCSV → [COH_CSV] (coherence 0..1); ends with ### END_EXPORT MODE=COH.</summary>
        public Task<List<string>> RequestCohCsv()
        {
            return RequestTechniqueCsv(STM32TextCommands.CohCsv);
        }

        public async Task<string> RequestHelp()
        {
            return await SendTextCommand(STM32TextCommands.Help, CMD_TIMEOUT_MS);
        }

        public async Task<string> SendBaselineTextCommand(string command)
        {
            return await SendMainCTextCommand(command);
        }

        /// <summary>
        /// Sends any main.c text line with appropriate timeout (flash/baseline vs normal).
        /// </summary>
        public async Task<string> SendMainCTextCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string c = command.Trim();
            string upper = c.ToUpperInvariant();
            int timeout = CMD_TIMEOUT_MS;
            if (upper == STM32TextCommands.Save || upper == STM32TextCommands.Load ||
                upper == STM32TextCommands.Default || upper == STM32TextCommands.SaveAdc ||
                upper == STM32TextCommands.Calib || upper == STM32TextCommands.SaveBase ||
                upper == STM32TextCommands.LoadBase || upper == STM32TextCommands.ClearBase ||
                upper == STM32TextCommands.CalibSt || upper == STM32TextCommands.SaveStSt ||
                upper == STM32TextCommands.LoadStSt || upper == STM32TextCommands.ClearStSt)
            {
                timeout = FLASH_TIMEOUT_MS;
            }
            return await SendTextCommand(c, timeout);
        }

        public Task<string> SendClockDateAsync(string yyyyMmDd)
        {
            return SendTextCommand(STM32TextCommands.DatePrefix + " " + yyyyMmDd.Trim(), CMD_TIMEOUT_MS);
        }

        public Task<string> SendClockTimeAsync(string hhMmSs)
        {
            return SendTextCommand(STM32TextCommands.TimePrefix + " " + hhMmSs.Trim(), CMD_TIMEOUT_MS);
        }

        private async Task<List<string>> RequestMultiLineExport(
            string command,
            int timeoutMs = FULL_REPORT_TIMEOUT_MS,
            UsbMultiLineEndMode endMode = UsbMultiLineEndMode.FullReport,
            int idleMs = 2000)
        {
            bool stoppedTelemetry = false;
            try
            {
                _commandPending = true;
                StopTelemetryMonitor();
                stoppedTelemetry = true;

                await Task.Delay(15);
                ExtractCompletedLines();
                DrainTelemetryLines();

                SendTextLine(command);
                Log("TX> " + command);

                List<string> lines = await WaitForMultiLineResponse(timeoutMs, endMode, idleMs);

                Log(command + ": received " + lines.Count + " lines");
                return lines;
            }
            catch (Exception ex)
            {
                Log(command + ": " + ex.Message);
                return new List<string>();
            }
            finally
            {
                _commandPending = false;
                if (stoppedTelemetry)
                    StartTelemetryMonitor();
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
            if (kv.ContainsKey("BPFO_HZ") && TryParseRobustFloat(kv["BPFO_HZ"], out v))
                t.BPFO_Hz = v;
            else if (kv.ContainsKey("BPFO") && TryParseRobustFloat(kv["BPFO"], out v))
                t.BPFO_Hz = v;
            if (kv.ContainsKey("BPFI_HZ") && TryParseRobustFloat(kv["BPFI_HZ"], out v))
                t.BPFI_Hz = v;
            else if (kv.ContainsKey("BPFI") && TryParseRobustFloat(kv["BPFI"], out v))
                t.BPFI_Hz = v;
            if (kv.ContainsKey("BSF_HZ") && TryParseRobustFloat(kv["BSF_HZ"], out v))
                t.BSF_Hz = v;
            else if (kv.ContainsKey("BSF") && TryParseRobustFloat(kv["BSF"], out v))
                t.BSF_Hz = v;
            if (kv.ContainsKey("FTF_HZ") && TryParseRobustFloat(kv["FTF_HZ"], out v))
                t.FTF_Hz = v;
            else if (kv.ContainsKey("FTF") && TryParseRobustFloat(kv["FTF"], out v))
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
            if (kv.ContainsKey("WL") && TryParseRobustFloat(kv["WL"], out v))
                t.Index_Welch = v;
            if (kv.ContainsKey("COH") && TryParseRobustFloat(kv["COH"], out v))
                t.Index_Coherence = v;
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
            if (kv.ContainsKey("HARM") && TryParseRobustFloat(kv["HARM"], out fv))
                target.Stator_HarmRatio = fv;
            if (kv.ContainsKey("R") && TryParseRobustFloat(kv["R"], out fv))
                target.Stator_ResidRatio = fv;
            if (kv.ContainsKey("RESID") && TryParseRobustFloat(kv["RESID"], out fv))
                target.Stator_ResidRatio = fv;
            if (kv.ContainsKey("Z3") && TryParseRobustFloat(kv["Z3"], out fv))
                target.Stator_ZSR_H3 = fv;
            if (kv.ContainsKey("ZSR_H3") && TryParseRobustFloat(kv["ZSR_H3"], out fv))
                target.Stator_ZSR_H3 = fv;
            if (kv.ContainsKey("IMB") && TryParseRobustFloat(kv["IMB"], out fv))
                target.Stator_Imbalance = fv;
            if (kv.ContainsKey("IMB_PCT") && TryParseRobustFloat(kv["IMB_PCT"], out fv))
                target.Stator_Imbalance = fv;
            if (kv.ContainsKey("NSR_TD") && TryParseRobustFloat(kv["NSR_TD"], out fv))
                target.Stator_NsrTd = fv;
            if (kv.ContainsKey("ZSR_TD") && TryParseRobustFloat(kv["ZSR_TD"], out fv))
                target.Stator_ZsrTd = fv;
            if (kv.ContainsKey("NSR_H5") && TryParseRobustFloat(kv["NSR_H5"], out fv))
                target.Stator_NsrH5 = fv;
            if (kv.ContainsKey("ODD") && TryParseRobustFloat(kv["ODD"], out fv))
                target.Stator_OddHarm = fv;
            if (kv.ContainsKey("PS_DEG") && TryParseRobustFloat(kv["PS_DEG"], out fv))
                target.Stator_PhaseSpreadDeg = fv;
            if (kv.ContainsKey("SI") && TryParseRobustFloat(kv["SI"], out fv))
                target.Stator_ShortIndex = fv;
            if (kv.ContainsKey("GI") && TryParseRobustFloat(kv["GI"], out fv))
                target.Stator_GndIndex = fv;
            if (kv.ContainsKey("SI_EMA") && TryParseRobustFloat(kv["SI_EMA"], out fv))
                target.Stator_ShortIndexEma = fv;
            if (kv.ContainsKey("GI_EMA") && TryParseRobustFloat(kv["GI_EMA"], out fv))
                target.Stator_GndIndexEma = fv;
            if (kv.ContainsKey("CS") && TryParseRobustFloat(kv["CS"], out fv))
                target.Stator_CusumShort = fv;
            if (kv.ContainsKey("CG") && TryParseRobustFloat(kv["CG"], out fv))
                target.Stator_CusumGnd = fv;
            if (kv.ContainsKey("ES") && int.TryParse(kv["ES"], out iv))
                target.Stator_EarlyShort = iv;
            if (kv.ContainsKey("EG") && int.TryParse(kv["EG"], out iv))
                target.Stator_EarlyGnd = iv;
            if (kv.ContainsKey("I0") && TryParseRobustFloat(kv["I0"], out fv))
                target.Stator_I0Mag = fv;
            if (kv.ContainsKey("I1") && TryParseRobustFloat(kv["I1"], out fv))
                target.Stator_I1Mag = fv;
            if (kv.ContainsKey("I2") && TryParseRobustFloat(kv["I2"], out fv))
                target.Stator_I2Mag = fv;
            if (kv.ContainsKey("LV") && int.TryParse(kv["LV"], out iv))
                target.Stator_FaultLevel = iv;
            else if (kv.ContainsKey("SLV") && int.TryParse(kv["SLV"], out iv))
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

        /// <summary>Windows PnP caption, e.g. "STMicroelectronics STLink Virtual COM Port (COM6)".</summary>
        public static string GetPortFriendlyName(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName)) return "";
            try
            {
                string q = "SELECT Caption FROM Win32_PnPEntity WHERE Caption LIKE '%(" +
                           portName.Replace("'", "") + ")'";
                using (var searcher = new ManagementObjectSearcher(q))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object cap = mo["Caption"];
                        if (cap != null) return cap.ToString();
                    }
                }
            }
            catch { }
            return "";
        }

        private static bool IsLikelyStLinkVcp(string friendlyCaption)
        {
            if (string.IsNullOrEmpty(friendlyCaption)) return false;
            return friendlyCaption.IndexOf("STLink", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   friendlyCaption.IndexOf("ST-Link", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   friendlyCaption.IndexOf("ST Link", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLikelyMcuUsbCdc(string friendlyCaption)
        {
            if (string.IsNullOrEmpty(friendlyCaption)) return false;
            if (IsLikelyStLinkVcp(friendlyCaption)) return false;
            return friendlyCaption.IndexOf("Virtual COM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   friendlyCaption.IndexOf("USB Serial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   friendlyCaption.IndexOf("CDC", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Opens a port briefly and sends PING. Returns true if "OK" is received.
        /// Use to pick the USB-CDC port when ST-Link VCP is also present.
        /// </summary>
        public async Task<bool> ProbePortWithLogAsync(string portName, int timeoutMs = PROBE_PORT_TIMEOUT_MS)
        {
            string caption = GetPortFriendlyName(portName);
            if (!string.IsNullOrEmpty(caption))
                Log("  " + caption);
            if (IsLikelyStLinkVcp(caption))
                Log("  WARNING: " + portName + " looks like ST-Link debug VCP — use the MCU USB port (separate cable/jack)");
            else if (IsLikelyMcuUsbCdc(caption))
                Log("  " + portName + " looks like MCU USB-CDC");

            string rxHint;
            bool ok = await ProbePortAsync(portName, timeoutMs, out rxHint);
            if (ok)
            {
                Log(portName + " — firmware answered PING/banner");
                return true;
            }
            if (!string.IsNullOrEmpty(rxHint))
                Log(portName + " RX (not H750): " + rxHint);
            else
                Log(portName + ": no reply — reflash H750 firmware, replug MCU USB, refresh ports");
            return false;
        }

        public static async Task<bool> ProbePortAsync(string portName, int timeoutMs = PROBE_PORT_TIMEOUT_MS)
        {
            string ignored;
            return await ProbePortAsync(portName, timeoutMs, out ignored);
        }

        public static async Task<bool> ProbePortAsync(string portName, int timeoutMs, out string rxHint)
        {
            rxHint = null;
            if (string.IsNullOrWhiteSpace(portName)) return false;

            Task<Tuple<bool, string>> probe = Task.Run(() =>
            {
                string hint;
                bool ok = ProbePortSync(portName, timeoutMs, out hint);
                return Tuple.Create(ok, hint);
            });
            Task finished = await Task.WhenAny(probe, Task.Delay(timeoutMs + 800));
            if (finished != probe)
                return false;
            try
            {
                Tuple<bool, string> r = await probe;
                rxHint = r.Item2;
                return r.Item1;
            }
            catch
            {
                return false;
            }
        }

        private static bool ProbePortSync(string portName, int timeoutMs, out string rxHint)
        {
            rxHint = null;
            SerialPort port = null;
            var acc = new StringBuilder();
            try
            {
                port = CreateSerialPort(portName, 200, 800);
                port.Open();
                Thread.Sleep(1200);
                if (port.BytesToRead > 0)
                    port.DiscardInBuffer();

                byte[] quiet = Encoding.ASCII.GetBytes("STREAM=0\r\n");
                byte[] cmd = Encoding.ASCII.GetBytes("PING\r\n");
                DateTime t0 = DateTime.UtcNow;
                byte[] buf = new byte[512];

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if ((DateTime.UtcNow - t0).TotalMilliseconds >= timeoutMs)
                        break;
                    if (attempt == 0)
                    {
                        try { port.Write(quiet, 0, quiet.Length); } catch { }
                        Thread.Sleep(80);
                    }
                    if (attempt == 2)
                    {
                        try { port.Write(new byte[] { STM32Commands.CMD_PING }, 0, 1); } catch { }
                    }
                    else
                    {
                        try { port.Write(cmd, 0, cmd.Length); } catch { }
                    }

                    DateTime attemptEnd = DateTime.UtcNow.AddMilliseconds(timeoutMs / 3);
                    while (DateTime.UtcNow < attemptEnd &&
                           (DateTime.UtcNow - t0).TotalMilliseconds < timeoutMs)
                    {
                        int waiting = port.BytesToRead;
                        if (waiting > 0)
                        {
                            int n = port.Read(buf, 0, Math.Min(waiting, buf.Length));
                            for (int bi = 0; bi < n; bi++)
                            {
                                if (buf[bi] == 0xAA)
                                    return true;
                            }
                            acc.Append(Encoding.ASCII.GetString(buf, 0, n));
                            if (IsProbeResponse(acc.ToString()))
                                return true;
                        }
                        Thread.Sleep(40);
                    }
                }

                string seen = acc.ToString().Trim();
                if (seen.Length > 0)
                    rxHint = seen.Length > 100 ? seen.Substring(0, 100) + "…" : seen;
                return false;
            }
            catch (Exception ex)
            {
                if (IsSerialDriverTimeout(ex))
                    rxHint = "serial driver timeout";
                return false;
            }
            finally
            {
                if (port != null)
                {
                    try { if (port.IsOpen) port.Close(); } catch { }
                    try { port.Dispose(); } catch { }
                }
            }
        }

        /// <summary>Tries each COM port (preferred first) and connects to the first that answers PING.</summary>
        public async Task<string> ConnectFirstResponsivePortAsync(string preferredPort = null)
        {
            string[] ports = GetAvailablePorts();
            if (ports.Length == 0) return null;

            var order = new List<string>();
            if (!string.IsNullOrEmpty(preferredPort) && ports.Contains(preferredPort))
                order.Add(preferredPort);
            foreach (string p in ports)
            {
                if (!order.Contains(p))
                    order.Add(p);
            }

            foreach (string port in order)
            {
                Log("Probing " + port + "…");
                if (!await ProbePortWithLogAsync(port, PROBE_PORT_TIMEOUT_MS))
                    continue;

                Log(port + " — PING OK, opening session…");
                if (await Connect(port))
                    return port;
            }
            return null;
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