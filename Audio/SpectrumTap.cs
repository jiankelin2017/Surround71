using System;
using NAudio.Wave;

namespace Surround71.Audio
{
    /// <summary>
    /// 频谱采集（只读，不改一个采样）：把输出前的多声道混成单声道，过一组对数分布的带通滤波器，
    /// 每段取"本块峰值"→ 转 dB → 归一化到 0~1，再做快起慢落的包络。界面拿这个数组画频谱动画。
    ///
    /// 纯显示用途，所以不加锁：写的是对齐的 float，界面最多读到差一帧的值，无所谓。
    /// </summary>
    internal sealed class SpectrumTap : ISampleProvider
    {
        /// <summary>频段数（低频在前）。</summary>
        public const int Bands = 16;

        private readonly ISampleProvider _src;
        private readonly int _channels;
        private readonly Biquad[] _hp = new Biquad[Bands];
        private readonly Biquad[] _lp = new Biquad[Bands];
        private readonly float[] _levels = new float[Bands];
        private readonly float[] _peak = new float[Bands];
        private float[] _mono = new float[0];

        public SpectrumTap(ISampleProvider src, float minHz = 35f, float maxHz = 16000f)
        {
            _src = src;
            _channels = Math.Max(1, src.WaveFormat.Channels);
            int rate = src.WaveFormat.SampleRate;

            MinHz = minHz;
            MaxHz = Math.Max(minHz * 2f, Math.Min(maxHz, rate * 0.45f));
            double ratio = Math.Pow(MaxHz / MinHz, 1.0 / Bands);
            Centers = new float[Bands];
            for (int i = 0; i < Bands; i++)
            {
                double f1 = MinHz * Math.Pow(ratio, i);
                double f2 = f1 * ratio;
                Centers[i] = (float)Math.Sqrt(f1 * f2);
                _hp[i] = new Biquad();
                _hp[i].SetHighPass(rate, f1 * 0.97, 0.7071);
                _lp[i] = new Biquad();
                _lp[i].SetLowPass(rate, f2 / 0.97, 0.7071);
            }
        }

        public float MinHz { get; }
        public float MaxHz { get; }

        /// <summary>各频段中心频率，界面按它把柱子摆在对数频率轴上。</summary>
        public float[] Centers { get; }

        /// <summary>各段电平 0~1（低频在前）。界面直接读，别改。</summary>
        public float[] Levels => _levels;

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _src.Read(buffer, offset, count);
            if (read <= 0) return read;

            int frames = read / _channels;
            if (frames <= 0) return read;
            if (_mono.Length < frames) _mono = new float[frames];

            for (int f = 0; f < frames; f++)
            {
                int b = offset + f * _channels;
                float s = 0f;
                for (int c = 0; c < _channels; c++) s += buffer[b + c];
                _mono[f] = s / _channels;
            }

            Array.Clear(_peak, 0, _peak.Length);
            for (int f = 0; f < frames; f++)
            {
                float x = _mono[f];
                for (int i = 0; i < Bands; i++)
                {
                    float y = _lp[i].Process(_hp[i].Process(x));
                    float a = y < 0f ? -y : y;
                    if (a > _peak[i]) _peak[i] = a;
                }
            }

            for (int i = 0; i < Bands; i++)
            {
                // -54 dBFS ~ -6 dBFS 映射到 0~1
                float db = 20f * (float)Math.Log10(_peak[i] + 1e-9f);
                float target = (db + 54f) / 48f;
                if (target < 0f) target = 0f;
                else if (target > 1f) target = 1f;
                float cur = _levels[i];
                _levels[i] = cur + (target - cur) * (target > cur ? 0.55f : 0.16f);   // 快起慢落
            }
            return read;
        }
    }
}
