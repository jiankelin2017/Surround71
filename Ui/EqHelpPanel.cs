using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Surround71.Ui
{
    /// <summary>
    /// 六段均衡器的详细使用说明（整窗浮层）。
    ///
    /// 入口是隐藏的：在「7.1 声场」卡片上连点 3 下才出来，再点一下任意位置收起。
    /// 之所以做成整窗浮层而不是塞在声场卡片里 —— 卡片只有 514×282 设计单位，
    /// 放下这份说明会挤成一团；整窗浮层才有地方把六个频段、四种滤波类型讲清楚。
    /// </summary>
    internal sealed class EqHelpPanel : ThemedControl
    {
        private const float PadOut = 34f;      // 面板内边距
        private const float TitleH = 44f;      // 标题区高度
        private const float ColGap = 34f;      // 两栏间距
        private const float LineH = 17.6f;     // 正文行高
        private const float HeadGap = 9f;      // 小标题上方留白

        /// <summary>标题 / 小标题 / 正文（以 · 开头的是条目）。null 小标题表示只画正文。</summary>
        private static readonly (string head, string[] body)[] LeftBlocks =
        {
            ("这是什么", new[]
            {
                "六段参数均衡器：把声音分成 6 个频段，每段可以单独提升或衰减，最多 ±20 dB。",
                "卡片右上角的「启用」是总开关，关掉时整条曲线都不参与处理。",
            }),
            ("六个频段管什么", new[]
            {
                "① 70 Hz 低架 — 超低音、下潜、电影里的轰鸣",
                "② 115 Hz 峰值 — 鼓的冲击感、男声的胸腔",
                "③ 240 Hz 峰值 — 声音的厚度，加多了会发闷",
                "④ 1.2 kHz 峰值 — 人声主体、吉他、对白",
                "⑤ 6 kHz 峰值 — 齿音、唇齿细节、清晰度",
                "⑥ 12 kHz 高架 — 空气感、镲片泛音",
            }),
            ("怎么操作", new[]
            {
                "· 拖圆点：左右改频率，上下改增益",
                "· 双击圆点：单独关掉这一段（灰掉 = 旁路）",
                "· 点空白处：自动选中离你最近的那一段",
                "· 每段下面的「类型 / 频率」可以精确设置",
                "· 右侧竖条「总增益」：整体音量补偿",
            }),
        };

        private static readonly (string head, string[] body)[] RightBlocks =
        {
            ("四种滤波类型", new[]
            {
                "峰值 Peak — 只动中心频率附近，最常用",
                "低架 Low Shelf — 从该频率往低处整体提降",
                "高架 High Shelf — 从该频率往高处整体提降",
                "带阻 Notch — 只削掉很窄的一条，不需要增益",
            }),
            ("常用调法", new[]
            {
                "· 觉得闷：240 Hz 降 2~3 dB",
                "· 人声糊：1.2 kHz 加 2、6 kHz 加 2",
                "· 齿音刺耳：6 kHz 降 3~4",
                "· 低音不够：70 Hz 加 4~6，同时把总增益降到 -1~-2",
                "· 只想保原味：预设选「平直（不调）」",
            }),
            ("三个提醒", new[]
            {
                "· 提得多一定要减总增益，否则会削波失真",
                "· 拖动即时生效，正在播的声音会立刻变",
                "· 预设只是起点，按你自己的耳机微调",
            }),
        };

        /// <summary>点任意位置收起时通知主窗体（主窗体自己维护"是否展开"的状态）。</summary>
        public Action CloseRequested;

        public EqHelpPanel()
        {
            Cursor = Cursors.Hand;
            DoubleBuffered = true;   // 半透明遮罩整块画，切换时不露底
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            CloseRequested?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var T2 = T;

            float w = Width / S, h = Height / S;

            // 1) 遮罩：把底下的界面压暗，视线集中到说明上
            using (var b = new SolidBrush(Color.FromArgb(150, 6, 10, 8)))
                g.FillRectangle(b, 0, 0, Width, Height);

            // 2) 说明面板：居中、不超出窗口
            float pw = Math.Min(920f, w - 40f);
            float ph = Math.Min(446f, h - 40f);
            if (pw < 260f || ph < 200f) return;
            float px = (w - pw) / 2f, py = (h - ph) / 2f;

            using (var b = new SolidBrush(Color.FromArgb(255, T2.Card)))
                Shaper.FillRound(g, b, Rect(px * S, py * S, pw * S, ph * S), 18f * S);
            using (var p = new Pen(Color.FromArgb(200, T2.CardBorder), 1f * S))
                Shaper.DrawRound(g, p, Rect((px + 0.5f) * S, (py + 0.5f) * S, (pw - 1f) * S, (ph - 1f) * S), 18f * S);

            float x0 = px + PadOut, y0 = py + 20f;
            float cw = pw - PadOut * 2f;

            // 3) 标题
            using (var f = Fx.F(18f, FontStyle.Bold))
                TX(g, "六段均衡器 · 使用说明", f, T2.Title, x0, y0, cw - 150f, 28f);
            using (var f = Fx.F(11.5f))
                TX(g, "点任意位置关闭", f, T2.Muted, x0 + cw - 150f, y0 + 2f, 150f, 26f,
                   ContentAlignment.MiddleRight);

            // 4) 标题下的分隔线
            float dy = y0 + TitleH;
            using (var p = new Pen(Color.FromArgb(70, T2.Muted), 1f * S))
                g.DrawLine(p, x0 * S, dy * S, (x0 + cw) * S, dy * S);

            // 5) 两栏正文
            float colW = (cw - ColGap) / 2f;
            DrawColumn(g, LeftBlocks, x0, dy + 14f, colW, ph - (dy - py) - 44f, T2);
            DrawColumn(g, RightBlocks, x0 + colW + ColGap, dy + 14f, colW, ph - (dy - py) - 44f, T2);

            // 6) 底部提示
            using (var f = Fx.F(11.5f))
                TX(g, "在「7.1 声场」卡片上连点 3 次，可随时打开 / 关闭这份说明",
                   f, T2.Muted, x0, py + ph - 34f, cw, 22f, ContentAlignment.MiddleCenter);
        }

        private void DrawColumn(Graphics g, (string head, string[] body)[] blocks,
                                float x, float y, float w, float maxH, UiTheme T2)
        {
            float cy = y;
            float bottom = y + maxH;
            foreach (var blk in blocks)
            {
                if (blk.head != null)
                {
                    cy += HeadGap;
                    if (cy + 20f > bottom) return;
                    using (var f = Fx.F(13f, FontStyle.Bold))
                        TX(g, blk.head, f, T2.Accent, x, cy, w, 20f);
                    cy += 21f;
                }
                foreach (var line in blk.body)
                {
                    if (cy + LineH > bottom) return;
                    // 条目行用正文色，说明性长句用稍暗的色，读起来有层次
                    bool bullet = line.StartsWith("·") || line.StartsWith("①") || line.StartsWith("②")
                                  || line.StartsWith("③") || line.StartsWith("④") || line.StartsWith("⑤")
                                  || line.StartsWith("⑥");
                    using (var f = Fx.F(12f))
                        TX(g, line, f, bullet ? T2.Text : T2.Muted, x, cy, w, LineH);
                    cy += LineH;
                }
            }
        }
    }
}
