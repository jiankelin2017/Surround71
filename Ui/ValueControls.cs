using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Surround71.Ui
{
    // =========================================================================
    // 横向滑块：标签 + 细轨道 + 圆形手柄 + 数值胶囊（拖动即时生效）
    // =========================================================================
    internal sealed class ModernSlider : ThemedControl
    {
        public string Label = "";
        public int Min;
        public int Max = 100;
        public int Step = 1;
        public float LabelWidth = 118f;
        /// <summary>数值显示格式（把控件内部整数换算成显示文本）。</summary>
        public Func<int, string> Format = v => v.ToString();

        private int _value;
        private bool _drag;
        private bool _hover;

        public event Action<int> ValueChanged;

        public ModernSlider()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Math.Max(Min, Math.Min(Max, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                ValueChanged?.Invoke(v);
            }
        }

        /// <summary>只改显示不触发回调（用于外部批量同步设置）。</summary>
        public void SetValueSilent(int v)
        {
            v = Math.Max(Min, Math.Min(Max, v));
            if (v == _value) return;
            _value = v;
            Invalidate();
        }

        private float TrackX0 => LabelWidth + 6f;
        private float TrackX1 => Math.Max(TrackX0 + 20f, Width / S - 74f);

        private void FromMouse(float mx)
        {
            float w = TrackX1 - TrackX0;
            float t = w <= 1 ? 0 : (mx - TrackX0) / w;
            t = Math.Max(0f, Math.Min(1f, t));
            int v = Min + (int)Math.Round(t * (Max - Min));
            if (Step > 1) v = Min + (int)Math.Round((v - Min) / (float)Step) * Step;
            Value = v;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.X / S < LabelWidth) return;
            _drag = true;
            Focus();
            FromMouse(e.X / S);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) FromMouse(e.X / S);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = _value + Math.Sign(e.Delta) * Math.Max(1, Step) * 2;
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int s = Math.Max(1, Step);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { Value = _value - s; e.Handled = true; }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { Value = _value + s; e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { Value = _value - s * 10; e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp) { Value = _value + s * 10; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float w = Width / S, h = Height / S;
            using (var f = Fx.F(13f))
                TX(g, Label, f, T.Text, 0, 0, LabelWidth - 6, h);

            float ty = h / 2f;
            float x0 = TrackX0, x1 = TrackX1;
            FR(g, T.Track, x0, ty - 2.5f, x1 - x0, 5, 2.5f);
            float t = Max > Min ? (_value - Min) / (float)(Max - Min) : 0f;
            if (t > 0.001f) FR(g, T.TrackOn, x0, ty - 2.5f, (x1 - x0) * t, 5, 2.5f);

            float kx = x0 + (x1 - x0) * t;
            float kr = (_drag || _hover) ? 9.5f : 8.5f;
            using (var b = new SolidBrush(T.Knob))
                g.FillEllipse(b, (kx - kr) * S, (ty - kr) * S, kr * 2 * S, kr * 2 * S);
            using (var p = new Pen(T.Accent, 2.2f * ((_drag || _hover) ? 1.2f : 1f) * S))
                g.DrawEllipse(p, (kx - kr) * S, (ty - kr) * S, kr * 2 * S, kr * 2 * S);

            string val = Format?.Invoke(_value) ?? _value.ToString();
            float cw = 64, ch = 26;
            FR(g, T.Field, w - cw, (h - ch) / 2, cw, ch, ch / 2);
            using (var fm = Fx.M(13f, FontStyle.Bold))
                TX(g, val, fm, T.Text, w - cw, (h - ch) / 2, cw - 12, ch, ContentAlignment.MiddleRight);
        }
    }

    // =========================================================================
    // 竖向滑块（均衡器总增益）
    // =========================================================================
    internal sealed class VSlider : ThemedControl
    {
        public int Min = -200;
        public int Max = 200;
        public int Step = 5;
        public string Caption = "增益";
        public Func<int, string> Format = v => (v / 10f).ToString("0.0");

        private int _value;
        private bool _drag;

        public event Action<int> ValueChanged;

        public VSlider()
        {
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Math.Max(Min, Math.Min(Max, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                ValueChanged?.Invoke(v);
            }
        }

        public void SetValueSilent(int v)
        {
            v = Math.Max(Min, Math.Min(Max, v));
            if (v == _value) return;
            _value = v;
            Invalidate();
        }

        private float TopY => 36f;
        private float BotY => Math.Max(TopY + 20f, Height / S - 46f);

        private void FromMouse(float my)
        {
            float t = 1f - (my - TopY) / Math.Max(1f, BotY - TopY);
            t = Math.Max(0f, Math.Min(1f, t));
            int v = Min + (int)Math.Round(t * (Max - Min));
            if (Step > 1) v = Min + (int)Math.Round((v - Min) / (float)Step) * Step;
            Value = v;
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _drag = true; Focus(); FromMouse(e.Y / S); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag) FromMouse(e.Y / S); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value = _value + Math.Sign(e.Delta) * Math.Max(1, Step) * 2; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float w = Width / S, h = Height / S;
            using (var f = Fx.F(11.5f))
                TX(g, Caption, f, T.Muted, 0, 6, w, 20, ContentAlignment.MiddleCenter);

            float sx = w / 2f, y0 = TopY, y1 = BotY;
            FR(g, T.Track, sx - 2.5f, y0, 5, y1 - y0, 2.5f);
            float mid = (y0 + y1) / 2f;
            float t = (Max > Min ? (_value - Min) / (float)(Max - Min) : 0.5f);
            float ky = y1 - (y1 - y0) * t;
            using (var p = new Pen(T.TrackOn, 5f * S))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                if (Math.Abs(ky - mid) > 0.5f) g.DrawLine(p, sx * S, mid * S, sx * S, ky * S);
            }
            using (var p = new Pen(T.Muted, 1f * S))
                g.DrawLine(p, (sx - 13) * S, mid * S, (sx + 13) * S, mid * S);
            FR(g, T.Knob, sx - 10, ky - 13, 20, 26, 7);
            DR(g, T.Accent, sx - 10, ky - 13, 20, 26, 7, 1.8f);
            using (var p = new Pen(T.Accent, 2f * S))
                g.DrawLine(p, (sx - 4) * S, ky * S, (sx + 4) * S, ky * S);

            string val = Format?.Invoke(_value) ?? _value.ToString();
            float cw = w - 10, ch = 28;
            FieldBox(g, 5, h - 38, cw, ch, ch / 2, false);
            using (var fm = Fx.M(13f, FontStyle.Bold))
                TX(g, val, fm, T.Text, 5, h - 38, cw, ch, ContentAlignment.MiddleCenter);
        }
    }

    // =========================================================================
    // 下拉选择（自绘 + 主题化弹出菜单）
    // =========================================================================
    internal sealed class GlassCombo : ThemedControl
    {
        private readonly List<string> _items = new List<string>();
        private ContextMenuStrip _menu;      // 复用同一个菜单：随开随关地 Dispose 会被 WinForms 的菜单过滤器二次关闭，抛 ObjectDisposedException
        private int _sel = -1;
        private bool _hover;

        public event EventHandler SelectedIndexChanged;

        public GlassCombo() { Cursor = Cursors.Hand; }

        public int Count => _items.Count;

        public int SelectedIndex
        {
            get => _sel;
            set
            {
                int v = _items.Count == 0 ? -1 : Math.Max(0, Math.Min(_items.Count - 1, value));
                if (v == _sel) return;
                _sel = v;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string SelectedText => _sel >= 0 && _sel < _items.Count ? _items[_sel] : "";

        public void SetItems(IEnumerable<string> items, int select)
        {
            _items.Clear();
            _items.AddRange(items);
            int old = _sel;
            _sel = _items.Count == 0 ? -1 : Math.Max(0, Math.Min(_items.Count - 1, select));
            Invalidate();
            if (_sel != old) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        /// <summary>调试用：强制显示悬停态（截图核对描边发光用）。</summary>
        internal void SetHot(bool on) { _hover = on; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            ShowMenu();
        }

        /// <summary>下拉菜单本体（调试截图用）。</summary>
        internal ContextMenuStrip Menu => _menu;

        public void ShowMenu()
        {
            if (_items.Count == 0) return;
            if (_menu == null)
            {
                _menu = new ContextMenuStrip { ShowImageMargin = false, DropShadowEnabled = true };
                _menu.Closed += (s, e) => { /* 菜单留着复用，别在关闭回调里 Dispose */ };
            }
            _menu.Items.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(_items[i]) { Checked = i == _sel };
                it.Click += (s, args) => SelectedIndex = idx;
                _menu.Items.Add(it);
            }
            UiMenu.Style(_menu, S);
            try { _menu.Show(this, new Point(0, Height)); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _menu?.Dispose(); } catch { }
                _menu = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float w = Width / S, h = Height / S;
            float rad = h / 2f;
            FieldBox(g, 0, 0, w, h, rad, _hover);
            using (var f = Fx.F(13f))
                TX(g, SelectedText, f, _sel >= 0 ? T.Text : T.Muted, Math.Max(15f, h * 0.42f), 0, w - 46, h);
            using (var p = new Pen(T.Muted, 1.6f * S))
                g.DrawLines(p, new[]
                {
                    new PointF((w - 24) * S, (h / 2 - 2.5f) * S),
                    new PointF((w - 17) * S, (h / 2 + 4f) * S),
                    new PointF((w - 10) * S, (h / 2 - 2.5f) * S),
                });
        }
    }

    /// <summary>
    /// 下拉菜单 / 托盘菜单的统一外观：圆角弹层、舒服的留白、细分隔线、圆角悬停块，纯文字不要图标。
    /// 菜单是独立的顶层窗口，缩放系数得单独传进来。
    /// </summary>
    internal static class UiMenu
    {
        private static int Px(float v, float s) => (int)Math.Round(v * s);
        private static readonly object HookMark = new object();

        public static void Style(ContextMenuStrip menu, float scale)
        {
            if (menu == null) return;
            float s = scale <= 0.05f ? 1f : scale;
            var t = UiThemes.Current;
            menu.SuspendLayout();
            try
            {
                // 先赋 Renderer（赋值会自动把 RenderMode 切成 Custom；反过来设 RenderMode 会抛异常）
                menu.Renderer = new UiMenuRenderer(t, s);
                menu.BackColor = t.MenuBg;
                menu.ForeColor = t.Text;
                menu.ShowImageMargin = false;
                menu.DropShadowEnabled = true;
                menu.Font = new Font(UiFonts.Ui, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel);
                // 左边：内边距 6 + 文字右移 22 = 28 个设计单位；
                // 右边要补到 33：右移是渲染时做的，不会把弹层撑宽，所以那 22 里有一部分是从右边留白里"借"的。
                // 补完之后最长的那一项离右边框和离左边差不多，不再像框在勒字。
                menu.Padding = new Padding(Px(6, s), Px(10, s), Px(33, s), Px(10, s));
                menu.MinimumSize = new Size(Px(200, s), 0);
                foreach (ToolStripItem it in menu.Items)
                {
                    if (it is ToolStripSeparator)
                    {
                        it.AutoSize = false;
                        it.Height = Px(7, s);
                        it.Padding = Padding.Empty;
                    }
                    else
                    {
                        // 左右留白在渲染器里统一补（工具项自己的左右 Padding 系统不认），这里只管行高：
                        // 行高给足，文字上下才不"窒息"；同时让菜单看起来不"矮胖"
                        it.Padding = new Padding(0, Px(15, s), 0, Px(15, s));
                    }
                }
            }
            catch { }
            finally { menu.ResumeLayout(); }

            // 圆角只在窗口显示出来以后才有尺寸可算；挂一次就够，重复 Style 不会再挂
            try
            {
                if (!ReferenceEquals(menu.Tag, HookMark))
                {
                    menu.Tag = HookMark;
                    menu.Opened += (a, b) => RoundCorners(menu);
                    menu.SizeChanged += (a, b) => RoundCorners(menu);
                }
            }
            catch { }
        }

        /// <summary>弹层的圆角：菜单是方窗口，只能给窗口设 Region 才能圆角。</summary>
        public static void RoundCorners(ContextMenuStrip menu)
        {
            try
            {
                if (menu == null || menu.Width <= 2 || menu.Height <= 2) return;
                float s = (menu.Renderer as UiMenuRenderer)?.Scale ?? 1f;
                menu.Region?.Dispose();
                menu.Region = new Region(Shaper.Round(new RectangleF(0, 0, menu.Width, menu.Height), 14f * s));
            }
            catch { }
        }
    }

    /// <summary>按主题和缩放画菜单（没有图标列，悬停是浅绿圆角块 + 左实右虚的发丝细边）。</summary>
    internal sealed class UiMenuRenderer : ToolStripRenderer
    {
        private readonly UiTheme _t;
        private readonly float _s;
        /// <summary>弹层圆角（设计单位）。</summary>
        private const float Rad = 14f;
        /// <summary>悬停块的圆角。</summary>
        private const float RadHover = 9f;

        public UiMenuRenderer(UiTheme t, float s)
        {
            _t = t ?? UiThemes.Current;
            _s = s <= 0.05f ? 1f : s;
        }

        public float Scale => _s;

        private float Px(float v) => v * _s;

        private float Line => Math.Max(1f, Px(1f));

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(_t.MenuBg))
                Shaper.FillRound(g, b, new RectangleF(0, 0, e.ToolStrip.Width, e.ToolStrip.Height), Px(Rad));
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float lw = Line;
            // 描边整体往里缩一个线宽：贴着窗口边画的话，最下面那条线会被裁掉，只剩半条
            float w = e.ToolStrip.Width, h = e.ToolStrip.Height;
            var r = new RectangleF(lw, lw, Math.Max(1f, w - lw * 2f), Math.Max(1f, h - lw * 2f));
            using (var p = new Pen(_t.MenuBorder, lw))
                Shaper.DrawRound(g, p, r, Math.Max(1f, Px(Rad) - lw));
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var it = e.Item;
            if (!it.Selected && !it.Pressed) return;
            var g = e.Graphics;
            // 扁平长条形浅灰：全宽、无圆角、无描边。
            // 注意 item 自己的宽度不含菜单的左右 Padding（右边留了 33 个设计单位给文字避让），
            // 直接画 it.Width 会"前面有、后面没有"—— 这里从菜单左缘画到菜单右缘，把整行拉满。
            var ts = e.ToolStrip;
            using (var b = new SolidBrush(_t.MenuHover))
                g.FillRectangle(b, -ts.Padding.Left, 0, ts.Width, it.Height);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            int y = e.Item.Height / 2;
            using (var p = new Pen(_t.MenuBorder, Math.Max(1f, Px(1f))))
                g.DrawLine(p, Px(14), y, e.Item.Width - Px(14), y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _t.Text : Color.FromArgb(110, _t.Text);
            bool check = e.Item is ToolStripMenuItem cm && cm.Checked;
            // 系统把选项文字摆在离左边框只有 ~4 个设计单位的地方（太挤），这里统一右移；
            // 有勾选的项用同一个起点（勾画在左边的空槽里），所有选项的文字才对得齐。
            // 右移之后右边要留出等量的空（见 Style 里的 Padding.Right），否则最长那一项会贴边。
            float want = 22f * _s;
            int dx = (int)Math.Round(want - e.TextRectangle.X);
            // 系统还把文字顶在选项的上沿（行高有多余时不会自己居中），这里按行高补一次垂直居中
            int dy = (int)Math.Round((e.Item.Height - e.TextRectangle.Height) / 2f - e.TextRectangle.Y);
            if (dx != 0 || dy != 0)
                e.TextRectangle = new Rectangle(e.TextRectangle.X + dx, e.TextRectangle.Y + dy,
                                                e.TextRectangle.Width, e.TextRectangle.Height);
            // ShowImageMargin=false 时系统不会画勾选框，这里自己给"当前选中项"画一个绿色小勾（纯指示，不是图标）
            if (check)
            {
                var g = e.Graphics;
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = new Pen(_t.Accent, Math.Max(1.4f, Px(2f))))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    float cx = Px(10f), cy = e.Item.Height / 2f;
                    g.DrawLines(p, new[]
                    {
                        new PointF(cx - Px(4f), cy),
                        new PointF(cx - Px(1f), cy + Px(3.4f)),
                        new PointF(cx + Px(4.6f), cy - Px(3.6f)),
                    });
                }
                g.SmoothingMode = old;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // 勾由 OnRenderItemText 统一画（ShowImageMargin=false 时这里不一定会被调用）
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ArrowRectangle;
            using (var p = new Pen(_t.Muted, Math.Max(1.2f, Px(1.6f))))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLines(p, new[]
                {
                    new PointF(r.Left + r.Width * 0.30f, r.Top + r.Height * 0.36f),
                    new PointF(r.Left + r.Width * 0.64f, r.Top + r.Height * 0.62f),
                    new PointF(r.Left + r.Width * 0.30f, r.Top + r.Height * 0.86f),
                });
            }
        }
    }

    // =========================================================================
    // 数字输入（频率 / 数值）：上下箭头微调，双击可直接输入
    // =========================================================================
    internal sealed class NumField : ThemedControl
    {
        public float Min = 20;
        public float Max = 20000;
        public float Step = 1;
        public int Decimals = 0;
        public string Unit = "";
        public Func<float, string> Format;

        private float _value;
        private bool _hover;
        private TextBox _editor;

        public event Action<float> ValueChanged;

        public NumField() { Cursor = Cursors.Hand; }

        public float Value
        {
            get => _value;
            set
            {
                float v = Math.Max(Min, Math.Min(Max, value));
                if (Math.Abs(v - _value) < 0.0001f) return;
                _value = v;
                Invalidate();
                ValueChanged?.Invoke(v);
            }
        }

        public void SetValueSilent(float v)
        {
            v = Math.Max(Min, Math.Min(Max, v));
            if (Math.Abs(v - _value) < 0.0001f) return;
            _value = v;
            Invalidate();
        }

        private string Display()
        {
            if (Format != null) return Format(_value);
            string s = _value.ToString(Decimals > 0 ? "0." + new string('0', Decimals) : "0");
            return Unit.Length > 0 ? s + Unit : s;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            float w = Width / S, h = Height / S;
            float mx = e.X / S, my = e.Y / S;
            if (mx > w - 30)
            {
                if (my < h / 2) Nudge(+1); else Nudge(-1);
                Invalidate();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.X / S < Width / S - 30) StartEdit();
        }

        private void Nudge(int dir)
        {
            Keys m = ModifierKeys;
            float st = Step;
            if ((m & Keys.Shift) != 0) st *= 10;
            if ((m & Keys.Control) != 0) st *= 100;
            Value = _value + dir * st;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Nudge(Math.Sign(e.Delta));
        }

        private void StartEdit()
        {
            if (_editor != null) return;
            _editor = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Text = _value.ToString(Decimals > 0 ? "0." + new string('0', Decimals) : "0"),
                TextAlign = HorizontalAlignment.Center,
                Font = new Font(UiFonts.Mono, 13f * S, FontStyle.Bold, GraphicsUnit.Pixel),
                BackColor = T.Field,
                ForeColor = T.Text,
                Bounds = new Rectangle((int)Math.Round(5 * S), (int)Math.Round(Height / 2f - 11 * S),
                                       Math.Max(20, Width - (int)Math.Round(40 * S)), (int)Math.Round(22 * S))
            };
            _editor.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { ApplyEdit(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { EndEdit(); }
            };
            _editor.LostFocus += (s, e) => ApplyEdit();
            Controls.Add(_editor);
            _editor.Focus();
            _editor.SelectAll();
        }

        private void ApplyEdit()
        {
            if (_editor == null) return;
            float v;
            if (float.TryParse(_editor.Text.Trim(), out v)) Value = v;
            EndEdit();
        }

        private void EndEdit()
        {
            if (_editor == null) return;
            var ed = _editor;
            _editor = null;
            try { Controls.Remove(ed); ed.Dispose(); } catch { }
            Focus();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float w = Width / S, h = Height / S;
            float rad = h / 2f;
            if (_editor != null)
            {
                FR(g, T.Field, 0, 0, w, h, rad);
                FieldStroke(g, 0, 0, w, h, rad, true);
                return;
            }
            FieldBox(g, 0, 0, w, h, rad, _hover);
            using (var f = Fx.M(13.5f, FontStyle.Bold))
                TX(g, Display(), f, T.Text, Math.Max(12f, h * 0.4f + 4f), 0, w - 46, h);
            float ax = w - 22;
            var col = _hover ? T.Text : T.Muted;
            using (var p = new Pen(col, 1.5f * S))
            {
                g.DrawLines(p, new[]
                {
                    new PointF((ax - 5) * S, (h / 2 - 6) * S),
                    new PointF((ax) * S, (h / 2 - 11) * S),
                    new PointF((ax + 5) * S, (h / 2 - 6) * S),
                });
                g.DrawLines(p, new[]
                {
                    new PointF((ax - 5) * S, (h / 2 + 6) * S),
                    new PointF((ax) * S, (h / 2 + 11) * S),
                    new PointF((ax + 5) * S, (h / 2 + 6) * S),
                });
            }
        }
    }

    /// <summary>只读的键值小行（例如"延迟 ms"标签）。</summary>
    internal sealed class KeyValueText : ThemedControl
    {
        public string Text2 = "";
        public float FontPx = 11.5f;
        public ContentAlignment Align = ContentAlignment.MiddleLeft;

        public KeyValueText() { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var f = Fx.F(FontPx))
                TX(g, Text2, f, T.Muted, 0, 0, Width / S, Height / S, Align);
        }
    }
}
