using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Surround71.Ui
{
    /// <summary>界面缩放：由主窗体在每次重排时下发（DPI × 窗口缩放系数）。</summary>
    internal interface IUiScaled
    {
        void SetUiScale(float s);
    }

    /// <summary>
    /// 所有自绘控件的基类：双缓冲 + 透明背景（让磨砂卡片透上来）+ 设计单位坐标（绘制时乘 S）。
    /// </summary>
    internal abstract class ThemedControl : Control, IUiScaled
    {
        protected float S = 1f;
        protected readonly UiFonts Fx = new UiFonts();

        protected static UiTheme T => UiThemes.Current;

        protected ThemedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public virtual void SetUiScale(float s)
        {
            if (float.IsNaN(s) || float.IsInfinity(s) || s < 0.05f) s = 1f;
            if (Math.Abs(s - S) < 0.0008f) return;
            S = s;
            Fx.Scale = s;          // 字号跟着界面一起缩放（否则字会比方框大）
            Fx.Dispose();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Fx.Dispose();
            base.Dispose(disposing);
        }

        protected static RectangleF Rect(float x, float y, float w, float h) => new RectangleF(x, y, w, h);

        /// <summary>圆角矩形填充，坐标为设计单位。</summary>
        protected void FR(Graphics g, Color c, float x, float y, float w, float h, float rad)
        {
            using (var b = new SolidBrush(c)) Shaper.FillRound(g, b, Rect(x * S, y * S, w * S, h * S), rad * S);
        }

        protected void FR(Graphics g, Brush b, float x, float y, float w, float h, float rad)
        {
            Shaper.FillRound(g, b, Rect(x * S, y * S, w * S, h * S), rad * S);
        }

        protected void DR(Graphics g, Color c, float x, float y, float w, float h, float rad, float penW = 1f)
        {
            using (var p = new Pen(c, penW * S)) Shaper.DrawRound(g, p, Rect(x * S, y * S, w * S, h * S), rad * S);
        }

        protected void TX(Graphics g, string s, Font f, Color c, float x, float y, float w, float h,
                          ContentAlignment al = ContentAlignment.MiddleLeft)
        {
            Shaper.Text(g, s, f, c, Rect(x * S, y * S, w * S, h * S), al);
        }

        protected float TW(Graphics g, string s, Font f) => Shaper.Tw(g, s, f);

        /// <summary>
        /// 统一的"输入 / 选择框"：圆角底 + 一圈描边。
        /// 悬停 / 选中（hot）时换成"柔和的浅绿底 + 一条发丝细边"，不画任何毛边发光。
        /// </summary>
        protected void FieldBox(Graphics g, float x, float y, float w, float h, float rad, bool hot)
        {
            FR(g, hot ? T.AccentSoft : T.Field, x, y, w, h, rad);
            FieldStroke(g, x, y, w, h, rad, hot);
        }

        /// <summary>
        /// 热态（悬停 / 选中）的发丝细边：左实右虚。自带内缩，不会被控件自身边缘裁掉半条线。
        /// </summary>
        protected void HairEdge(Graphics g, Color c, float x, float y, float w, float h, float rad, float penW = 1f)
        {
            float bi = penW / 2f + 0.2f;
            float w2 = Math.Max(0f, w - bi * 2f), h2 = Math.Max(0f, h - bi * 2f);
            if (w2 <= 0.5f || h2 <= 0.5f) return;
            Shaper.HairRound(g, c, Rect((x + bi) * S, (y + bi) * S, w2 * S, h2 * S),
                             Math.Max(1f, rad - bi) * S, penW * S);
        }

        /// <summary>只描边不填底（底色已经画好的场合）。</summary>
        protected void FieldStroke(Graphics g, float x, float y, float w, float h, float rad, bool hot)
        {
            const float bw = 1f;
            float bi = bw / 2f + 0.2f;
            if (hot) { HairEdge(g, T.AccentEdge, x, y, w, h, rad, bw); return; }
            using (var p = new Pen(T.FieldBorder, bw * S))
                Shaper.DrawRound(g, p, Rect((x + bi) * S, (y + bi) * S, (w - bi * 2f) * S, (h - bi * 2f) * S),
                                 Math.Max(1f, rad - bi) * S);
        }
    }

    // =========================================================================
    // 磨砂玻璃卡片
    // =========================================================================
    /// <summary>
    /// 圆角卡片：柔和投影 + 半透明底 + 顶部高光。标题（左，粗体）与副标题（灰，小字）在这里画。
    /// 卡片内的控件由主窗体 Relayout 定位。
    /// </summary>
    internal sealed class GlassCard : ThemedControl
    {
        public string Title = "";
        public string Hint = "";

        public GlassCard()
        {
            SetStyle(ControlStyles.ContainerControl, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float pad = 1f;
            // 卡片主体：四边留白一致。投影不再画在卡片自己身上 —— 往下掉的那一层会被
            // 控件底边裁掉一半，底边留下一条硬线，两个下角就长得和上角不一样；
            // 现在由主窗体在背景层画一圈四面对称的柔光（见 MainForm.DrawCardHalos）。
            var body = Rect(pad, pad, Math.Max(1, Width / S - pad * 2), Math.Max(1, Height / S - pad * 2));
            float rad = 22f;

            // 卡片主体
            FR(g, T.Card, body.X, body.Y, body.Width, body.Height, rad);

            // 玻璃光泽：上下两边同时渐隐、中间最亮的对称版。
            // （原来是从上往下的斜向高光：两个上角被 82% 的白打得发亮、两个下角发暗，
            //  看着就像四个圆角不一样。改成上下对称后四角明暗才一致。）
            using (var p = Shaper.Round(Rect(body.X * S, body.Y * S, body.Width * S, body.Height * S), rad * S))
            using (var lg = new LinearGradientBrush(p.GetBounds(), T.CardSheen, Color.FromArgb(0, T.CardSheen),
                                                    LinearGradientMode.Vertical))
            {
                lg.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, T.CardSheen), T.CardSheen, Color.FromArgb(0, T.CardSheen) },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
                g.FillPath(lg, p);
            }

            // 描边
            DR(g, T.CardBorder, body.X, body.Y, body.Width, body.Height, rad, 1f);

            // 标题 + 副标题
            float x = 18;
            using (var f = Fx.F(15f, FontStyle.Bold))
            {
                TX(g, Title, f, T.Title, x, 11, 260, 22);
                float tw = TW(g, Title, f);
                if (!string.IsNullOrEmpty(Hint))
                    using (var f2 = Fx.F(11.5f))
                        TX(g, Hint, f2, T.Muted, x + tw + 10, 14, 260, 20);
            }
        }
    }

    // =========================================================================
    // 图1 风格的选项卡（图标块 + 小灰标签 + 粗体状态 + 使用中胶囊 + 状态点）
    // =========================================================================
    internal sealed class OptionCard : ThemedControl
    {
        public string Caption = "输出方式";
        public string StatusText = "";
        public GlyphKind Icon = GlyphKind.Headphone;
        public bool Active;
        public bool Hover;

        public OptionCard()
        {
            Cursor = Cursors.Hand;
        }

        public void SetActive(bool on)
        {
            if (Active == on) return;
            Active = on;
            Invalidate();
        }

        public event EventHandler Clicked;

        private void RaiseClick() => Clicked?.Invoke(this, EventArgs.Empty);

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hover = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hover = false; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); RaiseClick(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float w = Width / S, h = Height / S;
            float rad = Math.Min(26f, h * 0.38f);

            // 圆角 + 描边统一走 FieldStroke：悬停/选中时是均匀的一圈绿光，不会上下深浅不一
            FR(g, T.OptionBg, 0, 0, w, h, rad);
            if (Active || Hover) FR(g, T.AccentSoft, 0, 0, w, h, rad);
            FieldStroke(g, 0, 0, w, h, rad, Active || Hover);

            // 图标底块
            float tile = Math.Min(42, h - 22);
            float ty = (h - tile) / 2;
            FR(g, Active ? T.IconTileOn : T.IconTile, 12, ty, tile, tile, 12);
            Glyphs.Draw(g, Icon, Rect(12 * S + (tile * S - tile * S * 0.5f) / 2, (ty + tile * 0.25f) * S,
                                       tile * 0.5f * S, tile * 0.5f * S), Active ? T.Accent : T.Muted);

            // 文案
            float tx = 12 + tile + 12;
            using (var f1 = Fx.F(11.5f))
                TX(g, Caption, f1, T.Muted, tx, h / 2 - 20, 150, 18);
            using (var f2 = Fx.F(14f, FontStyle.Bold))
                TX(g, StatusText, f2, Active ? T.Title : T.Text, tx, h / 2 + 2, 170, 20);

            // 使用中胶囊 + 状态点
            float dot = 11;
            float dx = w - 14 - dot;
            float dy = (h - dot) / 2;
            if (Active)
            {
                string tag = "使用中";
                using (var fb = Fx.F(11f, FontStyle.Bold))
                {
                    float pw = TW(g, tag, fb) + 20;
                    FR(g, T.PillBg, dx - 12 - pw, (h - 22) / 2, pw, 22, 11);
                    TX(g, tag, fb, T.Accent, dx - 12 - pw, (h - 22) / 2, pw, 22, ContentAlignment.MiddleCenter);
                }
            }
            using (var b = new SolidBrush(Active ? T.Accent : T.DotOff))
                g.FillEllipse(b, dx * S, dy * S, dot * S, dot * S);
        }
    }

    // =========================================================================
    // 胶囊按钮（主按钮 / 幽灵按钮 / 开关式按钮）
    // =========================================================================
    internal sealed class PillButton : ThemedControl
    {
        public GlyphKind Icon = GlyphKind.Gear;
        public bool ShowIcon = true;
        public bool Primary;
        /// <summary>开关式：按下时高亮（例如"延迟更低"）。</summary>
        public bool Toggle;
        public bool Checked;
        public bool Hover;
        public bool Pressed;
        /// <summary>主按钮可用配色覆盖（运行中变红）。</summary>
        public Color? AccentOverride;

        // 主按钮那层呼吸光：0 = 最暗，1 = 最亮，只重绘按钮自己，几乎不占 CPU
        private readonly System.Windows.Forms.Timer _pulse = new System.Windows.Forms.Timer { Interval = 40 };
        private double _phase;
        private float _glow = 1f;
        private bool _pulsing;

        // 呼吸光预渲染缓存：整团柔光先画成一张位图，呼吸时只改整体透明度。
        // 否则每帧要重画几十层椭圆，GDI+ 逐层填充很贵，音频程序不能这么烧 CPU。
        private Bitmap _glowBmp;
        private Color _glowBmpColor;
        private int _glowBmpW, _glowBmpH;

        private string _text = "";

        public string LabelText
        {
            get => _text;
            set { _text = value ?? ""; Invalidate(); }
        }

        public event EventHandler Clicked;

        public PillButton()
        {
            Cursor = Cursors.Hand;
            _pulse.Tick += (s, e) =>
            {
                _phase += 0.055;
                if (_phase > Math.PI * 2) _phase -= Math.PI * 2;
                _glow = 0.45f + 0.55f * (float)((Math.Sin(_phase - Math.PI / 2) + 1) / 2);
                Invalidate();
            };
        }

        /// <summary>呼吸光只在"主按钮 + 真的显示着"的时候跑。</summary>
        private void SyncPulse()
        {
            bool want = Primary && IsHandleCreated && Visible;
            if (want == _pulsing) return;
            _pulsing = want;
            if (want) _pulse.Start();
            else _pulse.Stop();
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SyncPulse(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); SyncPulse(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _pulse.Stop(); _pulse.Dispose(); } catch { }
                try { _glowBmp?.Dispose(); } catch { }
                _glowBmp = null;
            }
            base.Dispose(disposing);
        }

        public void SetChecked(bool on)
        {
            if (Checked == on) return;
            Checked = on;
            Invalidate();
        }

        public void SetPrimary(bool p, string text, GlyphKind icon)
        {
            Primary = p; _text = text ?? ""; Icon = icon; ShowIcon = true; SyncPulse(); Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hover = false; Pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Pressed = false; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); Clicked?.Invoke(this, EventArgs.Empty); }

        public override void SetUiScale(float s)
        {
            if (Math.Abs(s - S) < 0.0008f) return;
            base.SetUiScale(s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float w = Width / S, h = Height / S;
            float rad = h / 2f;
            bool enabled = Enabled;
            // 不可用时统一走"灰底 + 灰字"，不画呼吸光，一眼就能看出点不动
            Color accent = !enabled ? Shaper.Mix(T.OptionBg, T.Muted, 0.22f) : (AccentOverride ?? T.Accent);

            bool lit = enabled && (Primary || (Toggle && Checked));
            if (lit)
            {
                // 底色：整颗胶囊
                FR(g, accent, 0, 0, w, h, rad);
                // 光效：以按钮水平中线为中心的绿色椭圆，中间实、往上下两边渐淡，
                // 到椭圆边界正好完全透明（所以边界处是"直接消失"，不会露出一圈灰边）。
                DrawLitGlow(g, accent, w, h, rad);
            }
            else
            {
                // 冷态也走内缩描边：贴着控件边缘画会被裁掉外半圈，看起来像"没有边"
                if (Hover)
                {
                    FR(g, T.AccentSoft, 0, 0, w, h, rad);
                    HairEdge(g, T.AccentEdge, 0, 0, w, h, rad, 1f);
                }
                else
                {
                    FR(g, T.OptionBg, 0, 0, w, h, rad);
                    FieldStroke(g, 0, 0, w, h, rad, false);
                }
            }
            if (Pressed && enabled) FR(g, Color.FromArgb(30, 0, 0, 0), 0, 0, w, h, rad);

            Color fg = !enabled ? T.Muted
                     : (lit ? T.OnAccent : T.Text);
            using (var f = Fx.F(Primary ? 14f : 13.5f, FontStyle.Bold))
            {
                float tw = TW(g, _text, f);
                float iconW = ShowIcon ? 20 : 0;
                float total = iconW + (ShowIcon ? 8 : 0) + tw;
                float x0 = Math.Max(14, (w - total) / 2);
                if (ShowIcon)
                {
                    Glyphs.Draw(g, Icon, Rect(x0 * S, (h / 2 - 10) * S, 20 * S, 20 * S), fg);
                    x0 += iconW + 8;
                }
                TX(g, _text, f, fg, x0, 0, tw + 4, h, ContentAlignment.MiddleLeft);
            }
        }

        /// <summary>
        /// 主按钮中央那团椭圆柔光：以水平中线为轴、上下对称，中间实、往两边渐淡，
        /// 到按钮边界正好归零（所以边界处是"直接消失"，不会留一圈平色的硬边）。
        /// 主按钮带缓慢呼吸（_glow 0.45→1）；开关式按钮只给一层静态微光。
        /// </summary>
        private void DrawLitGlow(Graphics g, Color accent, float w, float h, float rad)
        {
            float k = Primary ? _glow : 0.8f;
            if (k <= 0.01f) return;
            int pw = Math.Max(1, (int)Math.Round(w * S));
            int ph = Math.Max(1, (int)Math.Round(h * S));
            if (_glowBmp == null || _glowBmpW != pw || _glowBmpH != ph || _glowBmpColor != accent)
            {
                try { _glowBmp?.Dispose(); } catch { }
                _glowBmp = BuildGlow(accent, pw, ph, rad * S);
                _glowBmpColor = accent;
                _glowBmpW = pw;
                _glowBmpH = ph;
            }
            // 呼吸：只把整张光晕位图的透明度从 42% 拉到 100%，亮度变化最自然，也最省
            float a = 0.42f + 0.58f * k;
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix { Matrix33 = a };
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(_glowBmp, new Rectangle(0, 0, pw, ph), 0, 0, pw, ph, GraphicsUnit.Pixel, ia);
            }
        }

        /// <summary>把柔光画进一张透明位图（只在这个尺寸/配色第一次出现时画一次）。</summary>
        private Bitmap BuildGlow(Color accent, int pw, int ph, float rad)
        {
            // 中心色不能一律按固定比例混白：深色主题的主色本身就亮（45,212,152），
            // 一混白整颗按钮就发灰发粉；浅色主题的主色偏深，混少了又几乎看不出光。
            // 所以按主色亮度分两档：亮主色少混白、轻提亮；深主色多混白、做出"打光"的感觉。
            float lum = 0.299f * accent.R + 0.587f * accent.G + 0.114f * accent.B;
            bool bright = lum >= 140f;
            float t = bright ? 0.34f : 0.55f;
            int alpha = bright ? 214 : 184;
            Color inner = Shaper.Mix(accent, Color.White, t);

            var bmp = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var pill = Shaper.Round(new RectangleF(0, 0, pw, ph), rad))
                {
                    g.SetClip(pill, CombineMode.Intersect);
                    Shaper.GlowSoft(g, new RectangleF(0, 0, pw, ph), inner, alpha, 0.42f, 0.34f, 30);
                }
            }
            return bmp;
        }
    }

    // =========================================================================
    // 开关
    // =========================================================================
    internal sealed class ToggleSwitch : ThemedControl
    {
        private bool _on;
        public bool Checked
        {
            get => _on;
            set { if (_on == value) return; _on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }
        public bool Enabled2 = true;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            Cursor = Cursors.Hand;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Enabled2 = Enabled;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Width / S, h = Height / S;
            float rad = h / 2;
            Color track = !Enabled2 ? Color.FromArgb(90, T.DotOff) : (_on ? T.Accent : T.Track);
            FR(g, track, 0, 0, w, h, rad);
            float k = h - 6;
            float kx = _on ? w - k - 3 : 3;
            using (var b = new SolidBrush(Enabled2 ? T.Knob : Color.FromArgb(150, T.Knob)))
                g.FillEllipse(b, kx * S, 3 * S, k * S, k * S);
        }
    }

    // =========================================================================
    // 分段控件（两个以上选项互斥，比单选按钮现代）
    // =========================================================================
    internal sealed class SegmentedControl : ThemedControl
    {
        private string[] _items = new string[0];
        private int _sel;

        public event EventHandler SelectedIndexChanged;

        public string[] Items
        {
            get => _items;
            set { _items = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get => _sel;
            set
            {
                int v = Math.Max(0, Math.Min(_items.Length - 1, value));
                if (v == _sel) return;
                _sel = v;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public SegmentedControl()
        {
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_items.Length == 0) return;
            float w = Width / S;
            int idx = (int)(e.X / S / (w / _items.Length));
            SelectedIndex = Math.Max(0, Math.Min(_items.Length - 1, idx));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float w = Width / S, h = Height / S;
            FR(g, T.Field, 0, 0, w, h, h / 2);
            FieldStroke(g, 0, 0, w, h, h / 2, false);
            if (_items.Length == 0) return;
            float seg = w / _items.Length;
            float pad = 3;
            FR(g, T.AccentSoft, _sel * seg + pad, pad, seg - pad * 2, h - pad * 2, (h - pad * 2) / 2);
            HairEdge(g, T.AccentEdge, _sel * seg + pad, pad, seg - pad * 2, h - pad * 2, (h - pad * 2) / 2, 1f);
            using (var f = Fx.F(12.5f, FontStyle.Bold))
                for (int i = 0; i < _items.Length; i++)
                    TX(g, _items[i], f, i == _sel ? T.Accent : T.Muted, i * seg, 0, seg, h, ContentAlignment.MiddleCenter);
        }
    }

    // =========================================================================
    // 主题文字（代替 Label，颜色跟着主题走）
    // =========================================================================
    internal sealed class PlainText : ThemedControl
    {
        public float FontPx = 13f;
        public FontStyle Style = FontStyle.Regular;
        public bool Mono = false;
        public Color? ColorOverride = null;
        public ContentAlignment Align = ContentAlignment.MiddleLeft;

        private string _text = "";

        public string LabelText
        {
            get => _text;
            set { if (_text == value) return; _text = value ?? ""; Invalidate(); }
        }

        public PlainText() { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var f = Mono ? Fx.M(FontPx, Style) : Fx.F(FontPx, Style))
                TX(g, _text, f, ColorOverride ?? T.Text, 0, 0, Width / S, Height / S, Align);
        }
    }
}
