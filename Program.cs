using System;
using System.Windows.Forms;

namespace Surround71
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                string log = ArgValue(args, "--log");

                if (HasFlag(args, "--selftest")) { Environment.ExitCode = SelfTest.Run(log); return; }
                if (HasFlag(args, "--listdevices")) { Environment.ExitCode = Cli.ListDevices(log); return; }
                if (HasFlag(args, "--probeformats")) { Environment.ExitCode = Cli.ProbeFormats(log); return; }
                if (HasFlag(args, "--loopbacktest")) { Environment.ExitCode = Cli.LoopbackTest(log, IntArg(args, "seconds", 8)); return; }
                if (HasFlag(args, "--uidiag")) { Environment.ExitCode = UiDiag.Run(log); return; }
                string sizeArg = ArgValue(args, "--size");
                if (!string.IsNullOrEmpty(sizeArg))
                {
                    var parts = sizeArg.Split('x', 'X');
                    if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int sw) &&
                        int.TryParse(parts[1].Trim(), out int sh) && sw >= 200 && sh >= 200)
                        MainForm.ForcedClientSize = new System.Drawing.Size(sw, sh);
                }
                if (HasFlag(args, "--setdefault"))
                {
                    string keyword = ArgValue(args, "--setdefault");
                    int index = -1;
                    string indexText = ArgValue(args, "--index");
                    if (indexText != null && int.TryParse(indexText, out int parsed)) index = parsed;
                    Environment.ExitCode = Cli.SetDefault(keyword, index, log);
                    return;
                }
            }

            // 命令行诊断模式不参与单实例保护（可与其他 GUI 同时跑）
            bool isGui = args == null || args.Length == 0;

            // 单实例：已经在跑（托盘常驻）时，把上一个实例的主窗口顶出来并退出本实例
            var mutex = new Mutex(true, "Surround71_SingleInstance", out bool createdNow);
            if (!createdNow && isGui)
            {
                try
                {
                    if (EventWaitHandle.TryOpenExisting("Surround71_ShowWindow_Ask", out var evt))
                        evt.Set();
                }
                catch { }
                return;
            }
            GC.KeepAlive(mutex);

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 出问题时把堆栈写进 %TEMP%\Surround71_error.log，方便排查（界面不该崩，但万一崩了要留证据）
            Application.ThreadException += (s, e) => LogCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                MessageBox.Show("程序发生错误：\n" + ex.Message, "PC 7.1 环绕声",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>把异常堆栈追加到 %TEMP%\Surround71_error.log。</summary>
        internal static void LogCrash(Exception ex)
        {
            try
            {
                string p = Path.Combine(Path.GetTempPath(), "Surround71_error.log");
                File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                                       (ex?.ToString() ?? "(null)") + "\r\n\r\n");
            }
            catch { }
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static int IntArg(string[] args, string name, int defaultValue)
        {
            var s = ArgValue(args, name);
            if (int.TryParse(s, out int v)) return v;
            return defaultValue;
        }
    }
}
