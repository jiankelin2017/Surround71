using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Surround71.Audio
{
    /// <summary>采集方式。</summary>
    public enum CaptureKind
    {
        /// <summary>虚拟声卡（VB-Cable）：应用 → CABLE Input → 本程序抓 CABLE Output。一条输出设备即可。</summary>
        VirtualCable,

        /// <summary>Windows 环回采集：直接抓某个输出设备正在播放的声音。要求输出设备与环回来源不是同一个。</summary>
        Loopback
    }

    /// <summary>
    /// 全局环绕引擎：把系统正在播放的声音抓回来，经 7.1 处理后送到输出设备。
    ///
    ///   应用 → [虚拟声卡 | 系统环回] → 7.1 处理 → 耳机(双耳) / 7.1 音箱
    /// </summary>
    public sealed class SurroundEngine : IDisposable
    {
        private readonly MMDevice _captureDevice;
        private readonly SurroundPipeline _pipeline;

        private WasapiCapture _capture;
        private FloatRingBuffer _ring;
        private PcmToFloat _converter;
        private float[] _convertBuf = new float[16384];

        private long _capturedFrames;

        public SurroundEngine(MMDevice captureDevice, MMDevice renderDevice)
        {
            _captureDevice = captureDevice;
            _pipeline = new SurroundPipeline(renderDevice);
        }

        public CaptureKind Kind { get; private set; }

        public SurroundPipeline Pipeline => _pipeline;
        public string CaptureDeviceName { get; private set; }
        public string RenderDeviceName => _pipeline.OutputDeviceName;
        public WaveFormat CaptureFormat { get; private set; }
        public WaveFormat OutputFormat => _pipeline.OutputFormat;

        public bool IsRunning => _capture != null && _capture.CaptureState == CaptureState.Capturing;

        public long CapturedFrames => System.Threading.Interlocked.Read(ref _capturedFrames);
        public long DroppedSamples => _ring?.Overflows ?? 0;
        public float PeakL => _pipeline.PeakL;
        public float PeakR => _pipeline.PeakR;

        /// <summary>环回采集时，来源设备与输出设备相同会造成自激，这里提前判断。</summary>
        public static bool IsFeedbackRisk(MMDevice captureRenderDevice, MMDevice outputDevice)
        {
            if (captureRenderDevice == null || outputDevice == null) return false;
            try { return string.Equals(captureRenderDevice.ID, outputDevice.ID, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public void Start(SurroundSettings settings, int latencyMs, CaptureKind kind)
        {
            Stop();
            Kind = kind;

            _capture = kind == CaptureKind.Loopback
                ? CreateLoopback(_captureDevice)
                : CreateCapture(_captureDevice);

            CaptureFormat = _capture.WaveFormat;
            CaptureDeviceName = _captureDevice.FriendlyName;

            _converter = new PcmToFloat(CaptureFormat);
            int capRate = CaptureFormat.SampleRate;
            int capCh = _converter.Channels;

            // 约 1.5 秒容量
            _ring = new FloatRingBuffer(capRate * capCh * 3);
            _convertBuf = new float[Math.Max(16384, capRate * capCh)];
            _capturedFrames = 0;

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _pipeline.LatencyMs = latencyMs;
            _pipeline.Start(new RingBufferSampleProvider(_ring, capRate, capCh), settings);

            _capture.StartRecording();
        }

        private static WasapiCapture CreateCapture(MMDevice device)
        {
            try { return new WasapiCapture(device, true, 20); }
            catch { return new WasapiCapture(device); }
        }

        private static WasapiCapture CreateLoopback(MMDevice device)
        {
            try { return new WasapiLoopbackCapture(device); }
            catch { return new WasapiLoopbackCapture(); }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;

            int frames = _converter.MaxFrames(e.BytesRecorded);
            if (frames <= 0) return;

            int need = frames * _converter.Channels;
            if (_convertBuf.Length < need) _convertBuf = new float[need];

            int got = _converter.Convert(e.Buffer, e.BytesRecorded, _convertBuf);
            if (got <= 0) return;

            _ring.Write(_convertBuf, 0, got * _converter.Channels);
            System.Threading.Interlocked.Add(ref _capturedFrames, got);
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                RecordingError?.Invoke(this, e.Exception);
        }

        public event EventHandler<Exception> RecordingError;

        public void Stop()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    if (_capture.CaptureState == CaptureState.Capturing)
                        _capture.StopRecording();
                    _capture.Dispose();
                }
            }
            catch { }
            finally
            {
                _capture = null;
                _ring?.Clear();
                _ring = null;
            }

            _pipeline.Stop();
        }

        public void Dispose() => Stop();
    }
}
