using System;
using NAudio.Wave;
using Surround71.Spatial;

namespace Surround71.Audio
{
    /// <summary>
    /// 7.1 → 双耳（耳机虚拟环绕）。
    ///
    /// 每个虚拟音箱按其方位角从 MIT KEMAR HRIR 库取出左右耳脉冲响应，
    /// 用"重叠保留法"（overlap-save）做分块 FFT 卷积，再叠加到左右耳。
    /// 另加早期反射（外部化）与 M/S 展宽。
    /// </summary>
    public sealed class BinauralVirtualizer : ISampleProvider
    {
        private const int BlockFrames = 128;   // 每块输入帧数
        private const int FftSize = 256;       // FFT 长度（>= 块长 + HRIR 长度 - 1）
        private const int HrirTaps = 128;

        private readonly ISampleProvider _src;
        private readonly SurroundSettings _s;
        private readonly int _fs;
        private readonly int _spk = Layout71.Channels;

        private readonly Fft _fft = new Fft(FftSize);

        // HRIR 频谱（实部/虚部）
        private readonly float[][] _hLre, _hLim, _hRre, _hRim;

        private readonly float[][] _prev;              // 每声道上一块的尾部样本
        private readonly float[] _inBlock;             // 当前块输入（交错 8 声道）
        private readonly float[] _xr = new float[FftSize], _xi = new float[FftSize];
        private readonly float[] _yrL = new float[FftSize], _yiL = new float[FftSize];
        private readonly float[] _yrR = new float[FftSize], _yiR = new float[FftSize];
        private readonly float[] _outL = new float[BlockFrames], _outR = new float[BlockFrames];
        private readonly float[] _stage;               // 交错 8 声道暂存

        private int _outPos, _outAvail;

        // 外部化用的历史 + 反射
        private readonly float[] _histL, _histR;
        private readonly int _histMask;
        private int _histPos;
        private readonly int _refl1, _refl2, _refl3;
        private readonly Biquad _reflLpL = new Biquad(), _reflLpR = new Biquad();

        // 低音管理（<110Hz 等量补到双耳）
        private readonly Biquad _blL = new Biquad(), _blL2 = new Biquad();
        private readonly Biquad _blR = new Biquad(), _blR2 = new Biquad();

        private readonly float[] _speakerGain = new float[Layout71.Channels];

        private float _peakL, _peakR;

        public float PeakL => _peakL;
        public float PeakR => _peakR;

