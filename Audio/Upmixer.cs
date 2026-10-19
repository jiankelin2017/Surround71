using System;
using NAudio.Wave;

namespace Surround71.Audio
{
    /// <summary>
    /// 立体声 → 7.1 上混。
    ///
    /// 思路：保留前左/前右主体，提取中置（人声/对白），
    /// 再用"延迟 + 低通"从左右声道各派生一组侧环绕与后环绕（氛围提取），
    /// 低频单独送到 LFE。这样不会产生相位抵消，听感自然。
    /// </summary>
    public sealed class Upmixer : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly SurroundSettings _s;
        private readonly int _fs;

        // 40ms 延迟线（左右各一条），用于派生侧/后环绕
        private readonly float[] _dL;
        private readonly float[] _dR;
        private readonly int _mask;
        private int _pos;

        private readonly Biquad _sideLpL = new Biquad(), _sideLpR = new Biquad();
        private readonly Biquad _backLpL = new Biquad(), _backLpR = new Biquad();
        private readonly Biquad _lfeLpL = new Biquad(), _lfeLpR = new Biquad();

        private readonly int _sideDelL, _sideDelR, _backDelL, _backDelR;

        private float[] _tmp = new float[16384];

        public Upmixer(ISampleProvider src, SurroundSettings settings, int sampleRate)
        {
            _src = src;
            _s = settings;
            _fs = sampleRate;

            int size = 1;
            int need = (int)(0.045 * _fs) + 8;
            while (size < need) size <<= 1;
            _dL = new float[size];
            _dR = new float[size];
            _mask = size - 1;

            _sideDelL = (int)(0.018 * _fs);
            _sideDelR = (int)(0.023 * _fs);
            _backDelL = (int)(0.031 * _fs);
            _backDelR = (int)(0.037 * _fs);

            _sideLpL.SetLowPass(_fs, 7500, 0.7);
            _sideLpR.SetLowPass(_fs, 7500, 0.7);
            _backLpL.SetLowPass(_fs, 5200, 0.7);
            _backLpR.SetLowPass(_fs, 5200, 0.7);
            _lfeLpL.SetLowPass(_fs, 110, 0.707);
            _lfeLpR.SetLowPass(_fs, 110, 0.707);

            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_fs, Layout71.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / Layout71.Channels;
            if (frames <= 0) return 0;

            int need = frames * 2;
            if (_tmp.Length < need) _tmp = new float[need];

            int read = _src.Read(_tmp, 0, need);
            int got = read / 2;

            float s = _s.UpmixStrength;
            if (s < 0f) s = 0f; else if (s > 1f) s = 1f;

            float norm = 1f / (1f + 0.45f * s);

            for (int f = 0; f < got; f++)
            {
                float l = _tmp[f * 2];
                float r = _tmp[f * 2 + 1];

                int p = _pos;
                _dL[p] = l;
                _dR[p] = r;

                float sideL = _dL[(p - _sideDelL) & _mask];
                float sideR = _dR[(p - _sideDelR) & _mask];
                float backL = _dL[(p - _backDelL) & _mask];
                float backR = _dR[(p - _backDelR) & _mask];
                _pos = (p + 1) & _mask;

                float mid = (l + r) * 0.5f;

                float fc = mid * 0.55f * s;
                float fl = l * (1f - 0.25f * s);
                float fr = r * (1f - 0.25f * s);

                float sl = _sideLpL.Process(sideL) * 0.52f * s;
                float sr = _sideLpR.Process(sideR) * 0.52f * s;
                float bl = _backLpL.Process(backL) * 0.44f * s;
                float br = _backLpR.Process(backR) * 0.44f * s;

                float lo = (_lfeLpL.Process(mid) + _lfeLpR.Process(mid)) * 0.5f;
                float lfe = lo * 0.6f * s;

                int o = offset + f * Layout71.Channels;
                buffer[o + Layout71.FL] = fl * norm;
                buffer[o + Layout71.FR] = fr * norm;
                buffer[o + Layout71.FC] = fc * norm;
                buffer[o + Layout71.LFE] = lfe * norm;
                buffer[o + Layout71.BL] = bl * norm;
                buffer[o + Layout71.BR] = br * norm;
                buffer[o + Layout71.SL] = sl * norm;
                buffer[o + Layout71.SR] = sr * norm;
            }

            return got * Layout71.Channels;
        }
    }

    /// <summary>把 1 / 2 / 4 / 6 / 8 声道的浮点源统一成 7.1（8 声道）。</summary>
    public sealed class To71Provider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly int _sc;
        private float[] _tmp = new float[16384];

        public To71Provider(ISampleProvider src)
        {
            _src = src;
            _sc = Math.Max(1, src.WaveFormat.Channels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, Layout71.Channels);
        }

        public static ISampleProvider Wrap(ISampleProvider src, SurroundSettings settings)
        {
            if (src.WaveFormat.Channels == Layout71.Channels) return src;
            if (src.WaveFormat.Channels == 2 || src.WaveFormat.Channels == 1)
                return new Upmixer(src, settings, src.WaveFormat.SampleRate);
            return new To71Provider(src);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / Layout71.Channels;
            if (frames <= 0) return 0;

            int need = frames * _sc;
            if (_tmp.Length < need) _tmp = new float[need];

            int read = _src.Read(_tmp, 0, need);
            int got = read / _sc;

            for (int f = 0; f < got; f++)
            {
                int s = f * _sc;
                int o = offset + f * Layout71.Channels;
                for (int c = 0; c < Layout71.Channels; c++) buffer[o + c] = 0f;

                switch (_sc)
                {
                    case 1:
                        buffer[o + Layout71.FC] = _tmp[s];
                        break;

                    case 2:
                        buffer[o + Layout71.FL] = _tmp[s];
                        buffer[o + Layout71.FR] = _tmp[s + 1];
                        break;

                    case 4: // FL FR BL BR
                        buffer[o + Layout71.FL] = _tmp[s];
                        buffer[o + Layout71.FR] = _tmp[s + 1];
                        buffer[o + Layout71.BL] = _tmp[s + 2];
                        buffer[o + Layout71.BR] = _tmp[s + 3];
                        break;

                    case 6: // FL FR FC LFE BL BR
                        for (int c = 0; c < 6; c++) buffer[o + c] = _tmp[s + c];
                        break;

                    default: // 8 或更多
                        for (int c = 0; c < Layout71.Channels && c < _sc; c++) buffer[o + c] = _tmp[s + c];
                        break;
                }
            }

            return got * Layout71.Channels;
        }
    }
}
