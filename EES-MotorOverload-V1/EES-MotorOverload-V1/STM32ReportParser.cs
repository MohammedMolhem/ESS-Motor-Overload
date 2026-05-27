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
        public List<PointF> MusicPoints = new List<PointF>();
        public List<PointF> Cyclic2Points = new List<PointF>();
        public List<PointF> WaveletPoints = new List<PointF>();
        public List<PointF> SkPoints = new List<PointF>();
        public List<float> EspritFrequencies = new List<float>();
        public bool IsComplete = false;
        public string Mode { get; set; }
        public TelemetryData ReportTelemetry { get; set; } = new TelemetryData();

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

        public event Action<SpectralFrame> OnFrameReady;

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public void ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            lock (_lock)
            {
                if (line.StartsWith("### H750_DSP"))
                {
                    _current = new SpectralFrame();
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
                    line.StartsWith("### H750_FULL_REPORT") ||
                    line.StartsWith("### BEGIN_GRAPHDATA") ||
                    line.StartsWith("### END_GRAPHDATA") ||
                    line.StartsWith("### END_FULL_REPORT") ||
                    line.StartsWith("### SECTION") ||
                    line.StartsWith("### PROGRESS"))
                {
                    ParseScalarLine(line);
                    return;
                }

                if (line.StartsWith("### END_REPORT"))
                {
                    _current.FinalReportSummary = "END_REPORT (full DSP spectral export)";
                    _current.IsComplete = true;
                    _lastComplete = _current;
                    OnFrameReady?.Invoke(_current);
                    _current = new SpectralFrame();
                    return;
                }

                if (line.StartsWith("### END_EXPORT"))
                {
                    Match mm = Regex.Match(line, @"MODE=(\S+)");
                    string mode = mm.Success ? mm.Groups[1].Value.Trim() : "";
                    _current.FinalReportSummary = string.IsNullOrEmpty(mode)
                        ? "END_EXPORT (single technique)"
                        : "END_EXPORT MODE=" + mode;
                    _current.IsComplete = true;
                    _lastComplete = _current;
                    OnFrameReady?.Invoke(_current);
                    _current = new SpectralFrame();
                    return;
                }

                if (line.StartsWith("[FOURIER_CSV]"))
                {
                    ParseCsvPairs(line, "[FOURIER_CSV]", _current.FourierPoints);
                }
                else if (line.StartsWith("[MUSIC_CSV]"))
                {
                    ParseCsvPairs(line, "[MUSIC_CSV]", _current.MusicPoints);
                }
                else if (line.StartsWith("[CYCLIC2_CSV]"))
                {
                    ParseCsvPairs(line, "[CYCLIC2_CSV]", _current.Cyclic2Points);
                }
                else if (line.StartsWith("[WAVELET_CSV]"))
                {
                    ParseWaveletLine(line, _current.WaveletPoints);
                }
                else if (line.StartsWith("[SK_CSV]"))
                {
                    ParseCsvPairs(line, "[SK_CSV]", _current.SkPoints);
                }
                else if (line.StartsWith("[ESPRIT_HZ]"))
                {
                    ParseEspritHzLine(line, _current.EspritFrequencies);
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

        public SpectralFrame GetLastFrame()
        {
            lock (_lock) { return _lastComplete; }
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

            if (isStatorLine)
            {
                if (kv.ContainsKey("F") && TryParseFloat(kv["F"], out fv)) t.Stator_FrequencyHz = fv;
                if (kv.ContainsKey("SH") && int.TryParse(kv["SH"], out iv)) t.Stator_ShortLevel = iv;
                if (kv.ContainsKey("GD") && int.TryParse(kv["GD"], out iv)) t.Stator_GndLevel = iv;
                if (kv.ContainsKey("NSR") && TryParseFloat(kv["NSR"], out fv)) t.Stator_NSR = fv;
                if (kv.ContainsKey("ZSR") && TryParseFloat(kv["ZSR"], out fv)) t.Stator_ZSR = fv;
                if (kv.ContainsKey("H") && TryParseFloat(kv["H"], out fv)) t.Stator_HarmRatio = fv;
                if (kv.ContainsKey("R") && TryParseFloat(kv["R"], out fv)) t.Stator_ResidRatio = fv;
                if (kv.ContainsKey("Z3") && TryParseFloat(kv["Z3"], out fv)) t.Stator_ZSR_H3 = fv;
                if (kv.ContainsKey("IMB") && TryParseFloat(kv["IMB"], out fv)) t.Stator_Imbalance = fv;
                if (kv.ContainsKey("LV") && int.TryParse(kv["LV"], out iv)) t.Stator_FaultLevel = iv;
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
            string data = line.Substring("[ESPRIT_HZ]".Length).Trim();
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