        public BinauralVirtualizer(ISampleProvider src, SurroundSettings settings, HrirDatabase hrir, int sampleRate)
        {
            _src = src;
            _s = settings;
            _fs = sampleRate;

            _inBlock = new float[_spk * BlockFrames];
            _stage = new float[_spk * BlockFrames];

            _prev = new float[_spk][];
            for (int i = 0; i < _spk; i++) _prev[i] = new float[FftSize - BlockFrames];

            _hLre = new float[_spk][]; _hLim = new float[_spk][];
            _hRre = new float[_spk][]; _hRim = new float[_spk][];

            // ---- 取 8 个方向的 HRIR，并做统一归一化（保留方向间的电平差）----
            var pairs = new HrirPair[_spk];
            double sumEnergy = 0.0;
            int counted = 0;

            for (int i = 0; i < _spk; i++)
            {
                if (Layout71.IsLfe[i]) continue;
                var pair = hrir.Get(Layout71.Elevation[i], Layout71.Azimuth[i]);
                pairs[i] = pair;
                for (int k = 0; k < HrirTaps; k++)
                    sumEnergy += (double)pair.Left[k] * pair.Left[k] + (double)pair.Right[k] * pair.Right[k];
                counted++;
            }

            double normEnergy = counted > 0 ? sumEnergy / counted : 1.0;
            float norm = (float)(1.0 / Math.Sqrt(Math.Max(1e-12, normEnergy)));

            for (int i = 0; i < _spk; i++)
            {
                var hL = new float[HrirTaps];
                var hR = new float[HrirTaps];

                if (Layout71.IsLfe[i])
                {
                    // 低音不做 HRTF：等量送左右耳
                    hL[0] = 1f;
                    hR[0] = 1f;
                }
                else
                {
                    for (int k = 0; k < HrirTaps; k++)
                    {
                        hL[k] = pairs[i].Left[k] * norm;
                        hR[k] = pairs[i].Right[k] * norm;
                    }
                }

                _hLre[i] = new float[FftSize]; _hLim[i] = new float[FftSize];
                _hRre[i] = new float[FftSize]; _hRim[i] = new float[FftSize];

                var re = new float[FftSize];
                var im = new float[FftSize];
                Array.Copy(hL, re, HrirTaps);
                _fft.Forward(re, im);
                Array.Copy(re, _hLre[i], FftSize);
                Array.Copy(im, _hLim[i], FftSize);

                Array.Clear(re, 0, FftSize);
                Array.Clear(im, 0, FftSize);
                Array.Copy(hR, re, HrirTaps);
                _fft.Forward(re, im);
                Array.Copy(re, _hRre[i], FftSize);
                Array.Copy(im, _hRim[i], FftSize);
            }

            // ---- 外部化 ----
            int hsize = 1;
            int hneed = (int)(0.030 * _fs) + 8;
            while (hsize < hneed) hsize <<= 1;
            _histL = new float[hsize];
            _histR = new float[hsize];
            _histMask = hsize - 1;

            _refl1 = (int)(0.009 * _fs);
            _refl2 = (int)(0.017 * _fs);
            _refl3 = (int)(0.026 * _fs);

            _reflLpL.SetLowPass(_fs, 6500, 0.7);
            _reflLpR.SetLowPass(_fs, 6500, 0.7);

            _blL.SetLowPass(_fs, 110, 0.707);
            _blL2.SetLowPass(_fs, 110, 0.707);
            _blR.SetLowPass(_fs, 110, 0.707);
            _blR2.SetLowPass(_fs, 110, 0.707);

            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_fs, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            if (frames <= 0) return 0;

            UpdateSpeakerGains();

            int produced = 0;
            while (produced < frames)
            {
                if (_outAvail == 0)
                {
                    FillBlock();
                    ProcessBlock();
                    _outAvail = BlockFrames;
                    _outPos = 0;
                }

                int take = Math.Min(frames - produced, _outAvail);
                for (int i = 0; i < take; i++)
                {
                    buffer[offset + (produced + i) * 2] = _outL[_outPos + i];
                    buffer[offset + (produced + i) * 2 + 1] = _outR[_outPos + i];
                }

                _outPos += take;
                _outAvail -= take;
                produced += take;
            }

            return produced * 2;
        }

        private void UpdateSpeakerGains()
        {
            float center = Db2Lin(_s.CenterLevelDb);
            float surround = Db2Lin(_s.SurroundLevelDb);
            float lfe = Db2Lin(_s.BassBoostDb);

            for (int i = 0; i < _spk; i++)
            {
                float g = 1f;
                if (i == Layout71.FC) g = center;
                else if (i == Layout71.BL || i == Layout71.BR || i == Layout71.SL || i == Layout71.SR) g = surround;
                else if (i == Layout71.LFE) g = lfe;

                _speakerGain[i] = g;
            }
        }

        private void FillBlock()
        {
            int need = _spk * BlockFrames;
            int got = _src.Read(_stage, 0, need);
            int gotFrames = got / _spk;

            if (gotFrames > 0)
                Array.Copy(_stage, 0, _inBlock, 0, gotFrames * _spk);
            if (gotFrames < BlockFrames)
                Array.Clear(_inBlock, gotFrames * _spk, (BlockFrames - gotFrames) * _spk);
        }

