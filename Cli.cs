using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Surround71.Audio;
using Surround71.Interop;

namespace Surround71
{
    /// <summary>命令行工具模式：列设备 / 切换默认播放设备。</summary>
    internal static class Cli
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        private sealed class MultiWriter : TextWriter
        {
            private readonly TextWriter[] _w;
            public MultiWriter(params TextWriter[] w) { _w = w; }
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char c) { foreach (var w in _w) w.Write(c); }
            public override void Write(string s) { foreach (var w in _w) w.Write(s); }
            public override void WriteLine(string s) { foreach (var w in _w) w.WriteLine(s); }
            public override void Flush() { foreach (var w in _w) w.Flush(); }
        }

        private static void Init(string logPath)
        {
            var writers = new System.Collections.Generic.List<TextWriter>();
            if (!string.IsNullOrEmpty(logPath))
                writers.Add(new StreamWriter(logPath, false, Encoding.UTF8) { AutoFlush = true });
            try
            {
                if (AttachConsole(-1) || AllocConsole())
                    writers.Add(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true });
            }
            catch { }
            Console.SetOut(new MultiWriter(writers.ToArray()));
        }

        private static string Name(MMDevice d)
        {
            try { var n = d.FriendlyName; if (!string.IsNullOrWhiteSpace(n)) return n; } catch { }
            try { var n = d.DeviceFriendlyName; if (!string.IsNullOrWhiteSpace(n)) return n; } catch { }
            return "(设备名不可用)";
        }

        private static string Fmt(MMDevice d)
        {
            try
            {
                var m = d.AudioClient.MixFormat;
                return $"{m.SampleRate}Hz {m.Channels}ch {m.BitsPerSample}bit";
            }
            catch { return "?"; }
        }

        public static int ListDevices(string logPath)
        {
            Init(logPath);
            Console.WriteLine("=== 音频设备列表 ===");
            using var en = new MMDeviceEnumerator();
            Console.WriteLine("--- 输出设备 (Render) ---");
            int i = 0;
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                Console.WriteLine($"  [{i++,2}] {Name(d),-46} {Fmt(d),-28} {d.ID}");

            Console.WriteLine("--- 采集设备 (Capture) ---");
            i = 0;
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                Console.WriteLine($"  [{i++,2}] {Name(d),-46} {Fmt(d),-28} {d.ID}");

            try
            {
                var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Console.WriteLine("当前默认播放设备：" + Name(def));
            }
            catch (Exception e) { Console.WriteLine("无法读取默认设备：" + e.Message); }

            return 0;
        }

        /// <summary>
        /// 端到端验证“系统环回”路径：默认播放设备上放一段测试曲，程序环回抓它的声音，
        /// 做 7.1 处理后从另一个输出设备放出去，报告实际采集的帧数与峰值。
        /// </summary>
        public static int LoopbackTest(string logPath, int seconds)
        {
            Init(logPath);
            if (seconds < 2) seconds = 8;
            Console.WriteLine($"=== 系统环回端到端测试（{seconds} 秒）===");

            using var en = new MMDeviceEnumerator();
            var renderDevices = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                  .Where(d => Name(d) != "(设备名不可用)").ToList();
            if (renderDevices.Count < 2)
            {
                Console.WriteLine("需要两个输出设备（抓一个、听另一个），当前只有 " + renderDevices.Count + " 个。");
                return 1;
            }

            MMDevice captureDevice;
            try { captureDevice = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
            catch (Exception e) { Console.WriteLine("读默认设备失败：" + e.Message); return 1; }

            var outputDevice = renderDevices.FirstOrDefault(d => d.ID != captureDevice.ID)
                               ?? renderDevices[0];

            Console.WriteLine($"  环回来源（抓）  : {Name(captureDevice)}  [{Fmt(captureDevice)}]");
            Console.WriteLine($"  输出设备（听）  : {Name(outputDevice)}  [{Fmt(outputDevice)}]");
            Console.WriteLine("  提示：等结束后如果有声音从另一台设备里流出，说明整条链路通了。");

            using var eng = new SurroundEngine(captureDevice, outputDevice);

            // 用测试曲直接推到默认播放设备，制造可采集的声源
            int rate = captureDevice.AudioClient.MixFormat.SampleRate;
            using var srcWave = new WasapiOut(captureDevice, AudioClientShareMode.Shared, false, 80);
            srcWave.Init(new Audio.TestTuneProvider(rate));
            srcWave.Play();

            eng.Start(SurroundPresets.All().First(p => p.Name == "电影环绕"), 60, CaptureKind.Loopback);

            System.Threading.Thread.Sleep(seconds * 1000);
            srcWave.Stop();

            Console.WriteLine($"  运行中={eng.IsRunning}  已采集帧={eng.CapturedFrames:N0}  丢帧={eng.DroppedSamples}");
            Console.WriteLine($"  峰值 L={eng.PeakL:0.000}  R={eng.PeakR:0.000}  输出格式={eng.OutputFormat}");

            var ok = eng.CapturedFrames > 0 && (eng.PeakL > 0.01f || eng.PeakR > 0.01f);
            Console.WriteLine(ok ? "结果：通过 —— 声音被抓到并被处理了。" : "结果：没有采集到声音（确认默认播放设备上确实有声音在放）。");
            eng.Stop();
            return ok ? 0 : 1;
        }

        /// <summary>探测每个设备支持的声道数 / 采样格式，用于判断能否走真 7.1。</summary>
        public static int ProbeFormats(string logPath)
        {
            Init(logPath);
            Console.WriteLine("=== 设备格式支持探测 ===");
            using var en = new MMDeviceEnumerator();

            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                Console.WriteLine($"--- {(flow == DataFlow.Render ? "输出设备 (Render)" : "采集设备 (Capture)")} ---");
                foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    Console.WriteLine("· " + Name(d));
                    try
                    {
                        var mix = d.AudioClient.MixFormat;
                        Console.WriteLine($"    当前混音格式：{mix.SampleRate}Hz {mix.Channels}ch {mix.BitsPerSample}bit {mix.Encoding}");

                        int rate = mix.SampleRate;
                        foreach (int ch in new[] { 1, 2, 4, 6, 8 })
                        {
                            bool f32 = false, i16 = false;
                            try { f32 = d.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, new WaveFormatExtensible(rate, 32, ch)); } catch { }
                            try { i16 = d.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, new WaveFormatExtensible(rate, 16, ch)); } catch { }
                            Console.WriteLine($"    {ch} 声道：  float32={(f32 ? "支持" : "  — ")}   int16={(i16 ? "支持" : "  — ")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("    探测失败：" + ex.Message);
                    }
                }
            }

            return 0;
        }

        public static int SetDefault(string keyword, int exactIndex, string logPath)
        {            Init(logPath);
            using var en = new MMDeviceEnumerator();
            var list = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

            MMDevice target = null;
            if (exactIndex >= 0)
            {
                if (exactIndex < list.Count) target = list[exactIndex];
            }
            else if (!string.IsNullOrEmpty(keyword))
            {
                target = list.FirstOrDefault(d => Name(d).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (target == null)
            {
                Console.WriteLine($"未找到匹配 \"{keyword}\" 的输出设备。");
                foreach (var d in list) Console.WriteLine("  · " + Name(d));
                return 2;
            }

            bool ok = PolicyConfig.SetDefaultPlayback(target.ID);
            Console.WriteLine($"{(ok ? "已设置默认播放设备：" : "设置失败：")}{Name(target)}");
            return ok ? 0 : 3;
        }
    }
}
