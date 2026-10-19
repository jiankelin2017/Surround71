using System;

namespace Surround71.Audio
{
    /// <summary>RBJ Audio EQ Cookbook 双二阶滤波器（Direct Form I）。</summary>
    public sealed class Biquad
    {
        private float _b0 = 1f, _b1, _b2, _a1, _a2;
        private float _x1, _x2, _y1, _y2;

        public void Reset() => _x1 = _x2 = _y1 = _y2 = 0f;

        public void SetBypass() { _b0 = 1f; _b1 = _b2 = _a1 = _a2 = 0f; }

        private void Normalize(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            if (a0 == 0 || double.IsNaN(a0)) { SetBypass(); return; }
            double inv = 1.0 / a0;
            _b0 = (float)(b0 * inv); _b1 = (float)(b1 * inv); _b2 = (float)(b2 * inv);
            _a1 = (float)(a1 * inv); _a2 = (float)(a2 * inv);
        }

        public void SetLowShelf(double fs, double f0, double gainDb, double slope = 1.0)
        {
            if (Math.Abs(gainDb) < 0.001 || f0 <= 1 || f0 >= fs * 0.49) { SetBypass(); return; }
            double a = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * f0 / fs;
            double cw = Math.Cos(w0), sw = Math.Sin(w0);
            double alpha = sw / 2.0 * Math.Sqrt((a + 1.0 / a) * (1.0 / slope - 1.0) + 2.0);
            double beta = 2.0 * Math.Sqrt(a) * alpha;
            Normalize(
                a * ((a + 1) - (a - 1) * cw + beta),
                2 * a * ((a - 1) - (a + 1) * cw),
                a * ((a + 1) - (a - 1) * cw - beta),
                (a + 1) + (a - 1) * cw + beta,
                -2 * ((a - 1) + (a + 1) * cw),
                (a + 1) + (a - 1) * cw - beta);
        }

        public void SetLowPass(double fs, double f0, double q = 0.7071)
        {
            if (f0 <= 1 || f0 >= fs * 0.49) { SetBypass(); return; }
            double w0 = 2.0 * Math.PI * f0 / fs;
            double cw = Math.Cos(w0), sw = Math.Sin(w0);
            double alpha = sw / (2.0 * Math.Max(0.05, q));
            Normalize((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
        }

        public void SetHighPass(double fs, double f0, double q = 0.7071)
        {
            if (f0 <= 1 || f0 >= fs * 0.49) { SetBypass(); return; }
            double w0 = 2.0 * Math.PI * f0 / fs;
            double cw = Math.Cos(w0), sw = Math.Sin(w0);
            double alpha = sw / (2.0 * Math.Max(0.05, q));
            Normalize((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
        }

        /// <summary>峰值滤波（均衡器单段）：f0 处提升/衰减 gainDb，带宽由 q 决定。</summary>
        public void SetPeak(double fs, double f0, double gainDb, double q = 1.4)
        {
            if (Math.Abs(gainDb) < 0.01 || f0 <= 1 || f0 >= fs * 0.49 || fs <= 0) { SetBypass(); return; }
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * f0 / fs;
            double cw = Math.Cos(w0), sw = Math.Sin(w0);
            double alpha = sw / (2.0 * Math.Max(0.05, q));
            Normalize(1 + alpha * A, -2 * cw, 1 - alpha * A, 1 + alpha / A, -2 * cw, 1 - alpha / A);
        }

        /// <summary>高架滤波（提升/衰减高频整体）。</summary>
        public void SetHighShelf(double fs, double f0, double gainDb, double slope = 1.0)
        {
            if (Math.Abs(gainDb) < 0.01 || f0 <= 1 || f0 >= fs * 0.49 || fs <= 0) { SetBypass(); return; }
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * f0 / fs;
            double cw = Math.Cos(w0), sw = Math.Sin(w0);
            double alpha = sw / 2.0 * Math.Sqrt((A + 1.0 / A) * (1.0 / slope - 1.0) + 2.0);
            double beta = 2.0 * Math.Sqrt(A) * alpha;
            Normalize(
                A * ((A + 1) + (A - 1) * cw + beta),
                -2 * A * ((A - 1) + (A + 1) * cw),
                A * ((A + 1) + (A - 1) * cw - beta),
                (A + 1) - (A - 1) * cw + beta,
                2 * ((A - 1) - (A + 1) * cw),
                (A + 1) - (A - 1) * cw - beta);
        }

        /// <summary>带阻（削掉 f0 附近一段）。</summary>
        public void SetNotch(double fs, double f0, double q = 1.4)
        {
            if (f0 <= 1 || f0 >= fs * 0.49 || fs <= 0) { SetBypass(); return; }
            double w0 = 2.0 * Math.PI * f0 / fs;
            double cw = Math.Cos(w0), sw = Math.Sin(w0);
            double alpha = sw / (2.0 * Math.Max(0.05, q));
            Normalize(1, -2 * cw, 1, 1 + alpha, -2 * cw, 1 - alpha);
        }

        /// <summary>按类型设置（均衡器统一入口）。</summary>
        public void Set(EqFilterType type, double fs, double f0, double gainDb, double q = 1.4)
        {
            switch (type)
            {
                case EqFilterType.LowShelf: SetLowShelf(fs, f0, gainDb); break;
                case EqFilterType.HighShelf: SetHighShelf(fs, f0, gainDb); break;
                case EqFilterType.Notch: SetNotch(fs, f0, q); break;
                default: SetPeak(fs, f0, gainDb, q); break;
            }
        }

        public float Process(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x;
            _y2 = _y1; _y1 = y;
            return y;
        }
    }
}