        private void ProcessBlock()
        {
            Array.Clear(_yrL, 0, FftSize); Array.Clear(_yiL, 0, FftSize);
            Array.Clear(_yrR, 0, FftSize); Array.Clear(_yiR, 0, FftSize);

            int tail = FftSize - BlockFrames; // 128

            for (int spk = 0; spk < _spk; spk++)
            {
                float g = _speakerGain[spk];
                if (g == 0f)
                {
                    // 仍然要更新历史
                    var pv0 = _prev[spk];
                    for (int i = 0; i < tail; i++) pv0[i] = _inBlock[i * _spk + spk];
                    continue;
                }

                var pv = _prev[spk];
                for (int i = 0; i < tail; i++) _xr[i] = pv[i];
                for (int i = 0; i < BlockFrames; i++) _xr[tail + i] = _inBlock[i * _spk + spk] * g;
                Array.Clear(_xi, 0, FftSize);

                _fft.Forward(_xr, _xi);

                var hLre = _hLre[spk]; var hLim = _hLim[spk];
                var hRre = _hRre[spk]; var hRim = _hRim[spk];

                for (int b = 0; b < FftSize; b++)
                {
                    float xr = _xr[b], xi = _xi[b];

                    _yrL[b] += hLre[b] * xr - hLim[b] * xi;
                    _yiL[b] += hLre[b] * xi + hLim[b] * xr;

                    _yrR[b] += hRre[b] * xr - hRim[b] * xi;
                    _yiR[b] += hRre[b] * xi + hRim[b] * xr;
                }

                for (int i = 0; i < tail; i++) pv[i] = _inBlock[i * _spk + spk];
            }

            _fft.Inverse(_yrL, _yiL);
            _fft.Inverse(_yrR, _yiR);

            for (int i = 0; i < BlockFrames; i++)
            {
                _outL[i] = _yrL[tail + i];
                _outR[i] = _yrR[tail + i];
            }

            ApplyPost(BlockFrames);
        }

        private void ApplyPost(int n)
        {
            float ext = _s.Externalize;
            if (ext < 0f) ext = 0f; else if (ext > 1f) ext = 1f;

            float width = _s.Width;
            if (width < 0f) width = 0f; else if (width > 2f) width = 2f;

            float outGain = Db2Lin(_s.OutputGainDb);
            float bassAmt = _s.BassManagement;

            float maxL = 0f, maxR = 0f;

            for (int i = 0; i < n; i++)
            {
                float dryL = _outL[i];
                float dryR = _outR[i];

                float wl = dryL, wr = dryR;

                if (ext > 0.001f)
                {
                    float rl = _histR[(_histPos - _refl1) & _histMask] * 0.34f
                             + _histL[(_histPos - _refl2) & _histMask] * 0.22f
                             + _histR[(_histPos - _refl3) & _histMask] * 0.13f;
                    float rr = _histL[(_histPos - _refl1) & _histMask] * 0.34f
                             + _histR[(_histPos - _refl2) & _histMask] * 0.22f
                             + _histL[(_histPos - _refl3) & _histMask] * 0.13f;

                    wl += ext * _reflLpL.Process(rl);
                    wr += ext * _reflLpR.Process(rr);
                }

                // 低音管理：把 <110Hz 的能量等量补到双耳，低频更结实、不发散
                if (bassAmt > 0.001f)
                {
                    float lo = (wl + wr) * 0.5f;
                    float f = _blL.Process(lo);
                    f = _blL2.Process(f);
                    wl += bassAmt * f;
                    wr += bassAmt * f;
                }

                // M/S 展宽
                float mid = (wl + wr) * 0.5f;
                float side = (wl - wr) * 0.5f * width;
                wl = mid + side;
                wr = mid - side;

                wl *= outGain;
                wr *= outGain;

                // 软限幅
                wl = SoftClip(wl);
                wr = SoftClip(wr);

                _histL[_histPos & _histMask] = wl;
                _histR[_histPos & _histMask] = wr;
                _histPos++;

                _outL[i] = wl;
                _outR[i] = wr;

                float al = wl < 0 ? -wl : wl;
                float ar = wr < 0 ? -wr : wr;
                if (al > maxL) maxL = al;
                if (ar > maxR) maxR = ar;
            }

            _peakL = _peakL * 0.75f + maxL * 0.25f;
            _peakR = _peakR * 0.75f + maxR * 0.25f;
        }

        private static float SoftClip(float x)
        {
            if (x > 1f) return 1f;
            if (x < -1f) return -1f;
            // 轻微软膝，避免硬削的刺耳
            if (x > 0.85f) { float t = (x - 0.85f) / 0.15f; return 0.85f + 0.15f * (2f * t - t * t); }
            if (x < -0.85f) { float t = (-x - 0.85f) / 0.15f; return -(0.85f + 0.15f * (2f * t - t * t)); }
            return x;
        }

        internal static float Db2Lin(float db) => (float)Math.Pow(10.0, db / 20.0);
    }
}
