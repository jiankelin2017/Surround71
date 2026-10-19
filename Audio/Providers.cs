using System;
using NAudio.Wave;

namespace Surround71.Audio
{
    /// <summary>把环形缓冲区包装成 ISampleProvider（N 声道浮点）。欠载时输出静音，保持时间轴连续。</summary>
    public sealed class RingBufferSampleProvider : ISampleProvider
    {
        private readonly FloatRingBuffer _ring;

        public RingBufferSampleProvider(FloatRingBuffer ring, int sampleRate, int channels)
        {
            _ring = ring;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, Math.Max(1, channels));
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            _ring.Read(buffer, offset, count);
            return count;
        }
    }

    /// <summary>常量静音源。</summary>
    public sealed class SilentProvider : ISampleProvider
    {
        private readonly float[] _zero = new float[16384];

        public SilentProvider(int rate, int channels)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, Math.Max(1, channels));
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _zero.Length);
            Array.Copy(_zero, 0, buffer, offset, n);
            return n;
        }
    }

    /// <summary>
    /// 把任意声道数的浮点源映射到设备声道数：
    ///  · 声道数相同 → 直通
    ///  · 2 → N（N&gt;2）→ 前左/前右，其余置 0
    ///  · 8 → 2 → 标准 7.1 折叠
    ///  · 其它 → 取前 min(源,目标) 个声道，其余置 0
    /// </summary>
    public sealed class ChannelMapper : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly int _sc;
        private readonly int _dc;
        private float[] _tmp = new float[16384];

        public ChannelMapper(ISampleProvider src, WaveFormat declaredFormat)
        {
            _src = src;
            _sc = Math.Max(1, src.WaveFormat.Channels);
            _dc = Math.Max(1, declaredFormat.Channels);
            WaveFormat = declaredFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _dc;
            if (frames <= 0) return 0;

            int need = frames * _sc;
            if (_tmp.Length < need) _tmp = new float[need];

            int read = _src.Read(_tmp, 0, need);
            int got = read / _sc;

            for (int f = 0; f < got; f++)
            {
                int s = f * _sc;
                int o = offset + f * _dc;

                if (_sc == _dc)
                {
                    Array.Copy(_tmp, s, buffer, o, _dc);
                }
                else if (_sc == 2 && _dc > 2)
                {
                    buffer[o] = _tmp[s];
                    buffer[o + 1] = _tmp[s + 1];
                    for (int c = 2; c < _dc; c++) buffer[o + c] = 0f;
                }
                else if (_sc >= 8 && _dc == 2)
                {
                    float fc = _tmp[s + 2], lfe = _tmp[s + 3];
                    float bl = _tmp[s + 4], br = _tmp[s + 5];
                    float sl = _tmp[s + 6], sr = _tmp[s + 7];
                    buffer[o] = (_tmp[s] + 0.707f * fc + 0.6f * bl + 0.6f * sl + 0.4f * lfe) * 0.75f;
                    buffer[o + 1] = (_tmp[s + 1] + 0.707f * fc + 0.6f * br + 0.6f * sr + 0.4f * lfe) * 0.75f;
                }
                else
                {
                    for (int c = 0; c < _dc; c++)
                        buffer[o + c] = c < _sc ? _tmp[s + c] : 0f;
                }
            }

            return got * _dc;
        }
    }

    /// <summary>
    /// 输出端 IWaveProvider。声明与设备匹配的 WaveFormatExtensible，
    /// 并把浮点样本写成设备要求的 32bit float 或 16bit PCM 字节流。
    /// </summary>
    public sealed class OutputWaveProvider : IWaveProvider
    {
        private readonly ISampleProvider _source;
        private readonly bool _isFloat;
        private float[] _f = new float[16384];
        private short[] _s;

        public OutputWaveProvider(ISampleProvider source, WaveFormat declaredFormat)
        {
            _source = source;
            WaveFormat = declaredFormat;
            _isFloat = declaredFormat.BitsPerSample >= 32;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_isFloat)
            {
                int n = count / 4;
                if (n <= 0) return 0;
                if (_f.Length < n) _f = new float[n];

                int read = _source.Read(_f, 0, n);
                if (read <= 0) return 0;

                Buffer.BlockCopy(_f, 0, buffer, offset, read * 4);
                return read * 4;
            }
            else
            {
                int n = count / 2;
                if (n <= 0) return 0;
                if (_f.Length < n) _f = new float[n];
                if (_s == null || _s.Length < n) _s = new short[n];

                int read = _source.Read(_f, 0, n);
                if (read <= 0) return 0;

                for (int i = 0; i < read; i++)
                {
                    float v = _f[i] * 32767f;
                    if (v > 32767f) v = 32767f;
                    else if (v < -32768f) v = -32768f;
                    _s[i] = (short)v;
                }

                Buffer.BlockCopy(_s, 0, buffer, offset, read * 2);
                return read * 2;
            }
        }
    }
}
