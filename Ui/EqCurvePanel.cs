using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Surround71.Audio;

namespace Surround71.Ui
{
    /// <summary>
    /// 六段参数均衡器的响应曲线面板（浅色薄荷面板 + 发光曲线 + 可拖拽编号点）。
    /// 直接读写 SurroundSettings 里的 EQ 数组，所以拖动即时生效；
    /// 【拖动=改增益/频率】【双击编号点=启用/停用该段】【滚轮=改选中段频率】。
    /// </summary>
    internal sealed class EqCurvePanel : ThemedControl
    {
        public SurroundSettings Settings;
        public int Rate = 48000;

        /// <summary>参数被拖动修改（主窗体据此 bump EqVersion 让 DSP 立刻重算）。</summary>
        public event Action Changed;

        public event Action<int> BandPicked;

        private int _dragBand = -1;
        private int _hoverBand = -1;
        private int _selected = -1;

        private GraphicsPath _wave;                 // 底纹波形（按尺寸缓存）
        private float _waveW = -1f, _waveH = -1f;
        private float[] _shown;                     // 实时频谱的显示值（低频在前，0~1）
        private float[] _centers;                   // 各段中心频率，用来把柱子摆到对数频率轴上

        private const float PadLd = 46f, PadRd = 14f, PadTd = 18f, PadBd = 28f;
        private const float DbRange = 22f;      // 纵轴 ±22 dB
        private const float DotR = 10f;

        public EqCurvePanel()
        {
            Cursor = Cursors.Hand;
        }

        private float Gw => Math.Max(20f, Width / S - PadLd - PadRd);
        private float Gh => Math.Max(20f, Height / S - PadTd - PadBd);
        private float ZeroY => PadTd + Gh / 2f;

