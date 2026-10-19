using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Surround71.Audio;

namespace Surround71.Ui
{
    /// <summary>
    /// 7.1 声场图：绿色枪靶（中心实、边缘渐隐到透明）+ 人头 + 8 个音箱点。
    /// 点击音箱点 → 单独试听该声道；正在播放时用橙色电平环标出当前声道。
    /// </summary>
    internal sealed class TargetPanel : ThemedControl
    {
        public event Action<int> ChannelClicked;

        /// <summary>在声场图上连点 3 下 —— 隐藏入口，用来打开均衡器使用说明。</summary>
        public event Action SecretGesture;

        private int _clickCount;
        private long _lastClickTicks;
        private bool _swallowClick;      // 手势刚触发，吞掉紧跟的那一次 Click

        private int _active = -1;
        private int _hover = -1;
        private readonly RectangleF[] _hit = new RectangleF[Layout71.Channels];

        public TargetPanel()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, false);
        }

        public int ActiveChannel
        {
            get => _active;
            set { if (_active == value) { return; } _active = value; Invalidate(); }
        }

        public float PeakL, PeakR;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            // 连点检测放在 MouseDown 而不是 MouseClick：
            // 快速连点时 WM_LBUTTONUP 常被消息循环吞掉（实测 4 次点击只到 2 个 Click），
            // 但 DOWN 每次都稳到。凑满 3 下就当作"隐藏手势"，并吞掉随之而来的那一次 Click，
            // 免得开说明的同时把某个声道点响。
            long now = Environment.TickCount64;
            if (_clickCount == 0 || now - _lastClickTicks > 1200) _clickCount = 1;
            else _clickCount++;
            _lastClickTicks = now;
            if (_clickCount >= 3)
            {
                _clickCount = 0;
                _swallowClick = true;
                SecretGesture?.Invoke();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (_swallowClick) { _swallowClick = false; return; }

            int h = HitTest(e.Location);
            if (h >= 0) ChannelClicked?.Invoke(h);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _hit.Length; i++)
                if (_hit[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var T2 = T;

            float w = Width, h = Height;
            float cx = w / 2f;
            float cy = h * 0.545f;
            float R = Math.Min(w * 0.36f, h * 0.38f);
            float rSpeaker = Math.Max(11f, Math.Min(w, h) * 0.056f);
            float labelFs = Math.Clamp(Math.Min(w, h) * 0.030f, 8f, 12.5f);
            float discR = R * 1.18f;

            // ===== 靶面：中心实心绿 → 边缘完全透明 =====
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - discR, cy - discR, discR * 2, discR * 2);
                using (var bg = new PathGradientBrush(gp))
                {
                    bg.CenterPoint = new PointF(cx, cy);
                    bg.CenterColor = T2.TargetCore;
                    bg.SurroundColors = new[] { T2.TargetEdge };
                    bg.SetSigmaBellShape(0.62f);      // 中心实、往外平滑淡出
                    g.FillPath(bg, gp);
                }
            }

            // ===== 同心圆环 + 十字 =====
            using (var pen = new Pen(T2.TargetRing, 1.3f))
            {
                for (int i = 1; i <= 3; i++)
                    g.DrawEllipse(pen, cx - R * i / 3f, cy - R * i / 3f, R * i * 2 / 3f, R * i * 2 / 3f);
                g.DrawLine(pen, cx - discR * 0.98f, cy, cx + discR * 0.98f, cy);
                g.DrawLine(pen, cx, cy - discR * 0.98f, cx, cy + discR * 0.98f);
            }

            // 方位标记
            using (var f = Fx.F(labelFs))
            using (var br = new SolidBrush(T2.DirMark))
            {
                DrawC(g, br, f, "前", cx - 10, cy - R - 26, 20, 18);
                DrawC(g, br, f, "后", cx - 10, cy + R + 8, 20, 18);
                DrawC(g, br, f, "左", cx - discR - 28, cy - 9, 20, 18);
                DrawC(g, br, f, "右", cx + discR + 8, cy - 9, 20, 18);
            }

            // ===== 人头 =====
            float headR = Math.Max(15f, R * 0.215f);
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - headR, cy - headR, headR * 2, headR * 2);
                using (var body = new PathGradientBrush(gp))
                {
                    body.CenterPoint = new PointF(cx, cy - headR * 0.35f);
                    body.CenterColor = T2.HeadFill;
                    body.SurroundColors = new[] { Color.FromArgb(200, T2.HeadFill) };
                    g.FillPath(body, gp);
                }
                using (var p = new Pen(T2.HeadRing, 1.6f))
                    g.DrawEllipse(p, cx - headR, cy - headR, headR * 2, headR * 2);
                using (var nose = new SolidBrush(T2.HeadRing))
                    g.FillPolygon(nose, new[]
                    {
                        new PointF(cx, cy - headR - 5),
                        new PointF(cx - 5, cy - headR + 5),
                        new PointF(cx + 5, cy - headR + 5),
                    });
            }
            using (var lf = Fx.F(labelFs, FontStyle.Bold))
            using (var lbr = new SolidBrush(T2.DirMark))
            {
                DrawC(g, lbr, lf, "L", cx - headR - 30, cy - 9, 20, 18);
                DrawC(g, lbr, lf, "R", cx + headR + 10, cy - 9, 20, 18);
            }

            // ===== 8 个音箱 =====
            for (int i = 0; i < Layout71.Channels; i++)
            {
                float az = Layout71.Azimuth[i] * (float)Math.PI / 180f;
                bool lfe = Layout71.IsLfe[i];
                float rr = lfe ? R * 0.52f : R;
                float x = cx + rr * (float)Math.Sin(az);
                float y = cy - rr * (float)Math.Cos(az);
                if (lfe) { x = cx + R * 0.32f; y = cy - R * 0.32f; }

                bool active = i == _active;
                bool hover = i == _hover;
                Color fill = lfe ? T2.SpkLfe : T2.Spk;
                if (hover) fill = ControlPaint.Light(fill, 0.28f);
                if (active) fill = T2.Accent;

                var rect = new RectangleF(x - rSpeaker, y - rSpeaker, rSpeaker * 2, rSpeaker * 2);
                _hit[i] = rect;
                using (var b = new SolidBrush(fill)) g.FillEllipse(b, rect);
                using (var p = new Pen(T2.SpkRing, active ? 2.6f : 1.8f)) g.DrawEllipse(p, rect);

                if (active && (PeakL > 0.01f || PeakR > 0.01f))
                {
                    float lvl = Math.Min(1f, Math.Max(PeakL, PeakR) * 1.3f);
                    using (var p = new Pen(Color.FromArgb(255, 205, 110), 2f))
                    {
                        float rr2 = rSpeaker + 4 + lvl * 10;
                        g.DrawEllipse(p, x - rr2, y - rr2, rr2 * 2, rr2 * 2);
                    }
                }

                using (var f = Fx.F(labelFs, FontStyle.Bold))
                using (var br = new SolidBrush(active ? T2.OnAccent : T2.SpkText))
                    DrawC(g, br, f, Layout71.ShortNames[i], x - rSpeaker, y - rSpeaker, rSpeaker * 2, rSpeaker * 2);
            }
        }

        private static void DrawC(Graphics g, Brush br, Font f, string s, float x, float y, float w, float h)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(s, f, br, new RectangleF(x, y, w, h), sf);
            }
        }
    }
}
