using System;

namespace Surround71.Spatial
{
    /// <summary>短序列重采样（Lanczos 窗 sinc），用于把 44.1kHz 的 HRIR 转到输出采样率。</summary>
    public static class Resampler
    {
        private const int A = 6; // Lanczos 半窗宽度

        /// <summary>把 x 按 outRate/inRate 重采样为 outLen 个样本。</summary>
        public static float[] ResampleLanczos(float[] x, double ratio, int outLen)
        {
            var y = new float[outLen];
            if (x == null || x.Length == 0 || outLen <= 0) return y;

            for (int i = 0; i < outLen; i++)
            {
                double p = i / ratio;              // 在输入中的位置
                int i0 = (int)Math.Floor(p);

                double sum = 0.0, wsum = 0.0;
                for (int k = -A + 1; k <= A; k++)
                {
                    int idx = i0 + k;
                    if (idx < 0 || idx >= x.Length) continue;

                    double d = p - idx;
                    if (Math.Abs(d) >= A) continue;

                    double w = Sinc(d) * Sinc(d / A);
                    sum += x[idx] * w;
                    wsum += w;
                }

                y[i] = wsum > 1e-12 ? (float)(sum / wsum) : 0f;
            }

            return y;
        }

        private static double Sinc(double t)
        {
            if (Math.Abs(t) < 1e-9) return 1.0;
            double p = Math.PI * t;
            return Math.Sin(p) / p;
        }

        /// <summary>结尾淡出，避免硬截断产生咔哒声。</summary>
        public static void FadeOut(float[] x, int fadeSamples)
        {
            if (x == null || fadeSamples <= 0) return;
            int n = Math.Min(fadeSamples, x.Length);
            int start = x.Length - n;
            for (int i = 0; i < n; i++)
            {
                double g = 1.0 - (double)i / n;
                x[start + i] *= (float)(g * g);
            }
        }
    }
}
