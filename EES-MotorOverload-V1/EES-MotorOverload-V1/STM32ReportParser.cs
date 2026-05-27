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
        public List<float> EspritFrequencies = new List<float>();
        public bool IsComplete = false;

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
                if (line.StartsWith("### H750_DSP_REPORT"))
                {
                    _current = new SpectralFrame();
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
    }
}