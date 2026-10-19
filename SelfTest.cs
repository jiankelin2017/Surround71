using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Surround71.Audio;
using Surround71.Spatial;

namespace Surround71
{
    /// <summary>无界面自检：验证 HRIR 库、双耳化、上混、音箱模式、输出管线。</summary>
    internal static class SelfTest
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        private sealed class MultiWriter : TextWriter
        {
            private readonly TextWriter[] _w;
            public MultiWriter(params TextWriter[] w) { _w = w; }
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
            public override void Write(char c) { foreach (var w in _w) w.Write(c); }
            public override void Write(string s) { foreach (var w in _w) w.Write(s); }
            public override void WriteLine(string s) { foreach (var w in _w) w.WriteLine(s); }
            public override void Flush() { foreach (var w in _w) w.Flush(); }
        }

        private static void Attach(string logPath)
        {
            var writers = new System.Collections.Generic.List<TextWriter>
            {
                new StreamWriter(logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true }
            };
            try
            {
                if (AttachConsole(-1) || AllocConsole())
                    writers.Add(new StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8) { AutoFlush = true });
            }
            catch { }
            Console.SetOut(new MultiWriter(writers.ToArray()));
        }

        public static int Run(string logPath)
        {
            if (string.IsNullOrEmpty(logPath))
                logPath = Path.Combine(Path.GetTempPath(), "surround71_selftest.txt");

            Attach(logPath);
            Console.WriteLine("=== PC 7.1 环绕声 自检 ===");
            Console.WriteLine("日志: " + logPath);

            const int fs = 48000;

            // ---- 1) HRIR 数据库 ----
            var db = HrirDatabase.Load(fs, 128);
            Console.WriteLine($"1) HRIR 库：方向数={db.PointCount}, 抽头={db.Taps} @ {fs}Hz");
            Console.WriteLine($"   仰角范围 {db.MinElevation}° ~ {db.MaxElevation}°");
            if (db.PointCount == 0) { Console.WriteLine("   ★ HRIR 未加载！"); return 1; }

            Console.WriteLine("   方向电平检查（左耳 / 右耳 能量）：");
            var dirs = new (float el, float az, string name, bool leftShouldWin)[]
            {
                (0f, -110f, "后左 BL", true),
                (0f,  -90f, "侧左 SL", true),
                (0f,  -30f, "前左 FL", true),
                (0f,    0f, "中置 FC", false),
                (0f,   30f, "前右 FR", false),
                (0f,   90f, "侧右 SR", false),
                (0f,  110f, "后右 BR", false),
            };
            foreach (var d in dirs)
            {
                var p = db.Get(d.el, d.az);
                double el = Energy(p.Left), er = Energy(p.Right);
                double ratioDb = 10 * Math.Log10((el + 1e-15) / (er + 1e-15));
                Console.WriteLine($"      {d.name,-8} az={d.az,6:0}  左={el:0.00000}  右={er:0.00000}  L/R={ratioDb:+0.0;-0.0} dB");
            }

            // ---- 2) 双耳化：脉冲方向测试 ----
            Console.WriteLine("2) 双耳化方向测试（在 FL / FR 上各打一个脉冲）：");
            foreach (int ch in new[] { Layout71.FL, Layout71.FR, Layout71.FC, Layout71.SL, Layout71.SR })
            {
                var s = new SurroundSettings();
                var imp = new ImpulseSource(Layout71.Channels, 4096, ch, 1.0f);
                var virt = new BinauralVirtualizer(imp, s, db, fs);
                var buf = new float[4096 * 2];
                virt.Read(buf, 0, buf.Length);

                int pl = 0, pr = 0;
                float vl = 0, vr = 0;
                for (int i = 0; i < 4096; i++)
                {
                    float a = Math.Abs(buf[i * 2]);
                    float b = Math.Abs(buf[i * 2 + 1]);
                    if (a > vl) { vl = a; pl = i; }
                    if (b > vr) { vr = b; pr = i; }
                }
                Console.WriteLine($"      {Layout71.ShortNames[ch],-4} 左耳峰值 {vl:0.000}@{pl,3}  右耳峰值 {vr:0.000}@{pr,3}  " +
                                  $"→ {(vl > vr ? "偏左" : "偏右")}");
            }

            // ---- 3) 整链电平（演示曲）----
            Console.WriteLine("3) 整链电平（演示曲 → 上混 → 双耳化）：");
            var st = SurroundPresets.All().First(p => p.Name == "电影环绕");
            var tune = new TestTuneProvider(fs);
            var up = new Upmixer(tune, st, fs);
            var v2 = new BinauralVirtualizer(up, st, db, fs);
            var outBuf = new float[fs * 2 * 2];
            int got = v2.Read(outBuf, 0, outBuf.Length);
            Console.WriteLine($"      帧数={got / 2}  RMS={Rms(outBuf):0.0000}  峰值={Peak(outBuf):0.000}  " +
                              $"削波={(Peak(outBuf) > 1.0001 ? "有!" : "无")}");

            // ---- 4) 各预设 ----
            Console.WriteLine("4) 各预设（耳机模式）峰值：");
            foreach (var p in SurroundPresets.All())
            {
                var t = new TestTuneProvider(fs);
                ISampleProvider sp = new Upmixer(t, p, fs);
                var vv = new BinauralVirtualizer(sp, p, db, fs);
                var b = new float[fs * 2 * 2];
                vv.Read(b, 0, b.Length);
                Console.WriteLine($"      · {p.Name,-14} 峰值 {Peak(b):0.000}  RMS {Rms(b):0.0000}  {(Peak(b) > 1.0001 ? "★削波" : "OK")}  ({p.Description})");
            }

            // ---- 5) 7.1 声道测试信号 ----
            Console.WriteLine("5) 7.1 声道测试信号（完整一轮遍历）：");
            {
                var ct = new ChannelTestProvider(fs);
                int total = fs * 6;                       // 6 秒，覆盖一整轮
                var cb = new float[Layout71.Channels * total];
                ct.Read(cb, 0, cb.Length);
                var perCh = new double[Layout71.Channels];
                for (int f = 0; f < total; f++)
                    for (int c = 0; c < Layout71.Channels; c++)
                        perCh[c] += cb[f * Layout71.Channels + c] * (double)cb[f * Layout71.Channels + c];
                for (int c = 0; c < Layout71.Channels; c++)
                    Console.WriteLine($"      {Layout71.ShortNames[c],-4}({Layout71.Names[c]}) 能量 {perCh[c]:0.00000}  {(perCh[c] > 0.5 ? "OK" : "★无输出")}");
            }

            // ---- 6) 音箱模式：0dB/0ms 应为恒等 ----
            {
                var s = new SurroundSettings { HeadphoneMode = false, BassManagement = 0f };
                var src = new RampSource(Layout71.Channels, 1024);
                var spk = new SpeakerProcessor(src, s, fs);
                var b = new float[1024 * Layout71.Channels];
                spk.Read(b, 0, b.Length);
                double maxErr = 0;
                for (int i = 0; i < b.Length; i++) maxErr = Math.Max(maxErr, Math.Abs(b[i] - src.Data[i]));
                Console.WriteLine($"6) 音箱模式恒等（增益0dB/延迟0ms）：最大误差 {maxErr:0.000000}  {(maxErr < 1e-6 ? "OK" : "★异常")}");
            }

            // ---- 7) 实时性 ----
            {
                var sw = Stopwatch.StartNew();
                var t = new TestTuneProvider(fs);
                ISampleProvider sp = new Upmixer(t, st, fs);
                var vv = new BinauralVirtualizer(sp, st, db, fs);
                var b = new float[fs * 2];
                int secs = 10;
                long total = 0;
                for (int i = 0; i < secs; i++) total += vv.Read(b, 0, b.Length) / 2;
                sw.Stop();
                double audioSeconds = total / (double)fs;
                Console.WriteLine($"7) 性能：处理 {audioSeconds:0.0}s 音频耗时 {sw.Elapsed.TotalSeconds:0.000}s " +
                                  $"（实时倍率 {audioSeconds / Math.Max(1e-6, sw.Elapsed.TotalSeconds):0.0}×，占用约 {100.0 * sw.Elapsed.TotalSeconds / audioSeconds:0.0}% 单核）");
            }

            // ---- 7b) 均衡器频响实测 ----
            Console.WriteLine("7b) 均衡器频响实测（同频率正弦波 进 / 出 对比）：");
            {
                var es = new SurroundSettings { EqEnabled = true };
                es.EqVersion++;
                Console.WriteLine($"      全 0dB 透明： 70Hz {MeasureEqDb(es, 70, fs):+0.0;-0.0;0.0}dB   1.2kHz {MeasureEqDb(es, 1200, fs):+0.0;-0.0;0.0}dB   （应 ≈ 0）");

                es.EqGains[1] = 12f; es.EqVersion++;                      // 115Hz 峰值 +12dB
                Console.WriteLine($"      115Hz +12dB： 115Hz {MeasureEqDb(es, 115, fs):+0.0;-0.0;0.0}dB   1.2kHz {MeasureEqDb(es, 1200, fs):+0.0;-0.0;0.0}dB   （应 ≈ +12 / 接近 0）");

                es.EqGains[1] = 0f; es.EqGains[4] = -9f; es.EqVersion++;  // 6kHz 峰值 -9dB
                Console.WriteLine($"      6kHz -9dB：   6kHz {MeasureEqDb(es, 6000, fs):+0.0;-0.0;0.0}dB   250Hz {MeasureEqDb(es, 250, fs):+0.0;-0.0;0.0}dB   （应 ≈ -9 / 接近 0）");

                es.EqGains[4] = 0f; es.EqPreampDb = -6f; es.EqVersion++;  // 总增益 -6dB
                Console.WriteLine($"      总增益 -6dB： 1.2kHz {MeasureEqDb(es, 1200, fs):+0.0;-0.0;0.0}dB   （应 = -6.0）");

                es.EqPreampDb = 0f; es.EqEnabled = false; es.EqVersion++; // 关闭开关 → 完全旁通
                Console.WriteLine($"      关闭开关：    1.2kHz {MeasureEqDb(es, 1200, fs):+0.0;-0.0;0.0}dB   （应 = 0）");
            }

            // ---- 8) 实机输出设备 ----
            Console.WriteLine("8) 实机输出测试：");
            try
            {
                using var en = new MMDeviceEnumerator();
                var devs = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                foreach (var d in devs)
                {
                    int ch = 0;
                    try { ch = d.AudioClient.MixFormat.Channels; } catch { }
                    try
                    {
                        using var pipe = new SurroundPipeline(d) { LatencyMs = 55 };
                        var s = SurroundPresets.All().First(p => p.Name == "电影环绕").Clone();
                        s.HeadphoneMode = true;
                        pipe.Start(new TestTuneProvider(d.AudioClient.MixFormat.SampleRate), s);
                        System.Threading.Thread.Sleep(500);
                        Console.WriteLine($"      · {d.FriendlyName} [混音 {ch}ch] → 声明 {pipe.OutputFormat}，运行中={pipe.IsRunning}");
                        pipe.Stop();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      · {d.FriendlyName} [混音 {ch}ch] → 失败：{ex.Message}");
                    }
                }

                // 8b) 音箱模式打到 8 声道设备
                var eight = devs.FirstOrDefault(d => { try { return d.AudioClient.MixFormat.Channels >= 8; } catch { return false; } });
                if (eight != null)
                {
                    try
                    {
                        using var pipe = new SurroundPipeline(eight) { LatencyMs = 55 };
                        var s = new SurroundSettings { HeadphoneMode = false, UpmixStrength = 0.9f };
                        pipe.Start(new TestTuneProvider(eight.AudioClient.MixFormat.SampleRate), s);
                        System.Threading.Thread.Sleep(500);
                        Console.WriteLine($"      8b) 音箱模式 → {eight.FriendlyName}，声明 {pipe.OutputFormat}，运行中={pipe.IsRunning}");
                        pipe.Stop();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("      8b) 音箱模式失败：" + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("      8b) 未找到 8 声道输出设备，跳过。");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("      设备测试失败：" + ex.Message);
            }

            // ---- 9) 全局引擎（两种采集方式）----
            Console.WriteLine("9) 全局引擎测试：");
            try
            {
                using var en = new MMDeviceEnumerator();
                var renderList = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                   .Where(UsableDev).ToList();
                var captureList = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                    .Where(UsableDev).ToList();

                // 9a) 虚拟声卡方式
                var cableOut = captureList.FirstOrDefault(d => SName(d).Contains("CABLE", StringComparison.OrdinalIgnoreCase));
                var realOut = renderList.FirstOrDefault(d => !SName(d).Contains("CABLE", StringComparison.OrdinalIgnoreCase));

                if (cableOut == null || realOut == null)
                {
                    Console.WriteLine($"      9a) 虚拟声卡方式：跳过（CABLE 采集端={(cableOut == null ? "缺失" : "有")}，真实输出={(realOut == null ? "缺失" : "有")}）");
                }
                else
                {
                    Console.WriteLine($"      9a) 虚拟声卡：采集 {SName(cableOut)} → 输出 {SName(realOut)}");
                    using var eng = new SurroundEngine(cableOut, realOut);
                    eng.Start(SurroundPresets.All().First(p => p.Name == "电影环绕"), 60, CaptureKind.VirtualCable);
                    System.Threading.Thread.Sleep(1500);
                    Console.WriteLine($"          运行中={eng.IsRunning}, 已采集帧={eng.CapturedFrames}, 丢帧={eng.DroppedSamples}, " +
                                      $"峰值 L={eng.PeakL:0.000} R={eng.PeakR:0.000}");
                    eng.Stop();
                }

                // 9b) 系统环回方式：抓一个设备，输出到另一个设备
                if (renderList.Count < 2)
                {
                    Console.WriteLine($"      9b) 系统环回方式：跳过（本机只有 {renderList.Count} 个输出设备，需要 2 个）");
                }
                else
                {
                    var src = renderList[0];
                    var dst = renderList[1];
                    Console.WriteLine($"      9b) 系统环回：抓 {SName(src)} → 输出 {SName(dst)}");
                    Console.WriteLine($"          自激风险={(SurroundEngine.IsFeedbackRisk(src, dst) ? "有" : "无")}");
                    using var eng2 = new SurroundEngine(src, dst);
                    eng2.Start(SurroundPresets.All().First(p => p.Name == "电影环绕"), 60, CaptureKind.Loopback);
                    System.Threading.Thread.Sleep(1500);
                    Console.WriteLine($"          运行中={eng2.IsRunning}, 已采集帧={eng2.CapturedFrames}, 丢帧={eng2.DroppedSamples}, " +
                                      $"采集格式={eng2.CaptureFormat}");
                    eng2.Stop();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("      全局引擎失败：" + ex.Message);
            }

            Console.WriteLine("=== 自检完成 ===");
            return 0;
        }

        private static string SName(MMDevice d)
        {
            try { var n = d.FriendlyName; if (!string.IsNullOrWhiteSpace(n)) return n; } catch { }
            try { var n = d.DeviceFriendlyName; if (!string.IsNullOrWhiteSpace(n)) return n; } catch { }
            return "(设备名不可用)";
        }

        private static bool UsableDev(MMDevice d) => SName(d) != "(设备名不可用)";

        private static double Energy(float[] a)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++) s += (double)a[i] * a[i];
            return s;
        }

        /// <summary>把指定频率的正弦波送进均衡器，量出稳态增益（dB）。</summary>
        private static double MeasureEqDb(SurroundSettings s, double freq, int rate)
        {
            const float amp = 0.25f;
            var src = new SineSource(2, rate, freq, amp, rate);
            var eq = new EqualizerProvider(src, s, rate);
            var buf = new float[rate * 2];
            eq.Read(buf, 0, buf.Length);
            int skip = (int)(rate * 0.1) * 2;                 // 跳过前 100ms，等滤波器进入稳态
            double sum = 0; int n = 0;
            for (int i = skip; i < buf.Length; i++) { sum += (double)buf[i] * buf[i]; n++; }
            double rms = Math.Sqrt(sum / Math.Max(1, n));
            return 20 * Math.Log10((rms + 1e-12) / (amp / Math.Sqrt(2)));
        }

        private static double Rms(float[] b)
        {
            double s = 0;
            for (int i = 0; i < b.Length; i++) s += (double)b[i] * b[i];
            return Math.Sqrt(s / Math.Max(1, b.Length));
        }

        private static double Peak(float[] b)
        {
            double p = 0;
            for (int i = 0; i < b.Length; i++)
            {
                double a = Math.Abs(b[i]);
                if (a > p) p = a;
            }
            return p;
        }

        /// <summary>在指定声道、指定帧上放一个单位脉冲，其余为静音。</summary>
        private sealed class ImpulseSource : ISampleProvider
        {
            private readonly int _ch;
            private readonly int _frames;
            private readonly int _impulseChannel;
            private readonly float _amplitude;
            private bool _done;

            public ImpulseSource(int channels, int frames, int impulseChannel, float amplitude)
            {
                _ch = channels;
                _frames = frames;
                _impulseChannel = impulseChannel;
                _amplitude = amplitude;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / _ch;
                Array.Clear(buffer, offset, frames * _ch);
                if (_done) return frames * _ch;
                if (frames > 0) buffer[offset + _impulseChannel] = _amplitude;
                _done = true;
                return frames * _ch;
            }
        }

        /// <summary>指定频率的正弦波（各声道同相），用于量均衡器频响。</summary>
        private sealed class SineSource : ISampleProvider
        {
            private readonly int _ch;
            private readonly double _rate, _freq;
            private readonly float _amp;
            private readonly int _frames;
            private long _pos;

            public SineSource(int channels, int rate, double freq, float amp, int frames)
            {
                _ch = channels; _rate = rate; _freq = freq; _amp = amp; _frames = frames;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / _ch;
                for (int f = 0; f < frames; f++)
                {
                    long p = _pos + f;
                    float v = p >= _frames ? 0f : (float)(_amp * Math.Sin(2 * Math.PI * _freq * p / _rate));
                    for (int c = 0; c < _ch; c++) buffer[offset + f * _ch + c] = v;
                }
                _pos += frames;
                return frames * _ch;
            }
        }

        /// <summary>每帧每声道一个不同常量，用于恒等测试。</summary>
        private sealed class RampSource : ISampleProvider
        {
            public float[] Data { get; }
            private int _pos;

            public RampSource(int channels, int frames)
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
                Data = new float[frames * channels];
                for (int f = 0; f < frames; f++)
                    for (int c = 0; c < channels; c++)
                        Data[f * channels + c] = (float)(Math.Sin(f * 0.01 + c) * 0.5);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int n = Math.Min(count, Data.Length - _pos);
                if (n <= 0) { Array.Clear(buffer, offset, count); return count; }
                Array.Copy(Data, _pos, buffer, offset, n);
                _pos += n;
                if (n < count) Array.Clear(buffer, offset + n, count - n);
                return count;
            }
        }
    }
}
