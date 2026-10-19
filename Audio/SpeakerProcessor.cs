using System;
using NAudio.Wave;

namespace Surround71.Audio
{
    /// <summary>
    /// 真实 7.1 音箱模式：对每一路做独立增益与延迟（模拟距离/摆位修正），
    /// 可选低音管理（把中置/环绕声道的 &lt;110Hz 并到 LFE）。
    /// </summary>
    public sealed class SpeakerProcessor : ISampleProvider
    {
        private const int MaxDelayMs = 40;

        private readonly ISampleProvider _src;
        private readonly SurroundSettings _s;
        private readonly int _fs;
        private readonly int _ch = Layout71.Channels;

        private readonly float[][] _lines;
        private int _pos;
        private readonly int _lineSize;
        private readonly int _mask;

        private readonly Biquad[] _lowExtract = new Biquad[Layout71.Channels];
        private readonly float[] _vals = new float[Layout71.Channels];

        private readonly int[] _delay = new int[Layout71.Channels];
        private readonly float[] _gain = new float[Layout71.Channels];

        private float[] _tmp = new float[16384];

        public SpeakerProcessor(ISampleProvider src, SurroundSettings settings, int sampleRate)
        {
            _src = src;
            _s = settings;
            _fs = sampleRate;

            int size = 1;
            int need = (int)(MaxDelayMs / 1000.0 * _fs) + 8;
            while (size < need) size <<= 1;
            _lineSize = size;
            _mask = size - 1;

            _lines = new float[_ch][];
            for (int c = 0; c < _ch; c++) _lines[c] = new float[_lineSize];

            for (int c = 0; c < _ch; c++)
            {
                _lowExtract[c] = new Biquad();
                _lowExtract[c].SetLowPass(_fs, 110, 0.707);
            }

            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_fs, Layout71.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _ch;
            if (frames <= 0) return 0;

            RefreshParameters();

            int need = frames * _ch;
            if (_tmp.Length < need) _tmp = new float[need];

            int read = _src.Read(_tmp, 0, need);
            int got = read / _ch;

            float outGain = BinauralVirtualizer.Db2Lin(_s.OutputGainDb);
            bool bass = _s.BassManagement > 0.001f;

            for (int f = 0; f < got; f++)
            {
                int s = f * _ch;
                int o = offset + f * _ch;

                float lfeAdd = 0f;

                for (int c = 0; c < _ch; c++)
                {
                    float x = _tmp[s + c];

                    if (bass && c != Layout71.LFE && c != Layout71.FL && c != Layout71.FR)
                    {
                        float lo = _lowExtract[c].Process(x);
                        x -= lo;
                        lfeAdd += lo;
                    }
                    else if (bass && c == Layout71.LFE)
                    {
                        // LFE 保持原样
                    }

                    _vals[c] = x;
                }

                if (bass) _vals[Layout71.LFE] += lfeAdd;

                for (int c = 0; c < _ch; c++)
                {
                    var line = _lines[c];
                    line[_pos & _mask] = _vals[c];
                    buffer[o + c] = line[(_pos - _delay[c]) & _mask] * _gain[c] * outGain;
                }

                _pos = (_pos + 1) & _mask;
            }

            return got * _ch;
        }

        private void RefreshParameters()
        {
            for (int c = 0; c < _ch; c++)
            {
                float db = _s.SpeakerGainDb != null && c < _s.SpeakerGainDb.Length ? _s.SpeakerGainDb[c] : 0f;
                float ms = _s.SpeakerDelayMs != null && c < _s.SpeakerDelayMs.Length ? _s.SpeakerDelayMs[c] : 0f;

                if (ms < 0f) ms = 0f; else if (ms > MaxDelayMs) ms = MaxDelayMs;

                _gain[c] = BinauralVirtualizer.Db2Lin(db);
                _delay[c] = (int)(ms / 1000.0 * _fs);
                if (_delay[c] > _lineSize - 1) _delay[c] = _lineSize - 1;
            }
        }
    }
}
