using System;
using System.IO;
using NAudio.Wave;

namespace Surround71.Audio
{
    /// <summary>内置"演示曲"：合成一小段循环音乐，用来试听效果。</summary>
    public sealed class TestTuneProvider : ISampleProvider
    {
        private readonly int _fs;
        private readonly Random _rnd = new Random(20240501);
        private long _n;

        private static readonly double[] Roots = { 110.0000, 87.3071, 130.8128, 97.9989 }; // Am - F - C - G

        public TestTuneProvider(int sampleRate)
        {
            _fs = Math.Max(8000, sampleRate);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_fs, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            if (frames <= 0) return 0;

            const double bpm = 96.0;
            double beatLen = 60.0 / bpm;
            double barLen = beatLen * 4.0;
            double hatLen = beatLen / 2.0;
            double twoPi = Math.PI * 2.0;

            for (int f = 0; f < frames; f++)
            {
                long n = _n + f;
                double t = n / (double)_fs;

                int bar = (int)(t / barLen) % 4;
                double root = Roots[bar];

                double tBeat = t % beatLen;
                int beatIdx = (int)(t / beatLen) % 4;

                double padL = (Math.Sin(twoPi * root * 2 * 1.0000 * t) +
                               Math.Sin(twoPi * root * 2 * 1.1892 * t) +
                               Math.Sin(twoPi * root * 2 * 1.4983 * t)) * 0.05;
                double padR = (Math.Sin(twoPi * root * 2 * 1.0015 * t) +
                               Math.Sin(twoPi * root * 2 * 1.1905 * t) +
                               Math.Sin(twoPi * root * 2 * 1.5000 * t)) * 0.05;

                double bass = Math.Sin(twoPi * root * tBeat) * Math.Exp(-tBeat * 3.0) * 0.22;

                double kick = 0.0;
                if (beatIdx == 0 || beatIdx == 2)
                    kick = Math.Sin(twoPi * 52.0 * tBeat) * Math.Exp(-tBeat * 15.0) * 0.50;

                double tHat = t % hatLen;
                double hat = (_rnd.NextDouble() * 2.0 - 1.0) * Math.Exp(-tHat * 70.0) * 0.05;

                float l = (float)(padL + bass + kick + hat);
                float r = (float)(padR + bass + kick + hat);

                if (l > 0.95f) l = 0.95f; else if (l < -0.95f) l = -0.95f;
                if (r > 0.95f) r = 0.95f; else if (r < -0.95f) r = -0.95f;

                buffer[offset + (f << 1)] = l;
                buffer[offset + (f << 1) + 1] = r;
            }

            _n += frames;
            return frames * 2;
        }
    }

    /// <summary>
    /// 7.1 声道测试信号：依次在 FL/FR/FC/LFE/BL/BR/SL/SR 上播放一段带通噪声脉冲，
    /// 用来验证方位是否正确。可用 SoloChannel 固定只放某一个声道。
    /// </summary>
    public sealed class ChannelTestProvider : ISampleProvider
    {
        private const double StepSeconds = 0.55;
        private const double GapSeconds = 0.12;
        private const double TailSeconds = 0.6;

        private readonly int _fs;
        private readonly int _stepFrames;
        private readonly int _gapFrames;
        private readonly int _cycleFrames;

        private long _n;
        private uint _rng = 0x1234567u;

        private readonly Biquad _hpL = new Biquad(), _lpL = new Biquad();

        /// <summary>-1 = 自动遍历全部 8 个声道；0..7 = 只循环该声道。</summary>
        public volatile int SoloChannel = -1;

        /// <summary>当前正在发声的声道（-1 = 静音/间隙）。</summary>
        public volatile int CurrentChannel = -1;

