using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Surround71.Ui
{
    /// <summary>主题类型：只有白色一套（深色主题已去除）。</summary>
    internal enum ThemeKind { Light }

    /// <summary>
    /// 一套界面配色。所有控件在绘制时都从这里取色。
    /// 颜色按用途分组：窗体底、卡片、文字、主色、控件、均衡器面板、声场靶。
    /// </summary>
    internal sealed class UiTheme
    {
        public ThemeKind Kind;

        // 窗口底：渐变 + 柔光光斑
        public Color Bg1, Bg2;
        public (float x, float y, float w, float h, Color c)[] Blobs;

        // 卡片
        public Color Card, CardBorder, CardSheen, Shadow;

        // 文字
        public Color Title, Text, Muted;

        // 主色（品牌绿）
        public Color Accent, AccentSoft, AccentEdge, OnAccent, Danger;

        // 选项卡片 / 胶囊 / 开关
        public Color OptionBg, OptionBorder, IconTile, IconTileOn, PillBg, DotOff;

        // 输入控件
        public Color Field, FieldBorder, Track, TrackOn, Knob;

        // 均衡器面板（浅色薄荷底，与白色卡片同色系）
        public Color EqBg, EqBorder, EqGrid, EqMuted, EqCurve, EqGlow, EqFill, EqDot, EqDotOff, EqDotRing, EqDotText;
        /// <summary>曲线面板底纹：半透明灰色波形，从右到左渐隐。</summary>
        public Color EqWave;

        // 7.1 声场靶
        public Color TargetCore, TargetEdge, TargetRing, HeadFill, HeadRing, DirMark, Spk, SpkLfe, SpkRing, SpkText;

        // 弹出菜单
        public Color MenuBg, MenuBorder, MenuHover;

        public static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);
        public static Color A(int a, int r, int g, int b) => Color.FromArgb(a, r, g, b);
    }

    /// <summary>主题表：只有白色一套（深色主题已去除，EQ 面板也改为浅色底，不再有突兀的黑块）。</summary>
    internal static class UiThemes
    {
        public static readonly UiTheme Light = new UiTheme
        {
            Kind = ThemeKind.Light,
            Bg1 = UiTheme.C(247, 250, 249),
            Bg2 = UiTheme.C(232, 240, 236),
            Blobs = new[]
            {
                (-0.16f, -0.28f, 0.62f, 0.72f, UiTheme.A(150, 255, 255, 255)),
                (0.56f, -0.18f, 0.66f, 0.68f, UiTheme.A(76, 120, 226, 190)),
                (-0.12f, 0.56f, 0.60f, 0.78f, UiTheme.A(70, 130, 219, 187)),
                (0.58f, 0.60f, 0.62f, 0.62f, UiTheme.A(120, 255, 255, 255)),
                (0.22f, 0.10f, 0.46f, 0.52f, UiTheme.A(40, 214, 246, 230)),
            },
            Card = UiTheme.A(244, 255, 255, 255),
            CardBorder = UiTheme.A(160, 255, 255, 255),
            CardSheen = UiTheme.A(96, 255, 255, 255),
            Shadow = UiTheme.A(16, 92, 112, 102),
            Title = UiTheme.C(26, 34, 30),
            Text = UiTheme.C(52, 64, 58),
            Muted = UiTheme.C(136, 151, 144),
            Accent = UiTheme.C(16, 185, 129),
            AccentSoft = UiTheme.C(226, 247, 238),
            AccentEdge = UiTheme.C(197, 224, 211),
            OnAccent = Color.White,
            Danger = UiTheme.C(226, 90, 74),
            OptionBg = UiTheme.A(240, 255, 255, 255),
            OptionBorder = UiTheme.C(224, 234, 229),
            IconTile = UiTheme.C(240, 245, 243),
            IconTileOn = UiTheme.C(255, 255, 255),
            PillBg = UiTheme.C(210, 241, 228),
            DotOff = UiTheme.C(212, 220, 216),
            Field = UiTheme.C(246, 249, 248),
            FieldBorder = UiTheme.C(224, 234, 229),
            Track = UiTheme.C(228, 236, 232),
            TrackOn = UiTheme.C(16, 185, 129),
            Knob = Color.White,
            // 均衡器面板：浅色薄荷底（不再是黑块），曲线/网格/点全部按浅色底重新配
            EqBg = UiTheme.C(233, 243, 238),
            EqBorder = UiTheme.C(203, 224, 215),
            EqGrid = UiTheme.C(198, 219, 209),
            EqMuted = UiTheme.C(104, 126, 116),
            EqCurve = UiTheme.C(13, 148, 104),
            EqGlow = UiTheme.A(40, 13, 148, 104),
            EqFill = UiTheme.A(30, 13, 148, 104),
            EqDot = UiTheme.C(13, 148, 104),
            EqDotOff = UiTheme.C(176, 192, 184),
            EqDotRing = UiTheme.C(255, 255, 255),
            EqDotText = Color.White,
            EqWave = UiTheme.A(46, 96, 138, 122),
            TargetCore = UiTheme.A(228, 32, 176, 124),
            TargetEdge = UiTheme.A(0, 32, 176, 124),
            TargetRing = UiTheme.A(190, 255, 255, 255),
            HeadFill = UiTheme.A(245, 255, 255, 255),
            HeadRing = UiTheme.C(22, 150, 104),
            DirMark = UiTheme.C(30, 122, 92),
            Spk = UiTheme.C(15, 150, 106),
            SpkLfe = UiTheme.C(12, 112, 80),
            SpkRing = UiTheme.A(235, 255, 255, 255),
            SpkText = Color.White,
            MenuBg = Color.White,
            MenuBorder = UiTheme.C(224, 234, 229),
            MenuHover = UiTheme.C(240, 242, 244),
        };

        /// <summary>当前主题：只有白色一套，永远返回它。</summary>
        public static UiTheme Current => Light;
    }

    /// <summary>
    /// 字体工厂：统一用"像素"单位，设计稿里的字号就是像素值，再乘界面缩放系数，
    /// 这样 175% DPI 下不会像 pt 那样被系统再放大一次导致文字溢出。
    /// 每次调用返回一个**新的字体对象**，由调用方用完即弃（绘制代码都用 using 包着）；
    /// 只有字体族长期缓存 —— 踩过的坑：字体对象一旦被 using 释放又被复用，
    /// GDI+ 会一直报 "Parameter is not valid"，整个界面画出一堆红叉。
    /// </summary>
    internal sealed class UiFonts : IDisposable
    {
        public const string Ui = "Microsoft YaHei UI";
        public const string Mono = "Consolas";

        /// <summary>长期持有的字体族（不会被调用方释放，也就不怕被垃圾回收）。</summary>
        private static readonly Dictionary<string, FontFamily> _families = new Dictionary<string, FontFamily>();

        private static FontFamily Family(string name, FontFamily fallback)
        {
            lock (_families)
            {
                if (_families.TryGetValue(name, out var f) && f != null) return f;
                try { f = new FontFamily(name); } catch { f = fallback; }
                _families[name] = f;
                return f;
            }
        }

        /// <summary>
        /// 界面缩放系数（DPI × 窗口缩放），由控件的 SetUiScale 下发。
        /// 字号必须跟着一起缩放，否则窗口越小方框越小、字却不动，看着就像"字符把方框撑爆了"。
        /// </summary>
        public float Scale = 1f;

        public Font Get(string family, float px, FontStyle style)
        {
            FontFamily fam = string.Equals(family, Mono, StringComparison.OrdinalIgnoreCase)
                ? Family(Mono, FontFamily.GenericMonospace)
                : Family(family, FontFamily.GenericSansSerif);
            float sz = Math.Max(6f, px * (Scale <= 0.05f ? 1f : Scale));
            try { return new Font(fam, sz, style, GraphicsUnit.Pixel); }
            catch { return new Font(fam, Math.Max(4f, sz), style, GraphicsUnit.Pixel); }
        }

        public Font F(float px, FontStyle style = FontStyle.Regular) => Get(Ui, px, style);

        public Font M(float px, FontStyle style = FontStyle.Regular) => Get(Mono, px, style);

        public void Dispose() { }
    }

    /// <summary>界面上的小图标（全部矢量绘制，不依赖字体图标，缩放不糊）。</summary>
    internal enum GlyphKind
    {
        Headphone, Speaker, Cable, Loop, Play, Folder, Wave, Gear, Reset,
        Sun, Moon, Power, Stop, Check, TV, Speaker7
    }

    internal static class Glyphs
    {
        /// <summary>在矩形内画一个图标，颜色与线宽自动随尺寸缩放。</summary>
        public static void Draw(Graphics g, GlyphKind kind, RectangleF r, Color color)
        {
            float size = Math.Min(r.Width, r.Height);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, h = size / 2f;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, Math.Max(1.4f, size * 0.085f)))
            using (var br = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case GlyphKind.Headphone:
                        g.DrawArc(pen, cx - h * 0.80f, cy - h * 0.86f, h * 1.60f, h * 1.64f, 180, 180);
                        FillRound(g, br, cx - h * 0.88f, cy - h * 0.12f, h * 0.44f, h * 0.98f, h * 0.22f);
                        FillRound(g, br, cx + h * 0.44f, cy - h * 0.12f, h * 0.44f, h * 0.98f, h * 0.22f);
                        break;
                    case GlyphKind.Speaker:
                    case GlyphKind.Speaker7:
                        using (var p = new GraphicsPath())
                        {
                            p.AddPolygon(new[]
                            {
                                new PointF(cx - h * 0.86f, cy - h * 0.30f),
                                new PointF(cx - h * 0.30f, cy - h * 0.30f),
                                new PointF(cx + h * 0.22f, cy - h * 0.80f),
                                new PointF(cx + h * 0.22f, cy + h * 0.80f),
                                new PointF(cx - h * 0.30f, cy + h * 0.30f),
                                new PointF(cx - h * 0.86f, cy + h * 0.30f),
                            });
                            g.FillPath(br, p);
                        }
                        g.DrawArc(pen, cx + h * 0.08f, cy - h * 0.56f, h * 0.72f, h * 1.12f, -62, 124);
                        break;
                    case GlyphKind.Cable:
                        g.DrawArc(pen, cx - h * 0.98f, cy - h * 0.44f, h * 0.98f, h * 0.88f, 42, 258);
                        g.DrawArc(pen, cx + h * 0.02f, cy - h * 0.44f, h * 0.98f, h * 0.88f, 222, 258);
                        g.DrawLine(pen, cx - h * 0.34f, cy, cx + h * 0.34f, cy);
                        break;
                    case GlyphKind.Loop:
                        g.DrawArc(pen, cx - h * 0.76f, cy - h * 0.76f, h * 1.52f, h * 1.52f, 28, 284);
                        g.DrawLine(pen, cx + h * 0.40f, cy - h * 0.84f, cx + h * 0.74f, cy - h * 0.50f);
                        g.DrawLine(pen, cx + h * 0.74f, cy - h * 0.50f, cx + h * 0.28f, cy - h * 0.44f);
                        break;
                    case GlyphKind.Play:
                        using (var p = new GraphicsPath())
                        {
                            p.AddPolygon(new[]
                            {
                                new PointF(cx - h * 0.40f, cy - h * 0.62f),
                                new PointF(cx + h * 0.62f, cy),
                                new PointF(cx - h * 0.40f, cy + h * 0.62f),
                            });
                            g.FillPath(br, p);
                        }
                        break;
                    case GlyphKind.Folder:
                        using (var p = new GraphicsPath())
                        {
                            p.AddPolygon(new[]
                            {
                                new PointF(cx - h * 0.84f, cy - h * 0.52f),
                                new PointF(cx - h * 0.18f, cy - h * 0.52f),
                                new PointF(cx + h * 0.02f, cy - h * 0.22f),
                                new PointF(cx + h * 0.84f, cy - h * 0.22f),
                                new PointF(cx + h * 0.84f, cy + h * 0.56f),
                                new PointF(cx - h * 0.84f, cy + h * 0.56f),
                            });
                            g.DrawPath(pen, p);
                        }
                        break;
                    case GlyphKind.Wave:
                        for (int i = -1; i <= 1; i++)
                        {
                            float hh = h * (0.34f + 0.26f * (1 - Math.Abs(i)));
                            g.DrawLine(pen, cx + i * h * 0.36f, cy - hh, cx + i * h * 0.36f, cy + hh);
                        }
                        break;
                    case GlyphKind.Gear:
                        g.DrawEllipse(pen, cx - h * 0.32f, cy - h * 0.32f, h * 0.64f, h * 0.64f);
                        for (int a = 0; a < 360; a += 45)
                        {
                            double rad = a * Math.PI / 180.0;
                            g.DrawLine(pen,
                                cx + (float)Math.Cos(rad) * h * 0.54f, cy + (float)Math.Sin(rad) * h * 0.54f,
                                cx + (float)Math.Cos(rad) * h * 0.88f, cy + (float)Math.Sin(rad) * h * 0.88f);
                        }
                        break;
                    case GlyphKind.Reset:
                    {
                        // 恢复默认：一整圈圆弧（缺口在右上）+ 一个实心箭头，小尺寸下也认得出来
                        float rr = h * 0.70f;
                        const float endDeg = 302f;
                        g.DrawArc(pen, cx - rr, cy - rr, rr * 2f, rr * 2f, endDeg, 300f);
                        double a = endDeg * Math.PI / 180.0;
                        float px = cx + (float)Math.Cos(a) * rr, py = cy + (float)Math.Sin(a) * rr;
                        float tx = -(float)Math.Sin(a), ty = (float)Math.Cos(a);   // 顺时针切线
                        float nx = (float)Math.Cos(a), ny = (float)Math.Sin(a);    // 半径方向
                        float len = h * 0.46f, wid = h * 0.34f;
                        using (var p = new GraphicsPath())
                        {
                            p.AddPolygon(new[]
                            {
                                new PointF(px + tx * len + nx * wid * 0.5f, py + ty * len + ny * wid * 0.5f),
                                new PointF(px + tx * len - nx * wid * 0.5f, py + ty * len - ny * wid * 0.5f),
                                new PointF(px - tx * wid * 0.85f, py - ty * wid * 0.85f),
                            });
                            g.FillPath(br, p);
                        }
                        break;
                    }
                    case GlyphKind.Sun:
                        g.DrawEllipse(pen, cx - h * 0.34f, cy - h * 0.34f, h * 0.68f, h * 0.68f);
                        for (int a = 0; a < 360; a += 45)
                        {
                            double rad = a * Math.PI / 180.0;
                            g.DrawLine(pen,
                                cx + (float)Math.Cos(rad) * h * 0.58f, cy + (float)Math.Sin(rad) * h * 0.58f,
                                cx + (float)Math.Cos(rad) * h * 0.92f, cy + (float)Math.Sin(rad) * h * 0.92f);
                        }
                        break;
                    case GlyphKind.Moon:
                        g.DrawArc(pen, cx - h * 0.62f, cy - h * 0.62f, h * 1.24f, h * 1.24f, 60, 240);
                        g.DrawArc(pen, cx - h * 0.20f, cy - h * 0.62f, h * 1.10f, h * 1.24f, 150, 120);
                        break;
                    case GlyphKind.Power:
                        g.DrawArc(pen, cx - h * 0.70f, cy - h * 0.60f, h * 1.40f, h * 1.40f, 300, 300);
                        g.DrawLine(pen, cx, cy - h * 0.94f, cx, cy - h * 0.18f);
                        break;
                    case GlyphKind.Stop:
                        FillRound(g, br, cx - h * 0.58f, cy - h * 0.58f, h * 1.16f, h * 1.16f, h * 0.18f);
                        break;
                    case GlyphKind.Check:
                        g.DrawLines(pen, new[]
                        {
                            new PointF(cx - h * 0.60f, cy + h * 0.02f),
                            new PointF(cx - h * 0.16f, cy + h * 0.48f),
                            new PointF(cx + h * 0.62f, cy - h * 0.46f),
                        });
                        break;
                    case GlyphKind.TV:
                        FillRound(g, br, cx - h * 0.86f, cy - h * 0.66f, h * 1.72f, h * 1.16f, h * 0.16f);
                        g.DrawLine(pen, cx - h * 0.34f, cy + h * 0.72f, cx + h * 0.34f, cy + h * 0.72f);
                        break;
                }
            }
            g.SmoothingMode = old;
        }

        private static void FillRound(Graphics g, Brush b, float x, float y, float w, float h, float r)
        {
            using (var p = Shaper.Round(new RectangleF(x, y, w, h), r)) g.FillPath(b, p);
        }
    }

    /// <summary>圆角矩形路径工具。</summary>
    internal static class Shaper
    {
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Max(0.1f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f) * 2f);
            if (r.Width <= 1 || r.Height <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Brush b, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Pen pen, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) g.DrawPath(pen, p);
        }

        /// <summary>
        /// 热态发丝细边：左边是实色，往右渐变到完全透明（右边看起来就是"消失"）。
        /// 点击 / 悬停 / 选中 的高亮统一用这条边，比均匀描一圈更轻、更有指向性。
        /// </summary>
        public static void HairRound(Graphics g, Color c, RectangleF r, float rad, float penW)
        {
            if (r.Width <= 1.4f || r.Height <= 1.4f || penW <= 0.01f) return;
            Color clear = Color.FromArgb(0, c);
            using (var lg = new LinearGradientBrush(r, c, clear, LinearGradientMode.Horizontal))
            {
                lg.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { c, c, clear },
                    Positions = new[] { 0f, 0.34f, 1f },
                };
                using (var p = new Pen(lg, penW)) DrawRound(g, p, r, rad);
            }
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var b = new SolidBrush(c)) FillRound(g, b, r, rad);
        }

        /// <summary>按比例混两种颜色（t=0 → a，t=1 → b）。</summary>
        public static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// 椭圆形柔光：以椭圆中心为轴，中心一段是实色（coreX/coreY 是"实心核心"的比例），
        /// 往外一路渐淡，到椭圆边界正好完全透明——所以绝不会在边界上被切出一条硬边。
        /// </summary>
        public static void GlowEllipse(Graphics g, RectangleF ell, Color c, int alpha, float coreX, float coreY)
        {
            if (ell.Width <= 1f || ell.Height <= 1f || alpha <= 0) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(ell);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterPoint = new PointF(ell.X + ell.Width / 2f, ell.Y + ell.Height / 2f);
                    pgb.CenterColor = Color.FromArgb(Math.Min(255, alpha), c);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, c) };
                    pgb.FocusScales = new PointF(
                        Math.Max(0f, Math.Min(0.95f, coreX)),
                        Math.Max(0f, Math.Min(0.95f, coreY)));
                    g.FillPath(pgb, path);
                }
            }
        }

        /// <summary>
        /// 胶囊形柔光：渐变的路径就是按钮自己的圆角轮廓，因此光晕在按钮边界上正好衰减到全透明
        /// —— 边界处"直接消失"，不会留一圈平色的硬边，也不会出现椭圆截在按钮中间的空档。
        /// FocusScales 决定中间那段"实心"核心的大小；核心是横放的椭圆，以按钮水平中线为轴上下对称。
        /// </summary>
        public static void GlowPill(Graphics g, RectangleF rect, float rad, Color c, int alpha, float coreX, float coreY)
        {
            if (rect.Width <= 1f || rect.Height <= 1f || alpha <= 0) return;
            using (var path = Round(rect, rad))
            using (var pgb = new PathGradientBrush(path))
            {
                pgb.CenterPoint = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
                pgb.CenterColor = Color.FromArgb(Math.Min(255, alpha), c);
                pgb.SurroundColors = new[] { Color.FromArgb(0, c) };
                pgb.FocusScales = new PointF(
                    Math.Max(0f, Math.Min(0.95f, coreX)),
                    Math.Max(0f, Math.Min(0.95f, coreY)));
                g.FillPath(pgb, path);
            }
        }

        /// <summary>
        /// 椭圆柔光（分层叠加版）：从"整个按钮"那么大的椭圆开始，一层层往中间收，
        /// 每层都用很小的 alpha 叠加，所以中心最亮、往外一路衰减、到最外层（正好是按钮边界）归零。
        /// 比 PathGradientBrush 的渐变更平滑，也能精确控制"实心核心"占多大。
        /// </summary>
        public static void GlowSoft(Graphics g, RectangleF b, Color c, int alpha, float coreX, float coreY, int steps = 26)
        {
            if (alpha <= 0 || b.Width <= 2f || b.Height <= 2f) return;
            steps = Math.Max(6, steps);
            coreX = Math.Max(0.02f, Math.Min(0.98f, coreX));
            coreY = Math.Max(0.02f, Math.Min(0.98f, coreY));
            // 单层 alpha：让 steps 层叠起来正好等于目标 alpha
            int each = (int)Math.Round(255.0 * (1.0 - Math.Pow(1.0 - Math.Min(250, alpha) / 255.0, 1.0 / steps)));
            if (each <= 0) return;
            using (var br = new SolidBrush(Color.FromArgb(each, c)))
            {
                for (int i = steps; i >= 1; i--)
                {
                    float t = i / (float)steps;                       // 1 = 最外层（贴按钮边界）
                    float sx = coreX + (1f - coreX) * t;
                    float sy = coreY + (1f - coreY) * t;
                    float rx = b.Width / 2f * sx, ry = b.Height / 2f * sy;
                    g.FillEllipse(br, b.X + b.Width / 2f - rx, b.Y + b.Height / 2f - ry, rx * 2f, ry * 2f);
                }
            }
        }

        /// <summary>文字（左对齐，垂直居中于 rect）。</summary>
        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r,
                                ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            if (string.IsNullOrEmpty(s) || f == null) return;
            // 绘制区域被完全裁掉时 GDI+ 会抛 "Parameter is not valid"，这里先挡掉
            RectangleF vb = g.VisibleClipBounds;
            if (vb.Width <= 0.01f || vb.Height <= 0.01f) return;
            if (!IsFinite(r.X) || !IsFinite(r.Y) || !IsFinite(r.Width) || !IsFinite(r.Height)) return;
            if (r.Width < 0f) r.Width = 0f;
            if (r.Height < 0f) r.Height = 0f;
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.Alignment = align == ContentAlignment.MiddleRight || align == ContentAlignment.TopRight || align == ContentAlignment.BottomRight
                    ? StringAlignment.Far
                    : align == ContentAlignment.MiddleCenter || align == ContentAlignment.TopCenter || align == ContentAlignment.BottomCenter
                        ? StringAlignment.Center : StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                try { g.DrawString(s, f, b, r, sf); }
                catch { }   // 万一个别情况下 GDI+ 画不了字，也不能让整个界面报红叉
            }
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        public static float Tw(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s) || f == null) return 0f;
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                return g.MeasureString(s, f, 10000, sf).Width;
            }
        }
    }
}
