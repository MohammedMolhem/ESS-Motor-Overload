using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EES_MotorOverload_V1
{
    public class SpectralFrame
    {
        public List<PointF> FourierPoints = new List<PointF>();
        public List<PointF> FourierPhase1Points = new List<PointF>();
        public List<PointF> FourierPhase2Points = new List<PointF>();
        public List<PointF> FourierPhase3Points = new List<PointF>();
        public List<PointF> MusicPoints = new List<PointF>();
        public List<PointF> MusicPhase1Points = new List<PointF>();
        public List<PointF> MusicPhase2Points = new List<PointF>();
        public List<PointF> MusicPhase3Points = new List<PointF>();
        public List<PointF> Cyclic2Points = new List<PointF>();
        public List<PointF> Cyclic2Phase1Points = new List<PointF>();
        public List<PointF> Cyclic2Phase2Points = new List<PointF>();
        public List<PointF> Cyclic2Phase3Points = new List<PointF>();
        public List<PointF> WaveletPoints = new List<PointF>();
        public List<PointF> WaveletPhase1Points = new List<PointF>();
        public List<PointF> WaveletPhase2Points = new List<PointF>();
        public List<PointF> WaveletPhase3Points = new List<PointF>();
        public List<PointF> SkPoints = new List<PointF>();
        public List<PointF> SkPhase1Points = new List<PointF>();
        public List<PointF> SkPhase2Points = new List<PointF>();
        public List<PointF> SkPhase3Points = new List<PointF>();
        /// <summary>Welch PSD (main.c [WELCH_CSV]) — single 3-phase-max trace.</summary>
        public List<PointF> WelchPoints = new List<PointF>();
        /// <summary>Magnitude-squared coherence 0..1 (main.c [COH_CSV]) — single 3-phase-min trace.</summary>
        public List<PointF> CoherencePoints = new List<PointF>();
        public List<float> EspritFrequencies = new List<float>();
        public List<float> EspritPhase1Frequencies = new List<float>();
        public List<float> EspritPhase2Frequencies = new List<float>();
        public List<float> EspritPhase3Frequencies = new List<float>();
        public bool IsComplete = false;
        public string Mode { get; set; }
        public TelemetryData ReportTelemetry { get; set; } = new TelemetryData();

        /// <summary>Motor/setup snapshot from [PARAMS] in full report (main.c).</summary>
        public MotorParameters ReportMotorParams { get; set; }

        /// <summary>
        /// Set when main.c sends the closing line: ### END_REPORT (full REPORT / auto export)
        /// or ### END_EXPORT MODE=… (FFTCSV, MUSICCSV, …).
        /// </summary>
        public string FinalReportSummary { get; set; }
    }

    public class STM32ReportParser
    {
        private SpectralFrame _current = new SpectralFrame();
        private readonly object _lock = new object();
        private SpectralFrame _lastComplete = null;
        private List<PointF> _activeNativeFftTarget = null;
        /// <summary>REPORT/FULLREPORT: do not reset frame on H750_DSP or complete on END_REPORT.</summary>
        private bool _fullReportOpen = false;

        public event Action<SpectralFrame> OnFrameReady;

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public void ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            lock (_lock)
            {
                if (line.StartsWith("### H750_FULL_REPORT"))
                {
                    _fullReportOpen = true;
                    if (_current == null)
                        _current = new SpectralFrame();
                    ParseScalarLine(line);
                    return;
                }

                if (line.StartsWith("### H750_DSP"))
                {
                    if (!_fullReportOpen)
                    {
                        _current = new SpectralFrame();
                        _activeNativeFftTarget = null;
                    }
                    Match mm = Regex.Match(line, @"MODE=(\S+)");
                    _current.Mode = mm.Success ? mm.Groups[1].Value.Trim() : "";
                    return;
                }

                if (line.StartsWith("[META]") ||
                    line.StartsWith("[MODEL_IDX]") ||
                    line.StartsWith("[PARAMS]") ||
                    line.StartsWith("[MUSIC_EVAL_DESC]") ||
                    line.StartsWith("[WAVELET_META]") ||
                    line.StartsWith("T_MS=") ||
                    line.StartsWith("### BEGIN_GRAPHDATA") ||
                    line.StartsWith("### END_GRAPHDATA") ||
                    line.StartsWith("### SECTION") ||
                    line.StartsWith("### PROGRESS"))
                {
                    ParseScalarLine(line);
                    return;
                }

                if (line.StartsWith("### END_FULL_REPORT"))
                {
                    _activeNativeFftTarget = null;
                    _current.FinalReportSummary = "END_FULL_REPORT (REPORT/FULLREPORT from main.c)";
                    CompleteCurrentFrame();
                    _fullReportOpen = false;
                    return;
                }

                if (line.StartsWith("### END_REPORT"))
                {
                    _activeNativeFftTarget = null;
                    if (_fullReportOpen)
                    {
                        _current.FinalReportSummary = "END_REPORT (spectral — full report continues)";
                        return;
                    }
                    _current.FinalReportSummary = "END_REPORT (spectral export)";
                    CompleteCurrentFrame();
                    return;
                }

                if (line.StartsWith("### END_EXPORT"))
                {
                    _activeNativeFftTarget = null;
                    Match mm = Regex.Match(line, @"MODE=(\S+)");
                    string mode = mm.Success ? mm.Groups[1].Value.Trim() : "";
                    _current.FinalReportSummary = string.IsNullOrEmpty(mode)
                        ? "END_EXPORT (single technique)"
                        : "END_EXPORT MODE=" + mode;
                    CompleteCurrentFrame();
                    return;
                }

                if (TrySetNativeFftTargetFromLine(line)) return;
                if (TryParseNativeFftDataLine(line)) return;

                if (TryParseMainCSpectrumTag(line))
                {
                }
                else if (TryParsePhaseCsv(line))
                {
                }
                else if (line.StartsWith("[ESPRIT_HZ]"))
                {
                    ParseEspritHzLine(line, _current.EspritFrequencies);
                }
                else if (line.StartsWith("[ESPRIT_P1_HZ]"))
                {
                    ParseEspritHzLine(line, _current.EspritPhase1Frequencies);
                }
                else if (line.StartsWith("[ESPRIT_P2_HZ]"))
                {
                    ParseEspritHzLine(line, _current.EspritPhase2Frequencies);
                }
                else if (line.StartsWith("[ESPRIT_P3_HZ]"))
                {
                    ParseEspritHzLine(line, _current.EspritPhase3Frequencies);
                }
                else if (line.StartsWith("[ESPRIT_CSV]"))
                {
                    ParseEspritCsvLine(line, _current.EspritFrequencies);
                }
                else if (line.StartsWith("[ESPRIT_TSV]"))
                {
                    ParseEspritTsvLine(line, _current.EspritFrequencies);
                }
                else if (line.StartsWith("[BEARING]") || line.StartsWith("[STATOR]"))
                {
                    ParseScalarLine(line);
                }
            }
        }

        private void CompleteCurrentFrame()
        {
            SpectralFrame frameToPublish;
            if (_fullReportOpen || _lastComplete == null)
            {
                frameToPublish = _current;
            }
            else
            {
                frameToPublish = CloneFrame(_lastComplete);
                MergeFrameData(frameToPublish, _current);
            }

            frameToPublish.IsComplete = true;
            _lastComplete = frameToPublish;
            OnFrameReady?.Invoke(frameToPublish);
            _current = new SpectralFrame();
        }

        /// <summary>
        /// Keeps prior technique data when a single *CSV export arrives (main.c END_EXPORT).
        /// Full REPORT/FULLREPORT replaces the frame via _fullReportOpen.
        /// </summary>
        private static void MergeFrameData(SpectralFrame target, SpectralFrame source)
        {
            if (target == null || source == null) return;

            ReplaceIfNonEmpty(target.FourierPoints, source.FourierPoints);
            ReplaceIfNonEmpty(target.FourierPhase1Points, source.FourierPhase1Points);
            ReplaceIfNonEmpty(target.FourierPhase2Points, source.FourierPhase2Points);
            ReplaceIfNonEmpty(target.FourierPhase3Points, source.FourierPhase3Points);
            ReplaceIfNonEmpty(target.MusicPoints, source.MusicPoints);
            ReplaceIfNonEmpty(target.MusicPhase1Points, source.MusicPhase1Points);
            ReplaceIfNonEmpty(target.MusicPhase2Points, source.MusicPhase2Points);
            ReplaceIfNonEmpty(target.MusicPhase3Points, source.MusicPhase3Points);
            ReplaceIfNonEmpty(target.Cyclic2Points, source.Cyclic2Points);
            ReplaceIfNonEmpty(target.Cyclic2Phase1Points, source.Cyclic2Phase1Points);
            ReplaceIfNonEmpty(target.Cyclic2Phase2Points, source.Cyclic2Phase2Points);
            ReplaceIfNonEmpty(target.Cyclic2Phase3Points, source.Cyclic2Phase3Points);
            ReplaceIfNonEmpty(target.SkPoints, source.SkPoints);
            ReplaceIfNonEmpty(target.SkPhase1Points, source.SkPhase1Points);
            ReplaceIfNonEmpty(target.SkPhase2Points, source.SkPhase2Points);
            ReplaceIfNonEmpty(target.SkPhase3Points, source.SkPhase3Points);
            ReplaceIfNonEmpty(target.WelchPoints, source.WelchPoints);
            ReplaceIfNonEmpty(target.CoherencePoints, source.CoherencePoints);
            ReplaceIfNonEmpty(target.WaveletPoints, source.WaveletPoints);
            ReplaceIfNonEmpty(target.WaveletPhase1Points, source.WaveletPhase1Points);
            ReplaceIfNonEmpty(target.WaveletPhase2Points, source.WaveletPhase2Points);
            ReplaceIfNonEmpty(target.WaveletPhase3Points, source.WaveletPhase3Points);
            ReplaceIfNonEmpty(target.EspritFrequencies, source.EspritFrequencies);
            ReplaceIfNonEmpty(target.EspritPhase1Frequencies, source.EspritPhase1Frequencies);
            ReplaceIfNonEmpty(target.EspritPhase2Frequencies, source.EspritPhase2Frequencies);
            ReplaceIfNonEmpty(target.EspritPhase3Frequencies, source.EspritPhase3Frequencies);

            if (!string.IsNullOrEmpty(source.Mode))
                target.Mode = source.Mode;
            if (!string.IsNullOrEmpty(source.FinalReportSummary))
                target.FinalReportSummary = source.FinalReportSummary;

            if (source.ReportTelemetry != null &&
                (source.ReportTelemetry.BPFO_Hz != 0f || source.ReportTelemetry.FaultIndex != 0f ||
                 source.ReportTelemetry.Stator_NSR != 0f))
                target.ReportTelemetry = source.ReportTelemetry;

            if (source.ReportMotorParams != null)
                target.ReportMotorParams = source.ReportMotorParams;
        }

        private static void ReplaceIfNonEmpty<T>(List<T> target, List<T> source)
        {
            if (source == null || source.Count == 0) return;
            target.Clear();
            target.AddRange(source);
        }

        private static SpectralFrame CloneFrame(SpectralFrame src)
        {
            SpectralFrame f = new SpectralFrame
            {
                Mode = src.Mode,
                FinalReportSummary = src.FinalReportSummary,
                IsComplete = src.IsComplete,
                ReportMotorParams = src.ReportMotorParams,
                ReportTelemetry = src.ReportTelemetry
            };
            f.FourierPoints.AddRange(src.FourierPoints);
            f.FourierPhase1Points.AddRange(src.FourierPhase1Points);
            f.FourierPhase2Points.AddRange(src.FourierPhase2Points);
            f.FourierPhase3Points.AddRange(src.FourierPhase3Points);
            f.MusicPoints.AddRange(src.MusicPoints);
            f.MusicPhase1Points.AddRange(src.MusicPhase1Points);
            f.MusicPhase2Points.AddRange(src.MusicPhase2Points);
            f.MusicPhase3Points.AddRange(src.MusicPhase3Points);
            f.Cyclic2Points.AddRange(src.Cyclic2Points);
            f.Cyclic2Phase1Points.AddRange(src.Cyclic2Phase1Points);
            f.Cyclic2Phase2Points.AddRange(src.Cyclic2Phase2Points);
            f.Cyclic2Phase3Points.AddRange(src.Cyclic2Phase3Points);
            f.SkPoints.AddRange(src.SkPoints);
            f.SkPhase1Points.AddRange(src.SkPhase1Points);
            f.SkPhase2Points.AddRange(src.SkPhase2Points);
            f.SkPhase3Points.AddRange(src.SkPhase3Points);
            f.WelchPoints.AddRange(src.WelchPoints);
            f.CoherencePoints.AddRange(src.CoherencePoints);
            f.WaveletPoints.AddRange(src.WaveletPoints);
            f.WaveletPhase1Points.AddRange(src.WaveletPhase1Points);
            f.WaveletPhase2Points.AddRange(src.WaveletPhase2Points);
            f.WaveletPhase3Points.AddRange(src.WaveletPhase3Points);
            f.EspritFrequencies.AddRange(src.EspritFrequencies);
            f.EspritPhase1Frequencies.AddRange(src.EspritPhase1Frequencies);
            f.EspritPhase2Frequencies.AddRange(src.EspritPhase2Frequencies);
            f.EspritPhase3Frequencies.AddRange(src.EspritPhase3Frequencies);
            return f;
        }

        private bool TrySetNativeFftTargetFromLine(string line)
        {
            if (line.StartsWith("### END_GRAPHDATA"))
            {
                _activeNativeFftTarget = null;
                return false;
            }

            Match sectionMatch = Regex.Match(line, @"^### SECTION (\S+) NATIVE_FFT\b", RegexOptions.IgnoreCase);
            if (sectionMatch.Success)
            {
                string section = sectionMatch.Groups[1].Value.ToUpperInvariant();
                if (section == "PHASE_A")
                    _activeNativeFftTarget = _current.FourierPhase1Points;
                else if (section == "PHASE_B")
                    _activeNativeFftTarget = _current.FourierPhase2Points;
                else if (section == "PHASE_C")
                    _activeNativeFftTarget = _current.FourierPhase3Points;
                else
                    _activeNativeFftTarget = null;

                return true;
            }

            if (line.StartsWith("[PHASE_A_NATIVE_FFT_CSV]", StringComparison.OrdinalIgnoreCase))
            {
                _activeNativeFftTarget = _current.FourierPhase1Points;
                return true;
            }
            if (line.StartsWith("[PHASE_B_NATIVE_FFT_CSV]", StringComparison.OrdinalIgnoreCase))
            {
                _activeNativeFftTarget = _current.FourierPhase2Points;
                return true;
            }
            if (line.StartsWith("[PHASE_C_NATIVE_FFT_CSV]", StringComparison.OrdinalIgnoreCase))
            {
                _activeNativeFftTarget = _current.FourierPhase3Points;
                return true;
            }
            if (line.StartsWith("[MEAN_3PH_NATIVE_FFT_CSV]", StringComparison.OrdinalIgnoreCase))
            {
                _activeNativeFftTarget = _current.FourierPoints;
                return true;
            }

            if (line.StartsWith("### "))
                _activeNativeFftTarget = null;

            return false;
        }

        private bool TryParseNativeFftDataLine(string line)
        {
            if (_activeNativeFftTarget == null) return false;
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.StartsWith("bin,", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.StartsWith("sample_idx,", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.StartsWith("###")) return false;
            if (line.StartsWith("[")) return false;

            // Expected format from main.c native FFT rows:
            // bin_or_index,f_hz,magnitude
            string[] parts = line.Split(',');
            if (parts.Length < 3) return false;

            float fHz, mag;
            if (TryParseFloat(parts[1], out fHz) && TryParseFloat(parts[2], out mag))
            {
                _activeNativeFftTarget.Add(new PointF(fHz, mag));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parses spectrum CSV tags emitted by main.c (USB_Send_CsvSpectrumRows and sections).
        /// </summary>
        private bool TryParseMainCSpectrumTag(string line)
        {
            if (!line.StartsWith("[")) return false;
            int close = line.IndexOf(']');
            if (close <= 1) return false;

            string tag = line.Substring(0, close + 1);
            string tagInner = line.Substring(1, close - 1).Trim().ToUpperInvariant();

            switch (tagInner)
            {
                case "FOURIER_CSV":
                    ParseCsvPairs(line, tag, _current.FourierPoints);
                    return true;
                case "FOURIER_P1_CSV":
                    ParseCsvPairs(line, tag, _current.FourierPhase1Points);
                    return true;
                case "FOURIER_P2_CSV":
                    ParseCsvPairs(line, tag, _current.FourierPhase2Points);
                    return true;
                case "FOURIER_P3_CSV":
                    ParseCsvPairs(line, tag, _current.FourierPhase3Points);
                    return true;
                case "MUSIC_CSV":
                    ParseCsvPairs(line, tag, _current.MusicPoints);
                    return true;
                case "MUSIC_P1_CSV":
                    ParseCsvPairs(line, tag, _current.MusicPhase1Points);
                    return true;
                case "MUSIC_P2_CSV":
                    ParseCsvPairs(line, tag, _current.MusicPhase2Points);
                    return true;
                case "MUSIC_P3_CSV":
                    ParseCsvPairs(line, tag, _current.MusicPhase3Points);
                    return true;
                case "CYCLIC2_CSV":
                    ParseCsvPairs(line, tag, _current.Cyclic2Points);
                    return true;
                case "CYCLIC2_P1_CSV":
                    ParseCsvPairs(line, tag, _current.Cyclic2Phase1Points);
                    return true;
                case "CYCLIC2_P2_CSV":
                    ParseCsvPairs(line, tag, _current.Cyclic2Phase2Points);
                    return true;
                case "CYCLIC2_P3_CSV":
                    ParseCsvPairs(line, tag, _current.Cyclic2Phase3Points);
                    return true;
                case "SK_CSV":
                    ParseCsvPairs(line, tag, _current.SkPoints);
                    return true;
                case "SK_P1_CSV":
                    ParseCsvPairs(line, tag, _current.SkPhase1Points);
                    return true;
                case "SK_P2_CSV":
                    ParseCsvPairs(line, tag, _current.SkPhase2Points);
                    return true;
                case "SK_P3_CSV":
                    ParseCsvPairs(line, tag, _current.SkPhase3Points);
                    return true;
                case "WELCH_CSV":
                    ParseCsvPairs(line, tag, _current.WelchPoints);
                    return true;
                case "COH_CSV":
                    ParseCsvPairs(line, tag, _current.CoherencePoints);
                    return true;
                case "WAVELET_CSV":
                    ParseWaveletLineAllPhases(line);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryParsePhaseCsv(string line)
        {
            int close = line.IndexOf(']');
            if (!line.StartsWith("[") || close <= 1) return false;

            string tag = line.Substring(1, close - 1).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(tag)) return false;

            // Supported H750 tag variants:
            // [FOURIER_P1_CSV], [FOURIER_PHASE1_CSV], [P1_FOURIER_CSV], etc.
            int phase = 0;
            if (tag.Contains("PHASE1") || tag.Contains("_P1_") || tag.StartsWith("P1_") || tag.EndsWith("_P1")) phase = 1;
            else if (tag.Contains("PHASE2") || tag.Contains("_P2_") || tag.StartsWith("P2_") || tag.EndsWith("_P2")) phase = 2;
            else if (tag.Contains("PHASE3") || tag.Contains("_P3_") || tag.StartsWith("P3_") || tag.EndsWith("_P3")) phase = 3;
            else return false;

            string prefix = line.Substring(0, close + 1);
            if (tag.Contains("FOURIER"))
            {
                ParseCsvPairs(line, prefix, GetPhaseTarget("FOURIER", phase));
                return true;
            }
            if (tag.Contains("MUSIC"))
            {
                ParseCsvPairs(line, prefix, GetPhaseTarget("MUSIC", phase));
                return true;
            }
            if (tag.Contains("CYCLIC2") || tag.Contains("CYC2") || tag.Contains("CYCLIC"))
            {
                ParseCsvPairs(line, prefix, GetPhaseTarget("CYCLIC2", phase));
                return true;
            }
            if (tag.Contains("SK") || tag.Contains("KURTOSIS"))
            {
                ParseCsvPairs(line, prefix, GetPhaseTarget("SK", phase));
                return true;
            }
            if (tag.Contains("WAVELET"))
            {
                List<PointF> wavTarget = GetPhaseTarget("WAVELET", phase);
                // Try key=value format (F_HZ=... EMEAN=...) first, then CSV pairs
                if (line.Contains("F_HZ=") && line.Contains("EMEAN="))
                    ParseWaveletLine(line, wavTarget);
                else
                    ParseCsvPairs(line, prefix, wavTarget);
                return true;
            }
            if (tag.Contains("ESPRIT"))
            {
                ParseEspritPhaseFromTag(line, prefix, phase);
                return true;
            }

            return false;
        }

        private List<PointF> GetPhaseTarget(string technique, int phase)
        {
            if (technique == "FOURIER")
            {
                if (phase == 1) return _current.FourierPhase1Points;
                if (phase == 2) return _current.FourierPhase2Points;
                return _current.FourierPhase3Points;
            }
            if (technique == "MUSIC")
            {
                if (phase == 1) return _current.MusicPhase1Points;
                if (phase == 2) return _current.MusicPhase2Points;
                return _current.MusicPhase3Points;
            }
            if (technique == "SK")
            {
                if (phase == 1) return _current.SkPhase1Points;
                if (phase == 2) return _current.SkPhase2Points;
                return _current.SkPhase3Points;
            }
            if (technique == "WAVELET")
            {
                if (phase == 1) return _current.WaveletPhase1Points;
                if (phase == 2) return _current.WaveletPhase2Points;
                return _current.WaveletPhase3Points;
            }

            if (phase == 1) return _current.Cyclic2Phase1Points;
            if (phase == 2) return _current.Cyclic2Phase2Points;
            return _current.Cyclic2Phase3Points;
        }

        public SpectralFrame GetLastFrame()
        {
            lock (_lock)
            {
                if (_lastComplete != null)
                    return _lastComplete;
                if (_current != null && FrameHasSpectrumData(_current))
                    return _current;
                return null;
            }
        }

        /// <summary>
        /// If END_EXPORT was missed (host idle timeout), still publish parsed CSV data.
        /// </summary>
        public void FinalizeOpenExport(string mode)
        {
            lock (_lock)
            {
                if (_current == null || !FrameHasSpectrumData(_current))
                    return;
                if (!string.IsNullOrEmpty(mode))
                    _current.Mode = mode;
                if (string.IsNullOrEmpty(_current.FinalReportSummary))
                    _current.FinalReportSummary = "partial export (END_EXPORT not seen)";
                CompleteCurrentFrame();
            }
        }

        private static bool FrameHasSpectrumData(SpectralFrame f)
        {
            if (f == null) return false;
            return f.FourierPoints.Count > 0 || f.MusicPoints.Count > 0 ||
                   f.Cyclic2Points.Count > 0 || f.SkPoints.Count > 0 ||
                   f.WelchPoints.Count > 0 || f.CoherencePoints.Count > 0 ||
                   f.WaveletPoints.Count > 0 ||
                   f.WaveletPhase1Points.Count > 0 || f.WaveletPhase2Points.Count > 0 || f.WaveletPhase3Points.Count > 0 ||
                   f.EspritFrequencies.Count > 0 ||
                   f.FourierPhase1Points.Count > 0 || f.MusicPhase1Points.Count > 0;
        }

        private void ParseCsvPairs(string line, string prefix, List<PointF> target)
        {
            string data = line.Substring(prefix.Length).Trim();
            if (string.IsNullOrEmpty(data)) return;

            string[] pairs = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i].Trim();
                if (string.IsNullOrEmpty(pair)) continue;

                int comma = pair.IndexOf(',');
                if (comma <= 0 || comma >= pair.Length - 1) continue;

                string freqStr = pair.Substring(0, comma);
                string magStr = pair.Substring(comma + 1);

                float freq, mag;
                if (TryParseFloat(freqStr, out freq) && TryParseFloat(magStr, out mag))
                {
                    target.Add(new PointF(freq, mag));
                }
            }
        }

        /// <summary>
        /// Parses [WAVELET_CSV] lines and extracts ALL per-phase energy data.
        /// The STM32 sends: [WAVELET_CSV] LEV=N F_HZ=xxx EA=xxx EB=xxx EC=xxx EMEAN=xxx
        /// EA = Phase A energy, EB = Phase B, EC = Phase C, EMEAN = average.
        /// This populates WaveletPoints (EMEAN), WaveletPhase1Points (EA),
        /// WaveletPhase2Points (EB), and WaveletPhase3Points (EC) so the
        /// harmonics chart can overlay all 3 phases in one figure.
        /// </summary>
        private void ParseWaveletLineAllPhases(string line)
        {
            Match mf = Regex.Match(line, @"F_HZ=(\S+)");
            Match me = Regex.Match(line, @"EMEAN=(\S+)");
            Match ma = Regex.Match(line, @"EA=(\S+)");
            Match mb = Regex.Match(line, @"EB=(\S+)");
            Match mc = Regex.Match(line, @"EC=(\S+)");
            if (!mf.Success || !me.Success) return;

            float freq, emean;
            if (!TryParseFloat(mf.Groups[1].Value, out freq) ||
                !TryParseFloat(me.Groups[1].Value, out emean))
                return;

            _current.WaveletPoints.Add(new PointF(freq, emean));

            // Extract per-phase energies if present (EA, EB, EC)
            float ea, eb, ec;
            if (ma.Success && TryParseFloat(ma.Groups[1].Value, out ea))
                _current.WaveletPhase1Points.Add(new PointF(freq, ea));
            if (mb.Success && TryParseFloat(mb.Groups[1].Value, out eb))
                _current.WaveletPhase2Points.Add(new PointF(freq, eb));
            if (mc.Success && TryParseFloat(mc.Groups[1].Value, out ec))
                _current.WaveletPhase3Points.Add(new PointF(freq, ec));
        }

        /// <summary>
        /// Parses a [WAVELET_CSV] (or per-phase variant) line into a single target list.
        /// Used by TryParsePhaseCsv for [WAVELET_P1_CSV] etc. tags.
        /// </summary>
        private void ParseWaveletLine(string line, List<PointF> target)
        {
            Match mf = Regex.Match(line, @"F_HZ=(\S+)");
            Match me = Regex.Match(line, @"EMEAN=(\S+)");
            if (!mf.Success || !me.Success) return;

            float freq, emean;
            if (TryParseFloat(mf.Groups[1].Value, out freq) &&
                TryParseFloat(me.Groups[1].Value, out emean))
            {
                target.Add(new PointF(freq, emean));
            }
        }

        private void ParseScalarLine(string line)
        {
            if (_current == null)
                _current = new SpectralFrame();

            if (_current.ReportTelemetry == null)
                _current.ReportTelemetry = new TelemetryData();

            TelemetryData t = _current.ReportTelemetry;
            Dictionary<string, string> kv = ParseKeyValuePairs(line);
            if (kv.Count == 0) return;

            bool isBearingLine = line.StartsWith("[BEARING]");
            bool isStatorLine = line.StartsWith("[STATOR]");
            bool isParamsLine = line.StartsWith("[PARAMS]");

            float fv;
            int iv;
            byte bv;

            if (isBearingLine)
            {
                if (kv.ContainsKey("BPFO_HZ") && TryParseFloat(kv["BPFO_HZ"], out fv)) t.BPFO_Hz = fv;
                if (kv.ContainsKey("BPFI_HZ") && TryParseFloat(kv["BPFI_HZ"], out fv)) t.BPFI_Hz = fv;
                if (kv.ContainsKey("BSF_HZ") && TryParseFloat(kv["BSF_HZ"], out fv)) t.BSF_Hz = fv;
                if (kv.ContainsKey("FTF_HZ") && TryParseFloat(kv["FTF_HZ"], out fv)) t.FTF_Hz = fv;
                if (kv.ContainsKey("FI") && TryParseFloat(kv["FI"], out fv)) t.FaultIndex = fv;
                if (kv.ContainsKey("FI_EMA") && TryParseFloat(kv["FI_EMA"], out fv)) t.FaultIndex_Ema = fv;
                if (kv.ContainsKey("EMA") && TryParseFloat(kv["EMA"], out fv)) t.FaultIndex_Ema = fv;
                if (kv.ContainsKey("CUSUM") && TryParseFloat(kv["CUSUM"], out fv)) t.CusumScore = fv;
                if (kv.ContainsKey("LV") && byte.TryParse(kv["LV"], out bv)) t.FaultLevel = bv;
                if (kv.ContainsKey("LS") && TryParseFloat(kv["LS"], out fv)) t.Index_LS = fv;
                if (kv.ContainsKey("MI") && TryParseFloat(kv["MI"], out fv)) t.Index_Music = fv;
                if (kv.ContainsKey("ES") && TryParseFloat(kv["ES"], out fv)) t.Index_Esprit = fv;
                if (kv.ContainsKey("TK") && TryParseFloat(kv["TK"], out fv)) t.Index_Teager = fv;
                if (kv.ContainsKey("SK") && TryParseFloat(kv["SK"], out fv)) t.Index_SK = fv;
                if (kv.ContainsKey("WV") && TryParseFloat(kv["WV"], out fv)) t.Index_Wavelet = fv;
                if (kv.ContainsKey("CY") && TryParseFloat(kv["CY"], out fv)) t.Index_Cyclic = fv;
                if (kv.ContainsKey("SB") && TryParseFloat(kv["SB"], out fv)) t.Index_Sideband = fv;
                if (kv.ContainsKey("ACF") && TryParseFloat(kv["ACF"], out fv)) t.Index_EnvAcf = fv;
                if (kv.ContainsKey("WL") && TryParseFloat(kv["WL"], out fv)) t.Index_Welch = fv;
                if (kv.ContainsKey("COH") && TryParseFloat(kv["COH"], out fv)) t.Index_Coherence = fv;
                if (kv.ContainsKey("PF_O") && TryParseFloat(kv["PF_O"], out fv)) t.Index_Bpfo = fv;
                if (kv.ContainsKey("PF_I") && TryParseFloat(kv["PF_I"], out fv)) t.Index_Bpfi = fv;
                if (kv.ContainsKey("PF_B") && TryParseFloat(kv["PF_B"], out fv)) t.Index_Bsf = fv;
                if (kv.ContainsKey("PF_T") && TryParseFloat(kv["PF_T"], out fv)) t.Index_Ftf = fv;
                if (kv.ContainsKey("DOM") && byte.TryParse(kv["DOM"], out bv)) t.DominantFault = bv;
                if (kv.ContainsKey("SKPK") && TryParseFloat(kv["SKPK"], out fv)) t.SkPeak = fv;
                if (kv.ContainsKey("SKPK_HZ") && TryParseFloat(kv["SKPK_HZ"], out fv)) t.SkPeakHz = fv;
                if (kv.ContainsKey("KB_HZ") && int.TryParse(kv["KB_HZ"], out iv)) t.KurtBandHz = iv;
                else if (kv.ContainsKey("KB") && int.TryParse(kv["KB"], out iv)) t.KurtBandHz = iv;
            }

            if (isParamsLine)
            {
                if (_current.ReportMotorParams == null)
                    _current.ReportMotorParams = new MotorParameters();
                MotorParameters mp = _current.ReportMotorParams;
                if (kv.ContainsKey("RPM") && TryParseFloat(kv["RPM"], out fv)) mp.MotorRPM = fv;
                if (kv.ContainsKey("SLIP") && TryParseFloat(kv["SLIP"], out fv)) mp.Slip = fv;
                if (kv.ContainsKey("BPFO") && TryParseFloat(kv["BPFO"], out fv)) mp.BPFO = fv;
                if (kv.ContainsKey("BPFI") && TryParseFloat(kv["BPFI"], out fv)) mp.BPFI = fv;
                if (kv.ContainsKey("BSF") && TryParseFloat(kv["BSF"], out fv)) mp.BSF = fv;
                if (kv.ContainsKey("FTF") && TryParseFloat(kv["FTF"], out fv)) mp.FTF = fv;
                if (kv.ContainsKey("LINE_HZ") && TryParseFloat(kv["LINE_HZ"], out fv)) mp.SupplyLineHz = fv;
                else if (kv.ContainsKey("LINE") && TryParseFloat(kv["LINE"], out fv)) mp.SupplyLineHz = fv;
            }

            if (isStatorLine)
            {
                if (kv.ContainsKey("F") && TryParseFloat(kv["F"], out fv)) t.Stator_FrequencyHz = fv;
                if (kv.ContainsKey("SH") && int.TryParse(kv["SH"], out iv)) t.Stator_ShortLevel = iv;
                if (kv.ContainsKey("GD") && int.TryParse(kv["GD"], out iv)) t.Stator_GndLevel = iv;
                if (kv.ContainsKey("ES") && int.TryParse(kv["ES"], out iv)) t.Stator_EarlyShort = iv;
                if (kv.ContainsKey("EG") && int.TryParse(kv["EG"], out iv)) t.Stator_EarlyGnd = iv;
                if (kv.ContainsKey("NSR") && TryParseFloat(kv["NSR"], out fv)) t.Stator_NSR = fv;
                if (kv.ContainsKey("ZSR") && TryParseFloat(kv["ZSR"], out fv)) t.Stator_ZSR = fv;
                if (kv.ContainsKey("NSR_TD") && TryParseFloat(kv["NSR_TD"], out fv)) t.Stator_NsrTd = fv;
                if (kv.ContainsKey("ZSR_TD") && TryParseFloat(kv["ZSR_TD"], out fv)) t.Stator_ZsrTd = fv;
                if (kv.ContainsKey("NSR_H5") && TryParseFloat(kv["NSR_H5"], out fv)) t.Stator_NsrH5 = fv;
                if (kv.ContainsKey("HARM") && TryParseFloat(kv["HARM"], out fv)) t.Stator_HarmRatio = fv;
                else if (kv.ContainsKey("H") && TryParseFloat(kv["H"], out fv)) t.Stator_HarmRatio = fv;
                if (kv.ContainsKey("ODD") && TryParseFloat(kv["ODD"], out fv)) t.Stator_OddHarm = fv;
                if (kv.ContainsKey("PS_DEG") && TryParseFloat(kv["PS_DEG"], out fv)) t.Stator_PhaseSpreadDeg = fv;
                if (kv.ContainsKey("RESID") && TryParseFloat(kv["RESID"], out fv)) t.Stator_ResidRatio = fv;
                else if (kv.ContainsKey("R") && TryParseFloat(kv["R"], out fv)) t.Stator_ResidRatio = fv;
                if (kv.ContainsKey("ZSR_H3") && TryParseFloat(kv["ZSR_H3"], out fv)) t.Stator_ZSR_H3 = fv;
                else if (kv.ContainsKey("Z3") && TryParseFloat(kv["Z3"], out fv)) t.Stator_ZSR_H3 = fv;
                if (kv.ContainsKey("IMB_PCT") && TryParseFloat(kv["IMB_PCT"], out fv)) t.Stator_Imbalance = fv;
                else if (kv.ContainsKey("IMB") && TryParseFloat(kv["IMB"], out fv)) t.Stator_Imbalance = fv;
                if (kv.ContainsKey("SI") && TryParseFloat(kv["SI"], out fv)) t.Stator_ShortIndex = fv;
                if (kv.ContainsKey("GI") && TryParseFloat(kv["GI"], out fv)) t.Stator_GndIndex = fv;
                if (kv.ContainsKey("SI_EMA") && TryParseFloat(kv["SI_EMA"], out fv)) t.Stator_ShortIndexEma = fv;
                if (kv.ContainsKey("GI_EMA") && TryParseFloat(kv["GI_EMA"], out fv)) t.Stator_GndIndexEma = fv;
                if (kv.ContainsKey("CS") && TryParseFloat(kv["CS"], out fv)) t.Stator_CusumShort = fv;
                if (kv.ContainsKey("CG") && TryParseFloat(kv["CG"], out fv)) t.Stator_CusumGnd = fv;
                if (kv.ContainsKey("I0") && TryParseFloat(kv["I0"], out fv)) t.Stator_I0Mag = fv;
                if (kv.ContainsKey("I1") && TryParseFloat(kv["I1"], out fv)) t.Stator_I1Mag = fv;
                if (kv.ContainsKey("I2") && TryParseFloat(kv["I2"], out fv)) t.Stator_I2Mag = fv;
                if (kv.ContainsKey("SLV") && int.TryParse(kv["SLV"], out iv)) t.Stator_FaultLevel = iv;
                else if (kv.ContainsKey("LV") && int.TryParse(kv["LV"], out iv)) t.Stator_FaultLevel = iv;
            }

            if (kv.ContainsKey("TEMP") && TryParseFloat(kv["TEMP"], out fv))
            {
                t.TemperatureC = fv;
                t.HasTemperature = true;
            }
            else if (kv.ContainsKey("TEMP_C") && TryParseFloat(kv["TEMP_C"], out fv))
            {
                t.TemperatureC = fv;
                t.HasTemperature = true;
            }
        }

        /// <summary>
        /// Parses: [ESPRIT_HZ] N=3 123.456 234.567 345.678
        /// </summary>
        private void ParseEspritHzLine(string line, List<float> target)
        {
            int close = line.IndexOf(']');
            string data = (close > 0 && close < line.Length - 1)
                ? line.Substring(close + 1).Trim()
                : line.Trim();
            if (string.IsNullOrEmpty(data)) return;

            string[] tokens = data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i].Trim();
                if (tok.StartsWith("N=", StringComparison.OrdinalIgnoreCase)) continue;

                float f;
                if (TryParseFloat(tok, out f) && f > 0f)
                {
                    if (!target.Contains(f))
                        target.Add(f);
                }
            }
        }

        /// <summary>
        /// Parses: [ESPRIT_CSV] OK=1;N=3;ES_IDX=1.234;0,123.456;1,234.567;2,345.678
        /// Each ";K,F_HZ" pair after the header tokens contains an index and frequency.
        /// </summary>
        private void ParseEspritCsvLine(string line, List<float> target)
        {
            string data = line.Substring("[ESPRIT_CSV]".Length).Trim();
            if (string.IsNullOrEmpty(data)) return;

            string[] parts = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();

                // Skip header tokens like OK=1, N=3, ES_IDX=1.234
                if (part.Contains("=") && !part.Contains(",")) continue;

                // Expect "index,frequency" pairs e.g. "0,123.456"
                int comma = part.IndexOf(',');
                if (comma <= 0 || comma >= part.Length - 1) continue;

                string freqStr = part.Substring(comma + 1);
                float f;
                if (TryParseFloat(freqStr, out f) && f > 0f)
                {
                    if (!target.Contains(f))
                        target.Add(f);
                }
            }
        }

        /// <summary>
        /// Parses: [ESPRIT_TSV] 123.456\t234.567\t345.678
        /// Tab-separated frequency values.
        /// </summary>
        private void ParseEspritTsvLine(string line, List<float> target)
        {
            string data = line.Substring("[ESPRIT_TSV]".Length).Trim();
            if (string.IsNullOrEmpty(data)) return;

            string[] tokens = data.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                float f;
                if (TryParseFloat(tokens[i].Trim(), out f) && f > 0f)
                {
                    if (!target.Contains(f))
                        target.Add(f);
                }
            }
        }

        /// <summary>
        /// Parses per-phase ESPRIT frequency data from a tagged line.
        /// Expected format: [ESPRIT_P1_CSV] 0,123.456;1,234.567 or [ESPRIT_PHASE1_HZ] 123.456 234.567
        /// </summary>
        private void ParseEspritPhaseFromTag(string line, string prefix, int phase)
        {
            List<float> target;
            if (phase == 1) target = _current.EspritPhase1Frequencies;
            else if (phase == 2) target = _current.EspritPhase2Frequencies;
            else target = _current.EspritPhase3Frequencies;

            string data = line.Substring(prefix.Length).Trim();
            if (string.IsNullOrEmpty(data)) return;

            // Try CSV pair format first: "0,123.456;1,234.567"
            if (data.Contains(";") || data.Contains(","))
            {
                string[] parts = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i].Trim();
                    if (part.Contains("=") && !part.Contains(",")) continue;

                    int comma = part.IndexOf(',');
                    if (comma > 0 && comma < part.Length - 1)
                    {
                        string freqStr = part.Substring(comma + 1);
                        float f;
                        if (TryParseFloat(freqStr, out f) && f > 0f)
                        {
                            if (!target.Contains(f))
                                target.Add(f);
                        }
                    }
                    else
                    {
                        float f;
                        if (TryParseFloat(part, out f) && f > 0f)
                        {
                            if (!target.Contains(f))
                                target.Add(f);
                        }
                    }
                }
            }
            else
            {
                // Space/tab-separated frequencies
                string[] tokens = data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tokens.Length; i++)
                {
                    string tok = tokens[i].Trim();
                    if (tok.StartsWith("N=", StringComparison.OrdinalIgnoreCase)) continue;
                    float f;
                    if (TryParseFloat(tok, out f) && f > 0f)
                    {
                        if (!target.Contains(f))
                            target.Add(f);
                    }
                }
            }
        }

        private static bool TryParseFloat(string s, out float result)
        {
            result = 0f;
            if (string.IsNullOrEmpty(s)) return false;

            if (float.TryParse(s, NumberStyles.Float, CI, out result))
                return true;

            var sb = new System.Text.StringBuilder(s.Length);
            bool hasDot = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') sb.Append(c);
                else if (c == '.' && !hasDot) { sb.Append(c); hasDot = true; }
                else if (c == '-' && i == 0) sb.Append(c);
                else if ((c == 'e' || c == 'E') && i > 0) sb.Append(c);
            }
            return sb.Length > 0 &&
                   float.TryParse(sb.ToString(), NumberStyles.Float, CI, out result);
        }

        private static Dictionary<string, string> ParseKeyValuePairs(string line)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection matches = Regex.Matches(line, @"([A-Za-z_][A-Za-z0-9_]*)=(\S+)");
            foreach (Match m in matches)
            {
                result[m.Groups[1].Value.ToUpperInvariant()] = m.Groups[2].Value;
            }
            return result;
        }
    }
}