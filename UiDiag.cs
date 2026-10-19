using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Surround71
{
    /// <summary>界面诊断：把 DPI / 缩放 / 控件尺寸的真实数值写出来，用来排查布局问题。</summary>
    internal static class UiDiag
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

        public static int Run(string logPath)
        {
            if (string.IsNullOrEmpty(logPath))
                logPath = Path.Combine(Path.GetTempPath(), "surround71_uidiag.txt");

            var writers = new List<TextWriter>
            {
                new StreamWriter(logPath, false, Encoding.UTF8) { AutoFlush = true }
            };
            try
            {
                if (AttachConsole(-1) || AllocConsole())
                    writers.Add(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true });
            }
            catch { }
            Console.SetOut(new MultiWriter(writers.ToArray()));

            Console.WriteLine("=== 界面 / DPI 诊断 ===");

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();

            // ---- 设备名探针：找出哪个属性能拿到名字 ----
            Console.WriteLine("--- 设备名探针 ---");
            try
            {
                var en = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                foreach (var d in en.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.All, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    Console.WriteLine($"[{d.DataFlow}] id={d.ID}");
                    TryProp(d, "FriendlyName", () => d.FriendlyName);
                    TryProp(d, "DeviceFriendlyName", () => d.DeviceFriendlyName);
                    TryProp(d, "InstanceId", () => d.InstanceId);
                }
            }
            catch (Exception ex) { Console.WriteLine("设备枚举失败: " + ex.Message); }

            var f = new MainForm();
            f.CreateControl();

            Console.WriteLine("--- 启动时滑杆与设置值 ---");
            try
            {
                var st = f.GetType().GetField("_settings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(f);
                if (st != null)
                    foreach (var fld in st.GetType().GetFields())
                        Console.WriteLine("   设置 " + fld.Name + " = " + fld.GetValue(st));
                if (f.GetType().GetField("_sliders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(f) is System.Collections.Generic.Dictionary<string, Surround71.Ui.ModernSlider> sl)
                    foreach (var kv in sl)
                        Console.WriteLine("   滑杆 " + kv.Key + " = " + kv.Value.Value + "（范围 " + kv.Value.Min + ".." + kv.Value.Max + "）");
                Console.WriteLine("   主题 = " + Surround71.Ui.UiThemes.Current.Kind + "（只有白色一套）");
            }
            catch (Exception ex) { Console.WriteLine("   转储失败: " + ex.Message); }

            Console.WriteLine($"DeviceDpi            = {f.DeviceDpi}");
            Console.WriteLine($"AutoScaleMode        = {f.AutoScaleMode}");
            Console.WriteLine($"AutoScaleDimensions  = {f.AutoScaleDimensions}");
            Console.WriteLine($"ClientSize           = {f.ClientSize}");
            Console.WriteLine($"Size                 = {f.Size}");
            Console.WriteLine($"Font                 = {f.Font}  (Height={f.Font.Height}pt)");
            try
            {
                var scr = Screen.PrimaryScreen;
                Console.WriteLine($"Screen.Bounds        = {scr.Bounds}");
                Console.WriteLine($"Screen.WorkingArea   = {scr.WorkingArea}");
            }
            catch (Exception e) { Console.WriteLine("Screen: " + e.Message); }

            using (var g = f.CreateGraphics())
            {
                Console.WriteLine("--- 文字实测宽度（与设计宽度对比）---");
                foreach (var t in new[] { "输出设备", "采集方式", "采集设备", "上混强度", "外部化 / 空间感", "声场宽度", "耳机 — 虚拟 7.1 环绕（HRTF 双耳化，推荐）", "启动全局时自动把默认播放设备切到虚拟声卡" })
                {
                    var sz = g.MeasureString(t, f.Font);
                    Console.WriteLine($"   {t,-46} 实测 {sz.Width,7:0.0} x {sz.Height,5:0.0}");
                }
                Console.WriteLine($"   平均字符宽 {g.MeasureString("MMMMMMMMMM", f.Font).Width / 10.0:0.00}");
            }

            Console.WriteLine("--- 控件树（前 3 层）---");
            Dump(f, 0, 3);

            return 0;
        }

        private static void TryProp(NAudio.CoreAudioApi.MMDevice d, string what, Func<string> get)
        {
            try { Console.WriteLine($"   {what,-40} = \"{get()}\""); }
            catch (Exception ex) { Console.WriteLine($"   {what,-40} 异常: {ex.Message}"); }
        }

        private static void Dump(Control c, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            string pad = new string(' ', depth * 2);
            string text = c.Text ?? "";
            if (text.Length > 24) text = text.Substring(0, 24) + "…";
            Console.WriteLine($"{pad}{c.GetType().Name,-16} Bounds={c.Bounds} Text=\"{text}\"");
            foreach (Control ch in c.Controls) Dump(ch, depth + 1, maxDepth);
        }
    }
}
