using System;
using NAudio.Wave;

namespace Surround71.Audio
{
    /// <summary>均衡器滤波器类型（对应图 5 每个 Band 的类型选择）。</summary>
    public enum EqFilterType
    {
        LowShelf = 0,   // 低架（提升/衰减低频整体）
        Peak = 1,       // 峰值（只动中心频率附近）
        HighShelf = 2,  // 高架
        Notch = 3,      // 带阻（削掉某段）
    }

    /// <summary>六段参数均衡器的频段定义与取值范围。</summary>
    public static class EqBands
    {
        public const int Count = 6;

        /// <summary>默认中心频率（与图 5 的 6 个 Band 一致：70 / 115 / 240 / 1.2K / 6.0K / 22.0K）。</summary>
        public static readonly float[] DefaultFreq = { 70f, 115f, 240f, 1200f, 6000f, 12000f };

        /// <summary>每个 Band 的频率可调范围（图 5 里各段的下限/上限）。</summary>
        public static readonly float[] MinFreq = { 30f, 30f, 100f, 450f, 1400f, 1400f };
        public static readonly float[] MaxFreq = { 399f, 399f, 1500f, 8000f, 22000f, 16000f };

        /// <summary>默认类型：两端用架子滤波，中间用峰值。</summary>
        public static readonly EqFilterType[] DefaultType =
        {
            EqFilterType.LowShelf, EqFilterType.Peak, EqFilterType.Peak,
            EqFilterType.Peak, EqFilterType.Peak, EqFilterType.HighShelf
        };

        /// <summary>Q 值（峰值/带阻的带宽）。</summary>
        public const float Q = 1.4f;

        /// <summary>单段增益范围 dB。</summary>
        public const float MaxDb = 20f;

        /// <summary>总增益范围 dB（与图 5 右侧一致）。</summary>
        public const float PreampMaxDb = 20f;

        /// <summary>频率刻度的显示文字（与图 5 横轴一致）。</summary>
        public static readonly float[] AxisFreq = { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };
        public static readonly string[] AxisLabel = { "31", "62", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };

        public const float MinF = 20f;
        public const float MaxF = 22000f;

        public static string TypeName(EqFilterType t)
        {
            switch (t)
            {
                case EqFilterType.LowShelf: return "低架";
                case EqFilterType.HighShelf: return "高架";
                case EqFilterType.Notch: return "带阻";
                default: return "峰值";
            }
        }
    }

    /// <summary>各种滤波器类型的系数与频率响应（DSP 与界面曲线共用这套公式）。</summary>
    public static class EqualizerMath
    {
        /// <summary>算出某个类型在某增益下的归一化系数（b0..b2, a1..a2）。</summary>
        public static bool Coeffs(EqFilterType type, double rate, double f0, double gainDb, double q,
                                 out double b0, out double b1, out double b2,
                                 out double a1, out double a2)
        {
            b0 = 1; b1 = 0; b2 = 0; a1 = 0; a2 = 0;
            if (rate <= 0) return false;
            // 带阻不需要增益
            double fMaxAllowed = rate * 0.49;
            if (f0 <= 1 || f0 >= fMaxAllowed) return false;
            double w0 = 2.0 * Math.PI * f0 / rate;
            double cw = Math.Cos(w0), sw = Math.Sin(w0);
            double alpha = sw / (2.0 * Math.Max(0.05, q));
            double A = Math.Pow(10.0, gainDb / 40.0);
            double a0;
            switch (type)
            {
                case EqFilterType.LowShelf:
                {
                    double beta = 2.0 * Math.Sqrt(A) * alpha;
                    b0 = A * ((A + 1) - (A - 1) * cw + beta);
                    b1 = 2 * A * ((A - 1) - (A + 1) * cw);
                    b2 = A * ((A + 1) - (A - 1) * cw - beta);
                    a0 = (A + 1) + (A - 1) * cw + beta;
                    a1 = -2 * ((A - 1) + (A + 1) * cw);
                    a2 = (A + 1) + (A - 1) * cw - beta;
                    break;
                }
                case EqFilterType.HighShelf:
                {
                    double beta = 2.0 * Math.Sqrt(A) * alpha;
                    b0 = A * ((A + 1) + (A - 1) * cw + beta);
                    b1 = -2 * A * ((A - 1) + (A + 1) * cw);
                    b2 = A * ((A + 1) + (A - 1) * cw - beta);
                    a0 = (A + 1) - (A - 1) * cw + beta;
                    a1 = 2 * ((A - 1) - (A + 1) * cw);
                    a2 = (A + 1) - (A - 1) * cw - beta;
                    break;
                }
                case EqFilterType.Notch:
                {
                    b0 = 1; b1 = -2 * cw; b2 = 1;
                    a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha;
                    break;
                }
                default:
                {
                    b0 = 1 + alpha * A; b1 = -2 * cw; b2 = 1 - alpha * A;
                    a0 = 1 + alpha / A; a1 = -2 * cw; a2 = 1 - alpha / A;
                    break;
                }
            }
            if (Math.Abs(a0) < 1e-12) return false;
            double inv = 1.0 / a0;
            b0 *= inv; b1 *= inv; b2 *= inv; a1 *= inv; a2 *= inv;
            return true;
        }

