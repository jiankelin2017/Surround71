using System;

namespace Surround71.Audio
{
    /// <summary>7.1 声道定义（Windows WAVE_FORMAT_EXTENSIBLE 顺序）。</summary>
    public static class Layout71
    {
        public const int Channels = 8;

        public const int FL = 0, FR = 1, FC = 2, LFE = 3, BL = 4, BR = 5, SL = 6, SR = 7;

        public static readonly string[] ShortNames = { "FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR" };

        public static readonly string[] Names =
        {
            "前左", "前右", "中置", "低音", "后左", "后右", "侧左", "侧右"
        };

        /// <summary>虚拟音箱方位角（0=正前，正=右，单位度）。</summary>
        public static readonly float[] Azimuth = { -30f, 30f, 0f, 0f, -110f, 110f, -90f, 90f };

        /// <summary>虚拟音箱仰角（单位度）。</summary>
        public static readonly float[] Elevation = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };

        /// <summary>低音通道：不做 HRTF，直接等量送左右耳。</summary>
        public static readonly bool[] IsLfe = { false, false, false, true, false, false, false, false };

        /// <summary>标准 7.1 声道掩码。</summary>
        public const int ChannelMask =
            (1 << 0)  |  // FL
            (1 << 1)  |  // FR
            (1 << 2)  |  // FC
            (1 << 3)  |  // LFE
            (1 << 4)  |  // BL
            (1 << 5)  |  // BR
            (1 << 9)  |  // SL
            (1 << 10);   // SR

