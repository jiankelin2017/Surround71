using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Surround71.Spatial;

namespace Surround71.Audio
{
    /// <summary>
    /// 输出侧管线：
    ///
    ///   源(1/2/6/8ch) → (重采样到设备采样率) → 7.1 上混 → [耳机：HRTF 双耳化 | 音箱：每声道增益/延迟]
    ///   → 映射到设备声道 → WASAPI 输出
    /// </summary>
    public sealed class SurroundPipeline : IDisposable
    {
        private readonly MMDevice _render;
        private WasapiOut _out;
        private ISampleProvider _root;
        private BinauralVirtualizer _virt;
        private SpeakerProcessor _spk;
        private EqualizerProvider _eq;
        private SpectrumTap _spec;

        public SurroundPipeline(MMDevice render)
        {
            _render = render;
        }

        public BinauralVirtualizer Virtualizer => _virt;
        public SpeakerProcessor Speaker => _spk;

        public WaveFormat OutputFormat { get; private set; }
        public string OutputDeviceName => SafeName(_render);
        public int LatencyMs { get; set; } = 55;

        public bool IsRunning => _out != null && _out.PlaybackState == PlaybackState.Playing;

        public float PeakL => _virt?.PeakL ?? 0f;
        public float PeakR => _virt?.PeakR ?? 0f;

        /// <summary>实时频谱（16 段，低频在前，0~1）；没在跑时为 null。界面拿它做均衡器动画。</summary>
        public float[] BandLevels => _spec?.Levels;
        public float[] BandCenters => _spec?.Centers;

        public void Start(ISampleProvider source, SurroundSettings settings)
        {
            Stop();

            var mix = _render.AudioClient.MixFormat;
            int rate = mix.SampleRate;
            int devChannels = Math.Max(2, mix.Channels);

            // 1) 重采样到设备采样率
            ISampleProvider p = source;
            if (p.WaveFormat.SampleRate != rate)
                p = new WdlResamplingSampleProvider(p, rate);

            // 2) 统一到 7.1
            p = To71Provider.Wrap(p, settings);

            // 3) 效果
            if (settings.HeadphoneMode)
            {
                var hrir = HrirDatabase.Get(rate, 128);
                _virt = new BinauralVirtualizer(p, settings, hrir, rate);
                p = _virt;
            }
            else
            {
                _spk = new SpeakerProcessor(p, settings, rate);
                p = _spk;
            }

            // 3.5) 均衡器（输出级，作用于最终声道；参数拖动即时生效）
            _eq = new EqualizerProvider(p, settings, rate);
            p = _eq;

            // 3.6) 频谱采集（纯显示用，不改声音）：给均衡器面板的实时动画供数
            _spec = new SpectrumTap(p);
            p = _spec;

            // 4) 映射到设备声道并输出
            WaveFormat declared = BuildDeclaredFormat(rate, devChannels, mix);
            p = new ChannelMapper(p, declared);
            var wave = new OutputWaveProvider(p, declared);

            _root = p;

            _out = new WasapiOut(_render, AudioClientShareMode.Shared, false, Math.Max(20, LatencyMs));
            _out.Init(wave);
            _out.Play();

            OutputFormat = declared;
        }

        /// <summary>设备能接受的输出格式（必须是 WaveFormatExtensible，否则 8 声道设备会拒绝）。</summary>
        private static WaveFormat BuildDeclaredFormat(int rate, int channels, WaveFormat mixFormat)
        {
            bool mixIsFloat = mixFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                              (mixFormat is WaveFormatExtensible ext &&
                               ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));

            if (mixIsFloat)
                return new WaveFormatExtensible(rate, 32, channels);

            if (mixFormat.BitsPerSample == 24)
                return new WaveFormatExtensible(rate, 24, channels);

            return new WaveFormatExtensible(rate, 16, channels);
        }

        public void Stop()
        {
            try
            {
                if (_out != null)
                {
                    _out.Stop();
                    _out.Dispose();
                }
            }
            catch { }
            finally
            {
                _out = null;
                _root = null;
                _virt = null;
                _spk = null;
                _eq = null;
                _spec = null;
            }
        }

        public void Dispose() => Stop();

        private static string SafeName(MMDevice d)
        {
            try { return d?.FriendlyName ?? ""; } catch { return ""; }
        }
    }
}