        public ChannelTestProvider(int sampleRate)
        {
            _fs = Math.Max(8000, sampleRate);
            _stepFrames = (int)(StepSeconds * _fs);
            _gapFrames = (int)(GapSeconds * _fs);
            _cycleFrames = Layout71.Channels * (_stepFrames + _gapFrames) + (int)(TailSeconds * _fs);

            _hpL.SetHighPass(_fs, 600, 0.8);
            _lpL.SetLowPass(_fs, 2200, 0.8);

            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_fs, Layout71.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / Layout71.Channels;
            if (frames <= 0) return 0;

            for (int f = 0; f < frames; f++)
            {
                int baseOff = offset + f * Layout71.Channels;
                for (int c = 0; c < Layout71.Channels; c++) buffer[baseOff + c] = 0f;

                long n = _n + f;
                int channel;
                double env = 0.0;

                int solo = SoloChannel;
                if (solo >= 0 && solo < Layout71.Channels)
                {
                    channel = solo;
                    env = 0.85;
                }
                else
                {
                    long pos = n % _cycleFrames;
                    int slot = (int)(pos / (_stepFrames + _gapFrames));
                    if (slot >= Layout71.Channels)
                    {
                        channel = -1;
                    }
                    else
                    {
                        channel = slot;
                        int within = (int)(pos % (_stepFrames + _gapFrames));
                        if (within < _stepFrames)
                        {
                            // 淡入淡出，避免咔哒声
                            double u = within / (double)_stepFrames;
                            env = u < 0.06 ? u / 0.06 : (u > 0.94 ? (1.0 - u) / 0.06 : 1.0);
                            env *= 0.85;
                        }
                    }
                }

                if (channel < 0)
                {
                    _hpL.Process(0f); _lpL.Process(0f);
                    continue;
                }

                float noise = NextNoise();

                if (Layout71.IsLfe[channel])
                {
                    // 低音声道用 45Hz 正弦，更容易听出
                    double t = n / (double)_fs;
                    float thump = (float)(Math.Sin(2 * Math.PI * 45.0 * t) * 0.6);
                    buffer[baseOff + channel] = (float)(thump * env);
                    continue;
                }

                float band = _hpL.Process(noise);
                band = _lpL.Process(band);
                float v = band * 1.6f;

                buffer[baseOff + channel] = (float)(v * env);
            }

            _n += frames;

            // 记录当前声道，供界面高亮
            if (SoloChannel >= 0) CurrentChannel = SoloChannel;
            else
            {
                long pos = (_n % _cycleFrames);
                int slot = (int)(pos / (_stepFrames + _gapFrames));
                CurrentChannel = slot < Layout71.Channels ? slot : -1;
            }

            return frames * Layout71.Channels;
        }

        private float NextNoise()
        {
            // xorshift32 → [-1,1)
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng / 2147483648f) - 1f;
        }
    }

    /// <summary>播放本地音频文件（wav/mp3/aiff 原生，其它走 Media Foundation），可选循环。</summary>
    public sealed class FileLoopSource : ISampleProvider, IDisposable
    {
        private readonly string _path;
        private readonly bool _loop;
        private WaveStream _stream;
        private ISampleProvider _source;

        public FileLoopSource(string path, bool loop)
        {
            _path = path;
            _loop = loop;
            Open();
        }

        public WaveFormat WaveFormat => _source.WaveFormat;
        public string FileName => Path.GetFileName(_path);
        public double TotalSeconds => _stream.TotalTime.TotalSeconds;
        public double CurrentSeconds => _stream.CurrentTime.TotalSeconds;

        private void Open()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
            _source = null;

            try { _stream = new AudioFileReader(_path); }
            catch { _stream = new MediaFoundationReader(_path); }

            _source = _stream.ToSampleProvider();
        }

        public void Seek(double seconds)
        {
            try { _stream.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, seconds)); }
            catch { }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int total = 0;
            int guard = 0;

            while (total < count)
            {
                int r = _source.Read(buffer, offset + total, count - total);
                if (r > 0) { total += r; continue; }
                if (!_loop || ++guard > 8) break;
                try { _stream.Position = 0; }
                catch { Open(); }
            }

            return total;
        }

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
            _source = null;
        }
    }
}