        /// <summary>单个滤波器在某频率的增益 dB。</summary>
        public static double BandResponseDb(EqFilterType type, double gainDb, double freq, double f0, double q, double rate)
        {
            if (type != EqFilterType.Notch && Math.Abs(gainDb) < 0.001) return 0.0;
            if (!Coeffs(type, rate, f0, gainDb, q, out double b0, out double b1, out double b2, out double a1, out double a2))
                return 0.0;
            double w = 2.0 * Math.PI * freq / rate;
            double cw = Math.Cos(w), sw = Math.Sin(w);
            double c2w = Math.Cos(2 * w), s2w = Math.Sin(2 * w);
            double nr = b0 + b1 * cw + b2 * c2w;
            double ni = -(b1 * sw + b2 * s2w);
            double dr = 1 + a1 * cw + a2 * c2w;
            double di = -(a1 * sw + a2 * s2w);
            double num = Math.Sqrt(nr * nr + ni * ni);
            double den = Math.Sqrt(dr * dr + di * di);
            if (den < 1e-12) return 0.0;
            return 20.0 * Math.Log10(Math.Max(1e-9, num / den));
        }

        /// <summary>整条响应曲线（含总增益），用于界面画图。</summary>
        public static double CurveDb(SurroundSettings s, double freq, double rate)
        {
            double total = s.EqPreampDb;
            for (int i = 0; i < EqBands.Count; i++)
            {
                bool en = s.EqBandOn == null || i >= s.EqBandOn.Length || s.EqBandOn[i];
                if (!en) continue;
                double g = s.EqGains != null && i < s.EqGains.Length ? s.EqGains[i] : 0.0;
                double f0 = s.EqFreq != null && i < s.EqFreq.Length ? s.EqFreq[i] : EqBands.DefaultFreq[i];
                var t = s.EqType != null && i < s.EqType.Length ? (EqFilterType)s.EqType[i] : EqBands.DefaultType[i];
                total += BandResponseDb(t, g, freq, f0, EqBands.Q, rate);
            }
            return total;
        }

        /// <summary>对数频率采样出曲线（画图用）。</summary>
        public static void Curve(SurroundSettings s, double rate, int points, out double[] freqs, out double[] dbs)
        {
            freqs = new double[points];
            dbs = new double[points];
            double fMin = EqBands.MinF;
            double fMax = Math.Min(EqBands.MaxF, rate * 0.48);
            for (int i = 0; i < points; i++)
            {
                double t = points <= 1 ? 0.0 : (double)i / (points - 1);
                double f = fMin * Math.Pow(fMax / fMin, t);
                freqs[i] = f;
                dbs[i] = CurveDb(s, f, rate);
            }
        }

        /// <summary>频率 → 横轴 0..1（对数刻度）。</summary>
        public static double FreqToX(double f, double rate)
        {
            double fMin = EqBands.MinF, fMax = Math.Min(EqBands.MaxF, rate * 0.48);
            f = Math.Max(fMin, Math.Min(fMax, f));
            return Math.Log(f / fMin) / Math.Log(fMax / fMin);
        }

