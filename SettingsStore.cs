using System;
using System.IO;
using System.Text.Json;
using Surround71.Audio;

namespace Surround71
{
    /// <summary>
    /// 用户设置的持久化（%AppData%\Surround71\settings.json）。
    ///
    /// 以前注册表里只存了主题一个键，设备选择、预设、滑杆、均衡器、开关每次开机都要重配一遍。
    /// 这里把整份 <see cref="SurroundSettings"/> 加上设备 ID（不是下拉框索引，插拔后会错位）
    /// 和几个界面开关一起存；字段全部走 SurroundSettings 自己的 public field，JSON 直接含字段序列化。
    /// </summary>
    internal static class SettingsStore
    {
        public sealed class Data
        {
            public string OutputId;        // 输出设备端点 ID
            public string CaptureId;       // 环回来源端点 ID
            public int PresetIndex = -1;   // 环绕预设（-1 = 没存过）
            public bool? MuteSrc;          // 环回时静音环回来源
            public bool? AutoSwitch;       // 启动时自动接管默认播放设备
            public bool Latch;             // 延迟更低（30ms）
            public bool HaveAudio;         // Audio 是否有效（第一次运行时没有）
            public SurroundSettings Audio; // 全部音效参数 + 均衡器
        }

        private static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Surround71");

        private static string FilePath => Path.Combine(Dir, "settings.json");

        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            IncludeFields = true,
            WriteIndented = false,
        };

        public static Data Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Data();
                var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath), JsonOpt);
                return d ?? new Data();
            }
            catch { return new Data(); }   // 配置文件坏了就用默认值，绝不让程序起不来
        }

        public static void Save(Data d)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(d, JsonOpt));
                // 先写临时文件再替换：写一半断电/被杀也不会留下半份坏配置
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch { }
        }
    }
}
