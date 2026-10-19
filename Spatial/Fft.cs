using System;

namespace Surround71.Spatial
{
    /// <summary>基-2 就地复数 FFT（含位反转表与旋转因子表）。</summary>
    public sealed class Fft
    {
        public int Size { get; }

        private readonly int[] _rev;
        private readonly float[] _cos;
        private readonly float[] _sin;

        public Fft(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0)
                throw new ArgumentException("FFT 长度必须是 2 的幂", nameof(size));

            Size = size;
            _rev = new int[size];
            int bits = 0;
            while ((1 << bits) < size) bits++;

            for (int i = 0; i < size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                    if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                _rev[i] = r;
            }

            _cos = new float[size / 2];
            _sin = new float[size / 2];
            for (int i = 0; i < size / 2; i++)
            {
                double a = -2.0 * Math.PI * i / size;
                _cos[i] = (float)Math.Cos(a);
                _sin[i] = (float)Math.Sin(a);
            }
        }

        public void Forward(float[] re, float[] im) => Transform(re, im, false);

        /// <summary>逆变换，结果已除以 N。</summary>
        public void Inverse(float[] re, float[] im) => Transform(re, im, true);

        private void Transform(float[] re, float[] im, bool inverse)
        {
            int n = Size;

            for (int i = 0; i < n; i++)
            {
                int j = _rev[i];
                if (j > i)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1;
                int step = n / len;
                for (int i = 0; i < n; i += len)
                {
                    int tw = 0;
                    for (int k = 0; k < half; k++, tw += step)
                    {
                        float wr = _cos[tw];
                        float wi = inverse ? -_sin[tw] : _sin[tw];

                        int a = i + k;
                        int b = a + half;

                        float xr = re[b] * wr - im[b] * wi;
                        float xi = re[b] * wi + im[b] * wr;

                        re[b] = re[a] - xr;
                        im[b] = im[a] - xi;
                        re[a] += xr;
                        im[a] += xi;
                    }
                }
            }

            if (inverse)
            {
                float s = 1f / n;
                for (int i = 0; i < n; i++)
                {
                    re[i] *= s;
                    im[i] *= s;
                }
            }
        }
    }
}