        public int SelectedBand
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; Invalidate(); }
        }

        private float BandX(int b)
        {
            float f = Settings?.EqFreq != null && b < Settings.EqFreq.Length ? Settings.EqFreq[b] : EqBands.DefaultFreq[b];
            return PadLd + (float)EqualizerMath.FreqToX(f, Rate) * Gw;
        }

        private float BandY(int b)
        {
            float g = Settings?.EqGains != null && b < Settings.EqGains.Length ? Settings.EqGains[b] : 0f;
            return ZeroY - g / DbRange * (Gh / 2f);
        }

        private bool BandOn(int b) =>
            Settings?.EqBandOn == null || b >= Settings.EqBandOn.Length || Settings.EqBandOn[b];

        private int HitBand(float mx, float my)
        {
            int best = -1;
            float bestD = 16f;
            for (int i = 0; i < EqBands.Count; i++)
            {
                float dx = mx - BandX(i), dy = my - BandY(i);
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            float mx = e.X / S, my = e.Y / S;
            if (_dragBand >= 0)
            {
                ApplyDrag(_dragBand, mx, my);
                return;
            }
            int h = HitBand(mx, my);
            if (h != _hoverBand) { _hoverBand = h; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Settings == null) return;
            float mx = e.X / S, my = e.Y / S;
            int b = HitBand(mx, my);
            if (b < 0)
            {
                // 没点在点上：选最近的段（按 x 距离），方便直接拖
                float best = float.MaxValue;
                for (int i = 0; i < EqBands.Count; i++)
                {
                    float d = Math.Abs(mx - BandX(i));
                    if (d < best) { best = d; b = i; }
                }
                SelectedBand = b;
            }
            else SelectedBand = b;
            _dragBand = b;
            Capture = true;
            BandPicked?.Invoke(b);
            ApplyDrag(b, mx, my);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragBand = -1;
            Capture = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverBand = -1;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (Settings == null) return;
            int b = HitBand(e.X / S, e.Y / S);
            if (b < 0) return;
            var on = Settings.EqBandOn;
            if (on == null || b >= on.Length) return;
            on[b] = !on[b];
            Bump();
        }

        private void ApplyDrag(int b, float mx, float my)
        {
            if (Settings == null || b < 0 || b >= EqBands.Count) return;
            float x = (mx - PadLd) / Gw;
            double f = EqualizerMath.XToFreq(x, Rate);
            f = Math.Max(EqBands.MinFreq[b], Math.Min(EqBands.MaxFreq[b], f));
            float g = (ZeroY - my) / (Gh / 2f) * DbRange;
            g = Math.Max(-EqBands.MaxDb, Math.Min(EqBands.MaxDb, g));
            g = (float)(Math.Round(g * 2f) / 2f);      // 0.5 dB 步进
            if (Settings.EqFreq == null || Settings.EqFreq.Length <= b) return;
            Settings.EqFreq[b] = (float)Math.Round(f, f < 1000 ? 0 : -1);
            Settings.EqGains[b] = g;
            Bump();
        }

        private void Bump()
        {
            Invalidate();
            try { Changed?.Invoke(); } catch { }
        }

        public void SetBand(int b, float freq, float gain)
        {
            if (Settings == null || b < 0 || b >= EqBands.Count) return;
            Settings.EqFreq[b] = freq;
            Settings.EqGains[b] = gain;
            Invalidate();
        }

        public void RefreshCurve() => Invalidate();

        /// <summary>
        /// 主窗体在播放时每 ~33ms 灌一次实时频谱（0~1，低频在前），面板据此画"声音高/中/低"的动画。
        /// 只重画坐标区，不动四周标签，30fps 也不费电。
        /// </summary>
        public void SetLevels(float[] levels, float[] centers)
        {
            if (levels == null || levels.Length < 2) return;
            if (_shown == null || _shown.Length != levels.Length) _shown = new float[levels.Length];
            if (centers != null && centers.Length == levels.Length) _centers = centers;
            for (int i = 0; i < levels.Length; i++)
            {
                float t = levels[i];
                if (float.IsNaN(t) || t < 0f) t = 0f;
                else if (t > 1f) t = 1f;
                _shown[i] += (t - _shown[i]) * 0.45f;      // 再平滑一道，免得看着一跳一跳
            }
            Invalidate(PlotClientRect());
        }

        /// <summary>停止播放：动画归零。</summary>
        public void ClearLevels()
        {
            if (_shown == null) return;
            Array.Clear(_shown, 0, _shown.Length);
            Invalidate(PlotClientRect());
        }

        private Rectangle PlotClientRect()
        {
            float gw = Gw, gh = Gh;
            return new Rectangle(
                (int)Math.Floor((PadLd - 2f) * S),
                (int)Math.Floor((PadTd - 2f) * S),
                Math.Max(1, (int)Math.Ceiling((gw + 4f) * S)),
                Math.Max(1, (int)Math.Ceiling((gh + 4f) * S)));
        }

        /// <summary>实时频谱：贴着坐标区底边的一条绿色渐变轮廓，跟着声音的高/中/低起伏。</summary>
        private void DrawSpectrum(Graphics g, float gx, float gy, float gw, float gh)
        {
            var lv = _shown;
            var cs = _centers;
            if (lv == null || cs == null || lv.Length < 2 || cs.Length != lv.Length) return;
            float mx = 0f;
            for (int i = 0; i < lv.Length; i++) if (lv[i] > mx) mx = lv[i];
            if (mx < 0.012f) return;

            int n = lv.Length;
            float bot = gy + gh, usable = gh * 0.92f;
            var top = new PointF[n + 2];
            top[0] = new PointF(gx, bot);
            for (int i = 0; i < n; i++)
            {
                float x = gx + (float)EqualizerMath.FreqToX(cs[i], Rate) * gw;
                if (x < gx) x = gx; else if (x > gx + gw) x = gx + gw;
                top[i + 1] = new PointF(x, bot - lv[i] * usable);
            }
            top[n + 1] = new PointF(gx + gw, bot);

            var oldClip = g.Clip;
            var clip = Shaper.Round(Rect(gx * S - 1f, gy * S - 2f, gw * S + 3f, gh * S + 6f), 6f * S);
            g.SetClip(clip, CombineMode.Replace);
            using (var path = new GraphicsPath())
            {
                var dev = new PointF[top.Length];
                for (int i = 0; i < top.Length; i++) dev[i] = new PointF(top[i].X * S, top[i].Y * S);
                path.AddLines(dev);
                path.CloseFigure();
                using (var br = new LinearGradientBrush(Rect(gx * S, gy * S, gw * S, gh * S),
                                                        Color.FromArgb(12, T.EqCurve), Color.FromArgb(118, T.EqCurve), 90f))
                    g.FillPath(br, path);
            }
            using (var pen = new Pen(Color.FromArgb(165, T.EqCurve), 2f * S))
            {
                pen.LineJoin = LineJoin.Round;
                var line = new PointF[n];
                for (int i = 0; i < n; i++) line[i] = new PointF(top[i + 1].X * S, top[i + 1].Y * S);
                g.DrawLines(pen, line);
            }
            g.Clip = oldClip;
            oldClip.Dispose();
            clip.Dispose();
        }

        /// <summary>
        /// 面板底纹：一排镜像在 0dB 线两侧的圆角柱，铺满整块深色面板，模拟音频波形；
        /// 整条波形用"右浓左淡"的灰色渐变填充，左边完全淡出。纯装饰，不影响曲线读数。
        /// </summary>
        private void DrawWave(Graphics g, float w, float h)
        {
            const float pad = 3f;
            float bw = w - pad * 2f, bh = h - pad * 2f;
            if (bw < 60f || bh < 60f) return;

            float dw = bw * S, dh = bh * S;
            if (_wave == null || Math.Abs(_waveW - dw) > 0.5f || Math.Abs(_waveH - dh) > 0.5f)
            {
                _wave?.Dispose();
                _wave = BuildWave(pad, pad, bw, bh);
                _waveW = dw;
                _waveH = dh;
            }

            var c = T.EqWave;
            var box = Rect(pad * S, pad * S, dw, dh);
            var clip = Shaper.Round(Rect(1f * S, 1f * S, (w - 2f) * S, (h - 2f) * S), 13f * S);
            var oldClip = g.Clip;
            g.SetClip(clip, CombineMode.Replace);
            using (var br = new LinearGradientBrush(box, c, c, 0f))
            {
                br.InterpolationColors = new ColorBlend(4)
                {
                    Colors = new[]
                    {
                        Color.FromArgb(0, c),
                        Color.FromArgb(c.A * 26 / 100, c),
                        Color.FromArgb(c.A * 74 / 100, c),
                        c
                    },
                    Positions = new[] { 0f, 0.34f, 0.76f, 1f }
                };
                g.FillPath(br, _wave);
            }
            g.Clip = oldClip;
            oldClip.Dispose();
            clip.Dispose();
        }

        private GraphicsPath BuildWave(float bx, float by, float bw, float bh)
        {
            var path = new GraphicsPath();
            const float pitch = 8f, barW = 3.4f;
            float zero = by + bh / 2f;
            float maxH = Math.Max(12f, Math.Min(bh * 0.42f, 122f));
            int n = (int)(bw / pitch);
            if (n < 2) return path;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                // 三个不同周期的正弦叠加 → 平滑、不重复，且每次绘制都一模一样（不用随机数）
                float e = 0.50f
                        + 0.26f * (float)Math.Sin(i * 0.90)
                        + 0.18f * (float)Math.Sin(i * 0.31 + 1.2)
                        + 0.10f * (float)Math.Sin(i * 0.13 + 2.7);
                e = Math.Max(0.07f, Math.Min(1f, e)) * (0.55f + 0.45f * t);   // 右侧更"有劲"
                float hh = maxH * e;
                float x = bx + i * pitch + (pitch - barW) / 2f;
                float y = zero - hh / 2f;
                using (var p = Shaper.Round(Rect(x * S, y * S, barW * S, hh * S),
                                            Math.Min(barW / 2f, hh / 2f) * S))
                    path.AddPath(p, false);
            }
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { _wave?.Dispose(); } catch { } _wave = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float w = Width / S, h = Height / S;
            float rad = 14f;
            FR(g, T.EqBg, 0, 0, w, h, rad);
            DR(g, T.EqBorder, 0, 0, w, h, rad, 1f);

            float gw = Gw, gh = Gh, gx = PadLd, gy = PadTd, zero = ZeroY;

            // ---- 底纹：半透明灰色柱状波形，从右到左渐隐（纯装饰，不参与运算）----
            DrawWave(g, w, h);

            // ---- 实时频谱：跟着正在播放的声音起伏（没声音时不画）----
            DrawSpectrum(g, gx, gy, gw, gh);

            // ---- 网格 ----
            using (var pen = new Pen(T.EqGrid, 1f * S))
            using (var fLbl = Fx.M(10f))
            {
                foreach (var db in new[] { 20, 10, -10, -20 })
                {
                    float y = zero - db / DbRange * (gh / 2);
                    g.DrawLine(pen, gx * S, y * S, (gx + gw) * S, y * S);
                    TX(g, (db > 0 ? "+" : "") + db, fLbl, T.EqMuted, 4, y - 9, gx - 10, 18, ContentAlignment.MiddleRight);
                }
                using (var pen0 = new Pen(Color.FromArgb(90, T.EqMuted), 1f * S))
                    g.DrawLine(pen0, gx * S, zero * S, (gx + gw) * S, zero * S);
                TX(g, "0", fLbl, T.EqMuted, 4, zero - 9, gx - 10, 18, ContentAlignment.MiddleRight);

                for (int i = 0; i < EqBands.AxisFreq.Length; i++)
                {
                    float x = gx + (float)EqualizerMath.FreqToX(EqBands.AxisFreq[i], Rate) * gw;
                    g.DrawLine(pen, x * S, gy * S, x * S, (gy + gh) * S);
                    TX(g, EqBands.AxisLabel[i], fLbl, T.EqMuted, x - 22, gy + gh + 3, 44, 16, ContentAlignment.MiddleCenter);
                }
                TX(g, "Hz", fLbl, T.EqMuted, 4, gy + gh + 3, gx - 10, 16, ContentAlignment.MiddleRight);
            }

            if (Settings == null) return;

            // ---- 响应曲线 ----
            const int N = 220;
            var pts = new PointF[N];
            double fMin = EqBands.MinF, fMax = Math.Min(EqBands.MaxF, Rate * 0.48);
            for (int i = 0; i < N; i++)
            {
                double t = (double)i / (N - 1);
                double f = fMin * Math.Pow(fMax / fMin, t);
                double db = EqualizerMath.CurveDb(Settings, f, Rate);
                db = Math.Max(-DbRange, Math.Min(DbRange, db));
                float x = gx + (float)t * gw;
                float y = zero - (float)(db / DbRange) * (gh / 2);
                pts[i] = new PointF(x * S, y * S);
            }

            var clip = Shaper.Round(Rect(gx * S - 1, gy * S - 2, gw * S + 4, gh * S + 6), 6 * S);
            var oldClip = g.Clip;
            g.SetClip(clip, CombineMode.Replace);

            using (var fill = new GraphicsPath())
            {
                fill.AddLines(pts);
                fill.AddLine(pts[N - 1].X, zero * S, pts[0].X, zero * S);
                fill.CloseFigure();
                using (var fb = new SolidBrush(T.EqFill)) g.FillPath(fb, fill);
            }
            using (var glow = new Pen(T.EqGlow, 7f * S)) { glow.LineJoin = LineJoin.Round; g.DrawLines(glow, pts); }
            using (var cp = new Pen(T.EqCurve, 2.2f * S)) { cp.LineJoin = LineJoin.Round; g.DrawLines(cp, pts); }
            g.Clip = oldClip;
            oldClip.Dispose();
            clip.Dispose();

            // ---- 六个编号点 ----
            for (int i = 0; i < EqBands.Count; i++)
            {
                bool on = BandOn(i);
                float x = BandX(i), y = BandY(i);
                if (y < gy + 2) y = gy + 2;
                if (y > gy + gh - 2) y = gy + gh - 2;
                bool hot = i == _dragBand || i == _hoverBand || i == _selected;
                float r = DotR + (hot ? 1.5f : 0);

                using (var b = new SolidBrush(on ? T.EqDot : T.EqDotOff))
                    g.FillEllipse(b, (x - r) * S, (y - r) * S, r * 2 * S, r * 2 * S);
                using (var p = new Pen(T.EqDotRing, (hot ? 2.2f : 1.5f) * S))
                    g.DrawEllipse(p, (x - r) * S, (y - r) * S, r * 2 * S, r * 2 * S);
                using (var f = Fx.M(10.5f, FontStyle.Bold))
                    TX(g, (i + 1).ToString(), f, on ? T.EqDotText : Color.FromArgb(230, T.EqDotText),
                       x - r, y - r, r * 2, r * 2, ContentAlignment.MiddleCenter);
            }

            // 顶部提示：当前选中段的参数
            if (_selected >= 0 && _selected < EqBands.Count)
            {
                string info = $"Band {_selected + 1}   {EqBands.TypeName((EqFilterType)Settings.EqType[_selected])}   " +
                              $"{FmtFreq(Settings.EqFreq[_selected])} Hz   {Settings.EqGains[_selected]:0.0} dB" +
                              (BandOn(_selected) ? "" : "   （已停用，双击恢复）");
                using (var f = Fx.F(11f))
                    TX(g, info, f, T.EqMuted, gx, 2, gw, 14, ContentAlignment.MiddleRight);
            }
        }

        public static string FmtFreq(float f) =>
            f >= 1000f ? (f / 1000f).ToString(f % 1000f == 0 ? "0.0" : "0.00") + "K" : f.ToString("0");
    }
}