        /// <summary>把方位角缩放到 (0, 180] 的可视化用（用于画图）。</summary>
        public static float VisualAngle(int channel) => Azimuth[channel];
    }

    /// <summary>效果参数。</summary>
    public sealed class SurroundSettings
    {
        public string Name = "";
        public string Description = "";

        /// <summary>true = 耳机虚拟 7.1（HRTF 双耳化）；false = 真实 7.1 音箱直通。</summary>
        public bool HeadphoneMode = true;

        /// <summary>立体声上混成 7.1 的强度 0..1。</summary>
        public float UpmixStrength = 0.85f;

        /// <summary>外部化 / 空间感（早期反射强度）0..1。</summary>
        public float Externalize = 0.35f;

        /// <summary>低音管理增益 dB（LFE 通道电平）。</summary>
        public float BassBoostDb = 2f;

        /// <summary>低音管理强度 0..1：把 &lt;110Hz 的能量等量补到双耳，低频更结实。</summary>
        public float BassManagement = 0.25f;

        /// <summary>输出总增益 dB。</summary>
        public float OutputGainDb = 0f;

        /// <summary>虚拟声场宽度（对双耳输出的 M/S 展宽）。</summary>
        public float Width = 1.0f;

        /// <summary>环绕声道（BL/BR/SL/SR）相对电平 dB。</summary>
        public float SurroundLevelDb = 0f;

        /// <summary>中置声道相对电平 dB。</summary>
        public float CenterLevelDb = 0f;

        /// <summary>每声道增益 dB（音箱模式）。</summary>
        public float[] SpeakerGainDb = new float[Layout71.Channels];

        /// <summary>每声道延迟 ms（音箱模式，模拟距离）。</summary>
        public float[] SpeakerDelayMs = new float[Layout71.Channels];

        // ---------------- 均衡器（六段参数均衡，作用于最终输出）----------------
        /// <summary>是否启用均衡器。</summary>
        public bool EqEnabled = false;

        /// <summary>总增益 dB（-20..+20）。</summary>
        public float EqPreampDb = 0f;

        /// <summary>六段的中心频率 Hz。</summary>
        public float[] EqFreq = (float[])Audio.EqBands.DefaultFreq.Clone();

        /// <summary>六段的增益 dB（-20..+20）。</summary>
        public float[] EqGains = new float[Audio.EqBands.Count];

        /// <summary>六段各自的滤波器类型（见 EqFilterType）。</summary>
        public int[] EqType = (int[])Audio.EqBands.DefaultType.Clone();

        /// <summary>六段是否启用（与图 5 里灰掉的 Band 对应）。</summary>
        public bool[] EqBandOn = new bool[Audio.EqBands.Count] { true, true, true, true, true, true };

        /// <summary>参数版本号：界面改动后 +1，DSP 检测到变化就重算滤波器系数（实现"拖动即时生效"）。</summary>
        public int EqVersion = 1;

        public SurroundSettings Clone()
        {
            var c = (SurroundSettings)MemberwiseClone();
            c.SpeakerGainDb = (float[])SpeakerGainDb.Clone();
            c.SpeakerDelayMs = (float[])SpeakerDelayMs.Clone();
            c.EqFreq = (float[])EqFreq.Clone();
            c.EqGains = (float[])EqGains.Clone();
            c.EqType = (int[])EqType.Clone();
            c.EqBandOn = (bool[])EqBandOn.Clone();
            return c;
        }

        /// <summary>
        /// 把另一份设置的参数**就地**搬过来（不换对象、数组也不换）。
        ///
        /// 运行中的 DSP 持有的是这个对象的引用（逐块读参数），所以换预设时必须改内容而不是换引用，
        /// 否则正在跑的处理链还在读老对象 —— 那就只能停下来重开才能生效。
        /// 数组也要逐元素拷：万一某个 DSP 缓存了数组引用，换掉引用就失效了。
        /// </summary>
        public void CopyFrom(SurroundSettings o)
        {
            if (o == null || ReferenceEquals(o, this)) return;
            Name = o.Name;
            Description = o.Description;
            HeadphoneMode = o.HeadphoneMode;
            UpmixStrength = o.UpmixStrength;
            Externalize = o.Externalize;
            BassBoostDb = o.BassBoostDb;
            BassManagement = o.BassManagement;
            OutputGainDb = o.OutputGainDb;
            Width = o.Width;
            SurroundLevelDb = o.SurroundLevelDb;
            CenterLevelDb = o.CenterLevelDb;
            CopyInto(SpeakerGainDb, o.SpeakerGainDb);
            CopyInto(SpeakerDelayMs, o.SpeakerDelayMs);
            EqEnabled = o.EqEnabled;
            EqPreampDb = o.EqPreampDb;
            CopyInto(EqFreq, o.EqFreq);
            CopyInto(EqGains, o.EqGains);
            CopyInto(EqType, o.EqType);
            CopyInto(EqBandOn, o.EqBandOn);
            EqVersion++;      // 通知 DSP 重算系数（"拖动即时生效"靠的就是这个版本号）
        }

        private static void CopyInto<T>(T[] dst, T[] src)
        {
            if (dst == null || src == null) return;
            Array.Copy(src, dst, Math.Min(dst.Length, src.Length));
        }
    }

    /// <summary>内置预设。</summary>
    public static class SurroundPresets
    {
        public static SurroundSettings[] All()
        {
            return new[]
            {
                new SurroundSettings
                {
                    HeadphoneMode = true, UpmixStrength = 0.85f, Externalize = 0.60f,
                    BassBoostDb = 2f, OutputGainDb = 0f, Width = 1.0f,
                    SurroundLevelDb = 0f, CenterLevelDb = 0f
                }.Named("电影环绕", "中等上混 + 明显外部化，声场开阔"),
                new SurroundSettings
                {
                    HeadphoneMode = true, UpmixStrength = 1.0f, Externalize = 0.20f,
                    BassBoostDb = 4f, OutputGainDb = 0f, Width = 1.15f,
                    SurroundLevelDb = 2f, CenterLevelDb = 1f
                }.Named("游戏定位", "上混最强，脚步/枪声方位更明确"),
                new SurroundSettings
                {
                    HeadphoneMode = true, UpmixStrength = 0.45f, Externalize = 0.30f,
                    BassBoostDb = 1f, OutputGainDb = 0f, Width = 0.95f,
                    SurroundLevelDb = -3f, CenterLevelDb = 0f
                }.Named("音乐自然", "轻微上混，尽量不破坏原混音"),
                new SurroundSettings
                {
                    HeadphoneMode = true, UpmixStrength = 0.7f, Externalize = 0.25f,
                    BassBoostDb = 0f, OutputGainDb = 0f, Width = 1.0f,
                    SurroundLevelDb = -2f, CenterLevelDb = 4f
                }.Named("人声优先", "中置加强，对白更清楚"),
                new SurroundSettings
                {
                    HeadphoneMode = false, UpmixStrength = 0.9f, Externalize = 0f,
                    BassBoostDb = 0f, OutputGainDb = 0f, Width = 1.0f,
                    SurroundLevelDb = 0f, CenterLevelDb = 0f
                }.Named("真实 7.1 直通", "输出到 7.1 音箱，不做双耳化"),
            };
        }

        private static SurroundSettings Named(this SurroundSettings s, string name, string desc)
        {
            s.Name = name;
            s.Description = desc;
            return s;
        }
    }
}