        /// <summary>横轴 0..1 → 频率。</summary>
        public static double XToFreq(double x, double rate)
        {
            double fMin = EqBands.MinF, fMax = Math.Min(EqBands.MaxF, rate * 0.48);
            x = Math.Max(0, Math.Min(1, x));
            return fMin * Math.Pow(fMax / fMin, x);
        }
    }

    /// <summary>
    /// 输出级六段均衡器：每个声道串接 6 个滤波器 + 总增益。
    /// 参数改动实时生效（靠 settings.EqVersion 版本号判断重算系数）。
    /// </summary>
    public sealed class EqualizerProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly SurroundSettings _s;
        private readonly double _rate;
        private readonly int _channels;
        private readonly Biquad[][] _filters;
        private int _version = int.MinValue;
        private bool _on;
        private float _preampGain = 1f;

        public EqualizerProvider(ISampleProvider src, SurroundSettings settings, int rate)
        {
            _src = src;
            _s = settings;
            _rate = rate;
            _channels = Math.Max(1, src.WaveFormat.Channels);
            _filters = new Biquad[_channels][];
            for (int c = 0; c < _channels; c++)
            {
                _filters[c] = new Biquad[EqBands.Count];
                for (int b = 0; b < EqBands.Count; b++) _filters[c][b] = new Biquad();
            }
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _src.Read(buffer, offset, count);
            UpdateIfNeeded();
            if (read <= 0) return read;
            if (!_on)
            {
                if (Math.Abs(_preampGain - 1f) < 0.0001f) return read;
                for (int i = 0; i < read; i++) buffer[offset + i] *= _preampGain;
                return read;
            }
            for (int i = 0; i < read; i++)
            {
                int ch = i % _channels;
                float x = buffer[offset + i] * _preampGain;
                var chain = _filters[ch];
                for (int b = 0; b < chain.Length; b++) x = chain[b].Process(x);
                buffer[offset + i] = x;
            }
            return read;
        }

        private void UpdateIfNeeded()
        {
            if (_version == _s.EqVersion) return;
            _version = _s.EqVersion;
            _on = _s.EqEnabled;
            _preampGain = (float)Math.Pow(10.0, _s.EqPreampDb / 20.0);
            for (int c = 0; c < _channels; c++)
            {
                for (int b = 0; b < EqBands.Count; b++)
                {
                    float g = _s.EqGains != null && b < _s.EqGains.Length ? _s.EqGains[b] : 0f;
                    float f0 = _s.EqFreq != null && b < _s.EqFreq.Length ? _s.EqFreq[b] : EqBands.DefaultFreq[b];
                    var t = _s.EqType != null && b < _s.EqType.Length ? (EqFilterType)_s.EqType[b] : EqBands.DefaultType[b];
                    bool en = _s.EqBandOn == null || b >= _s.EqBandOn.Length || _s.EqBandOn[b];
                    if (!en || (t != EqFilterType.Notch && Math.Abs(g) < 0.01f)) _filters[c][b].SetBypass();
                    else _filters[c][b].Set(t, _rate, f0, g, EqBands.Q);
                }
            }
        }
    }

    /// <summary>均衡器预设（六段，频率与图 5 默认一致）。</summary>
    public static class EqPresets
    {
        public sealed class Preset
        {
            public string Name;
            public float[] Gains;
            public float PreampDb;
        }

        private static Preset P(string name, float preamp, params float[] gains) =>
            new Preset { Name = name, Gains = gains, PreampDb = preamp };

        public static readonly Preset[] All =
        {
            P("平直（不调）", 0f,    0, 0, 0, 0, 0, 0),
            P("低音强化",     -1.5f, 6, 5, 3, 0, 0, 0),
            P("人声清晰",     -1f,   0, 0, -1, 3, 3.5f, 1.5f),
            P("高音通透",     -1f,   0, 0, 0, 0.5f, 3, 4.5f),
            P("流行 / 明亮",  -2f,   3, 2, -1, 0, 3, 3),
            P("夜间小声听",   1.5f,  5, 3.5f, 1, -1, 1, 2),
            P("减少齿音",     -0.5f, 0, 0, -1, -2, -4, -3.5f),
        };
    }
}
