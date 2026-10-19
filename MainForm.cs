using System;using System.Collections.Generic;using System.Drawing;using System.Drawing.Drawing2D;using System.IO;using System.Linq;using System.Windows.Forms;using NAudio.CoreAudioApi;using NAudio.Wave;using Surround71.Audio;using Surround71.Interop;using Surround71.Ui;namespace Surround71{
    public sealed class MainForm : Form
    {
        private readonly MMDeviceEnumerator _en = new MMDeviceEnumerator();

        private readonly List<MMDevice> _renderDevices = new List<MMDevice>();

        private readonly List<MMDevice> _captureDevices = new List<MMDevice>();

        private DeviceChangeNotifier _notifier;

        private bool _refreshing;

        private readonly System.Windows.Forms.Timer _deviceTimer = new System.Windows.Forms.Timer();

        // ---------------- 托盘（常驻后台） ----------------
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayGlobal, _trayAutostart;
        private bool _realExit;        // 只有托盘菜单的"退出"才真正关闭
        private bool _balloonShown;    // 首次收进托盘时提示一次
        private bool _backgroundStart; // 带 --background 参数：开机自启时静默进托盘

        private SurroundSettings _settings;

        // ---------------- 设置持久化（%AppData%\Surround71\settings.json） ----------------
        private readonly SettingsStore.Data _persist = SettingsStore.Load();
        private readonly System.Windows.Forms.Timer _saveTimer = new System.Windows.Forms.Timer();
        private bool _firstDeviceFill = true;  // 第一次填设备列表时用持久化的 ID 还原选择
        private bool _silentStart;             // 开机自动开始环绕时不弹任何框
        private ToolStripMenuItem _trayAutostartGlobal;

        private SurroundPipeline _preview;

        private SurroundEngine _engine;

        private ISampleProvider _currentSource;

        private ChannelTestProvider _testSource;

        /// <summary>
        /// 试听链当前在放什么。以前只靠 _preview 在不在判断，结果"播放文件"也会把
        /// 「试听演示曲」那个按钮改成"停止试听" —— 按钮指着演示曲，实际放的是用户的文件。
        /// 现在每个按钮只认自己那一路。
        /// </summary>
        private enum PreviewKind { None, Demo, File, Channel }
        private PreviewKind _previewKind = PreviewKind.None;
        private long _previewStartedAt;        // 试听链启动时刻，给"被系统掐断"的看门狗留缓冲
        private long _previewLostAt;           // 试听链开始"不出声"的时刻（0 = 正常）

        private readonly System.Windows.Forms.Timer _uiTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _eqTimer = new System.Windows.Forms.Timer();
        private long _eqIdleTicks = 999;
        private static readonly float[] ZeroLevels = new float[SpectrumTap.Bands];

        private string _savedDefaultId;

        private string _savedDefaultName;
        private MMDevice _mutedCapDev;         // 被我们临时静音掉的「环回来源」端点
        private bool _mutedWasOn;              // 它原本是不是就静音着（停止时要还原）
        private bool _hasSurroundOut;      // 本机有没有 8 声道输出端点（决定"7.1 音箱直出"要不要出现）

        /// <summary>
        /// 是否保留"虚拟声卡"这条采集路（VB-Cable 之类第三方驱动）。
        ///
        /// 设成 false = 从产品里彻底删掉它：界面上不再出现"采集方式"这一行，
        /// 永远走 Windows 自带的系统环回（WASAPI Loopback），用户不用装任何东西。
        /// 引擎里的虚拟声卡支持没有删，改回 true 就整套恢复。
        /// </summary>
        private static readonly bool AllowCable = false;

        // ---------------- 界面控件（全部自绘，见 Ui\ 目录） ----------------
        private GlassCard _cardMode, _cardDevice, _cardParam, _cardField, _cardEq;
        private OptionCard _optHeadphone, _optSpeakers;
        private SegmentedControl _segCapture;
        private GlassCombo _cmbOutput, _cmbCapture, _cmbPreset, _cmbEqPreset;
        private ToggleSwitch _swAutoSwitch, _swEqEnable, _swMuteSrc;
        private ModernSlider _slVolume;   // 输出音量：接管默认设备后音量键调的是被静音的环回来源，这里给出真正有效的音量
        private PlainText _lblOutDev, _lblCapKind, _lblCapDev, _lblPreset, _lblAutoSwitch, _lblMuteSrc;
        private PlainText _lblEqPreset, _lblEqEnable;
        private readonly Dictionary<string, ModernSlider> _sliders = new Dictionary<string, ModernSlider>();
        private readonly Dictionary<string, (int col, int row)> _sliderPos = new Dictionary<string, (int col, int row)>();
        private TargetPanel _layout;
        private EqCurvePanel _eqCurve;
        private VSlider _eqPreamp;
        private readonly GlassCombo[] _eqType = new GlassCombo[EqBands.Count];
        private readonly NumField[] _eqFreq = new NumField[EqBands.Count];
        private readonly PlainText[] _eqRange = new PlainText[EqBands.Count];

        // ---------------- 自适应缩放（设计单位 × _uiScale，每次重排都从设计尺寸重算，不累积误差）----------------
        private float _uiScale = 1f;           // DPI × 窗口缩放
        private float _dpi = 1f;               // 系统 DPI 比例
        private float _zoom = 1f;              // 窗口比设计尺寸小时的等比缩放（<1）
        private bool _inRelayout;
        private bool _layoutReady;
        private Bitmap _bgCache;               // 窗口底（渐变 + 柔光光斑）缓存
        private bool _resizing;                // 正在拖动窗口大小：只画纯色底
        private readonly System.Windows.Forms.Timer _reflowTimer = new System.Windows.Forms.Timer();
        private bool _syncing;                 // 正在把设置同步到界面，抑制控件回调

        private PillButton _btnPreview, _btnFile, _btnChannelTest, _btnLatch, _btnGlobal, _btnRestore;
        private bool _globalBtnRunning;
        private long _engineLostAt;            // 引擎对象还在、采集却停了：从这个时刻开始计时
        private string _stoppedNotice;         // 因故障自动停止时，状态栏要说的那一句
        private long _zeroCapAt;               // 全局环绕启动后一直 0 帧的起始时刻（用于提示"没抓到声音"）
        private PlainText _lblStatus, _lblNowPlaying;

        // ---------------- 默认播放设备接管（全局环绕时自动切、停止时切回） ----------------
        private bool _defaultTakenOver;        // 这次运行中是我们把默认设备切走的
        private bool _guardSuppress;           // 我们自己切设备时，别让"守护"再切一次
        private bool _guardBalloonShown;       // 守护只提示一次，免得反复弹气泡
        private string _runCapId, _runOutId;   // 正在跑的这一对端点 ID（用来判断设备是否被拔掉）
        private int _autoFixDepth;             // 「一键配好并启动」的重入保护
        private PlainText _lblAutoHint;        // 主按钮旁边的小字说明
        private EqHelpPanel _eqHelp;           // 六段均衡器使用说明（连点声场图 3 下打开）
        private bool _eqHelpShown;             // 说明书是否展开（Visible 交给 Relayout 统一落地）

        private float _peakL, _peakR;

        /// <summary>设计尺寸（96 DPI 设计单位）。窗口比它小就整体等比缩小，比它大就拉伸卡片。</summary>
        private const float DW = 1200f, DH = 774f;

        /// <summary>调试用：--size 宽x高（物理像素）强制启动时的客户区尺寸，方便检查不同窗口大小下的布局。</summary>
        public static Size? ForcedClientSize;

        public MainForm()
        {
            DoubleBuffered = true;   // 浮层展开 / 重排时不再看到子控件逐个重画出来的条纹
            // 注意：WinForms 的 AutoScaleMode 会把基准尺寸重置成"当前 DPI"，
            // 缩放系数永远是 1 —— 在 175% 缩放的本机上文字会全被截断。
            // 所以这里不用 AutoScaleMode，自己按 DPI 等比放大整棵控件树，见 ApplyDpiScale。
            _settings = SurroundPresets.All().First(p => p.Name == "电影环绕").Clone();
            // 上次的音效参数直接就地恢复（设备选择等列表填好后再还原，见 RefreshLists）
            if (_persist.HaveAudio && _persist.Audio != null) _settings.CopyFrom(_persist.Audio);
            // 拖动窗口大小：先只重画纯色底（快），停手 90ms 后再整体重排 —— 避免缩放时反复重排卡死
            _reflowTimer.Interval = 90;
            _reflowTimer.Tick += (s, e) =>
            {
                _reflowTimer.Stop();
                _resizing = false;
                Reflow();
                Invalidate(true);
            };
            BuildUi();
            // 还原上次的开关状态（音效参数已在 _settings 里恢复，设备选择等列表填好后再还原）
            try
            {
                if (_persist.MuteSrc.HasValue && _swMuteSrc != null) _swMuteSrc.Checked = _persist.MuteSrc.Value;
                if (_persist.AutoSwitch.HasValue && _swAutoSwitch != null) _swAutoSwitch.Checked = _persist.AutoSwitch.Value;
                _btnLatch?.SetChecked(_persist.Latch);
            }
            catch { }
            LoadDevices();
            ApplySettingsToUi();
            _uiTimer.Interval = 90;
            _uiTimer.Tick += (s, e) => UiTick();
            _uiTimer.Start();
            _deviceTimer.Interval = 400;
            _deviceTimer.Tick += (s, e) => { _deviceTimer.Stop(); RefreshDevices(); };
            // 均衡器实时频谱：只在"正在出声 + 窗口可见"时才按 30fps 跑，其余时间停着不烧 CPU
            _eqTimer.Interval = 33;
            _eqTimer.Tick += (s, e) => EqTick();
            // 设置改动防抖保存：改动后 1.2 秒没再动才落盘，拖滑杆不会每格写一次磁盘
            _saveTimer.Interval = 1200;
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SavePersist(); };
            // 设备插入 / 拔出 / 驱动重装时自动刷新设备列表（例如装好 VB-Cable 后不用重启程序）
            try
            {
                _notifier = new DeviceChangeNotifier(this);
                _en.RegisterEndpointNotificationCallback(_notifier);
            }
            catch { }

            // 内部控件齐全：允许自适应重排，并立即按当前尺寸排一次
            _layoutReady = true;
            Relayout();
            _backgroundStart = Environment.GetCommandLineArgs().Any(
                a => string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase));

            BuildTray();
            RegisterShowRequest();   // 第二个实例启动时把托盘里的主窗口顶出来

            // 开机自启 + 勾了"启动后自动开始环绕"：等设备列表稳定后静默进入全局环绕。
            // 全程不弹框 —— 启动失败就安静地留在托盘，别在用户登录时突然蹦一个错误窗出来。
            if (_backgroundStart && GetAutostartGlobal())
            {
                var autoStart = new System.Windows.Forms.Timer { Interval = 2500 };
                autoStart.Tick += (s2, e2) =>
                {
                    autoStart.Stop(); autoStart.Dispose();
                    if (_engine == null && _preview == null && CanRunGlobal())
                    {
                        _silentStart = true;
                        try { ToggleGlobal(); }
                        finally { _silentStart = false; }
                    }
                };
                autoStart.Start();
            }

            // 调试用：--demo 自动播放试听演示曲；--menutest / --traytest 自动弹出菜单，方便截图核对样式
            {
                string[] argv = Environment.GetCommandLineArgs();
                bool dbgDemo = argv.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));
                bool dbgMenu = argv.Any(a => string.Equals(a, "--menutest", StringComparison.OrdinalIgnoreCase));
                bool dbgTray = argv.Any(a => string.Equals(a, "--traytest", StringComparison.OrdinalIgnoreCase));
                bool dbgHover = argv.Any(a => string.Equals(a, "--hoverall", StringComparison.OrdinalIgnoreCase));
                bool dbgSnap = argv.Any(a => string.Equals(a, "--snap", StringComparison.OrdinalIgnoreCase));
                if (dbgDemo || dbgMenu || dbgTray || dbgHover || dbgSnap)
                {
                    var dbgTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                    dbgTimer.Tick += (s2, e2) =>
                    {
                        dbgTimer.Stop();
                        dbgTimer.Dispose();
                        try
                        {
                            if (dbgDemo) TogglePreview();
                            if (dbgMenu)
                            {
                                _cmbOutput?.ShowMenu();
                                // 调试截图用：让第一项处于"选中/悬停"态，核对浅绿底 + 发丝细边
                                var dm = _cmbOutput?.Menu;
                                if (dm != null && dm.Items.Count > 0) dm.Items[0].Select();
                                DbgSnap(dm, "combo");
                            }
                            if (dbgTray) { _trayMenu?.Show(this, new Point(SRf(40), SRf(60))); DbgSnap(_trayMenu, "tray"); }
                            if (dbgHover)
                            {
                                // 让程序自己把鼠标移过去：它是 DPI-aware 的，坐标一定准（外面脚本算不准）
                                var c = _cmbOutput;
                                if (c != null && c.IsHandleCreated)
                                {
                                    Cursor.Position = c.PointToScreen(new Point(c.Width / 2, c.Height / 2));
                                    c.SetHot(true);   // 合成鼠标移动不一定触发 MouseEnter，这里直接强制悬停态
                                }
                                if (_btnPreview != null) { _btnPreview.Hover = true; _btnPreview.Invalidate(); }
                                DbgLog("hover forced");
                            }
                            if (dbgSnap)
                            {
                                DbgLog("theme=" + UiThemes.Current.Kind + " client=" + ClientSize.Width + "x" + ClientSize.Height +
                                       " uiScale=" + _uiScale.ToString("0.###"));
                                DbgLog("cards: " + string.Join(" | ", new[]
                                {
                                    "mode=" + Bb(_cardMode), "dev=" + Bb(_cardDevice), "param=" + Bb(_cardParam),
                                    "field=" + Bb(_cardField), "eq=" + Bb(_cardEq),
                                    "btnPreview=" + Bb(_btnPreview), "btnGlobal=" + Bb(_btnGlobal),
                                    "cmbOutput=" + Bb(_cmbOutput), "target=" + Bb(_layout),
                                }));
                                // 每张卡单独截：DrawToBitmap 的原点就是控件自己的左上角，
                                // 和日志里的 Left/Top 严格对齐（整窗截还要再扣窗体非客户区，容易错）
                                DbgSnap(_cardMode, "card_mode");
                                DbgSnap(_cardDevice, "card_dev");
                                DbgSnap(_cardParam, "card_param");
                                DbgSnap(_cardField, "card_field");
                                DbgSnap(_cardEq, "card_eq");
                                DbgSnap(this, "form");
                                // 整窗截图的原点约定：Form.DrawToBitmap 画的是整个窗口（含非客户区），
                                // 所以客户区在 PNG 里的偏移 = PointToScreen(客户区原点) - Bounds.Location
                                DbgLog("formSnap: pngSize=" + Width + "x" + Height +
                                       " bounds=" + Bounds.X + "," + Bounds.Y + " " + Bounds.Width + "x" + Bounds.Height +
                                       " clientOrigin=" + (PointToScreen(Point.Empty).X - Bounds.X) + "," +
                                       (PointToScreen(Point.Empty).Y - Bounds.Y));
                            }
                        }
                        catch (Exception ex) { DbgLog("dbg: " + ex.Message); }
                    };
                    dbgTimer.Start();
                }
            }

            // 点“×”、最小化、Alt+F4 → 收进托盘继续运行；只有托盘菜单里的“退出”才真正结束。
            // 注意：不要按 CloseReason.UserClosing 判断 —— 实测这台机器上 WM_CLOSE 会被 WinForms
            // 归类成 TaskManagerClosing，现实中点 × 也不是 UserClosing，一律以 _realExit 为准。
            // 系统关机时（WindowsShutDown）不拦截，正常走 Shutdown（停环绕、恢复默认设备）。
            FormClosing += (s, e) =>
            {
                if (!_realExit && e.CloseReason != CloseReason.WindowsShutDown)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }
                Shutdown();
            };

            // 点最小化按钮 → 同样收进托盘
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized && !_realExit) HideToTray();
            };

            if (_backgroundStart)
            {
                Opacity = 0;
                Shown += (s, e) =>
                {
                    HideToTray();
                    WindowState = FormWindowState.Minimized;
                    Opacity = 1;
                    ShowBalloon("PC 7.1 环绕声已在后台运行",
                                "系统声音会按当前设置继续处理。双击托盘图标打开主窗口，右键托盘图标可退出。");
                };
            }
        }

        // =====================================================================
        // 托盘：常驻后台
        // =====================================================================
        private void BuildTray()
        {
            _trayMenu = new ContextMenuStrip();

            _trayGlobal = (ToolStripMenuItem)_trayMenu.Items.Add("开始全局环绕", null, (s, e) => ToggleGlobal());
            _trayMenu.Items.Add(new ToolStripSeparator());

            // “开机自动启动”没有地方放第二个勾选框，收进托盘菜单（勾选状态写注册表 Run 键）
            _trayAutostart = (ToolStripMenuItem)_trayMenu.Items.Add("开机自动启动");
            _trayAutostart.CheckOnClick = true;
            _trayAutostart.Checked = GetAutostart();
            _trayAutostart.Click += (s, e) => SetAutostart(_trayAutostart.Checked);

            // 开机进托盘后不用再点一下"开始"：勾上它，程序起来几秒后自动进入全局环绕
            _trayAutostartGlobal = (ToolStripMenuItem)_trayMenu.Items.Add("启动后自动开始环绕");
            _trayAutostartGlobal.CheckOnClick = true;
            _trayAutostartGlobal.Checked = GetAutostartGlobal();
            _trayAutostartGlobal.Click += (s, e) => SetAutostartGlobal(_trayAutostartGlobal.Checked);

            _trayMenu.Items.Add(new ToolStripSeparator());
            _ = _trayMenu.Items.Add("退出", null, (s, e) => RealExit());

            // 菜单是独立的顶层窗口，主题和缩放都要在弹出前现算一次
            _trayMenu.Opening += (s, e) => UiMenu.Style(_trayMenu, _uiScale);

            _tray = new NotifyIcon
            {
                Icon = MakeTrayIcon(_globalBtnRunning),
                Text = "PC 7.1 环绕声",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _tray.DoubleClick += (s, e) => ShowFromTray();
        }

        /// <summary>程序 LOGO（内嵌 app.ico）：窗口标题栏与托盘共用。</summary>
        private static Icon LoadAppIcon()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("Surround71.app.ico");
                if (s != null) return new Icon(s, new Size(32, 32));
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            return SystemIcons.Application;
        }

        /// <summary>托盘图标：和 exe / 标题栏同一个 LOGO（内嵌 app.ico，取 32 尺寸，高 DPI 下也清楚）。
        /// running = 右下角加一枚绿点角标：收进托盘后不用点开菜单也知道"全局还开着"。</summary>
        private Icon _trayIconIdle, _trayIconRun;
        private Icon MakeTrayIcon(bool running)
        {
            try
            {
                if (running && _trayIconRun != null) return (Icon)_trayIconRun.Clone();
                if (!running && _trayIconIdle != null) return (Icon)_trayIconIdle.Clone();
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("Surround71.app.ico");
                if (s != null)
                {
                    using var baseIco = new Icon(s, new Size(32, 32));
                    using var bmp = new Bitmap(32, 32);
                    using (var g = Graphics.FromImage(bmp))
                        g.DrawIcon(baseIco, new Rectangle(0, 0, 32, 32));
                    if (running)
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            using var br = new SolidBrush(Color.FromArgb(45, 212, 152));
                            g.FillEllipse(br, 19, 19, 11, 11);
                            using var ring = new Pen(Color.White, 2f);
                            g.DrawEllipse(ring, 20, 20, 9, 9);
                        }
                    }
                    var made = Icon.FromHandle(bmp.GetHicon());
                    if (running) { _trayIconRun = made; return (Icon)made.Clone(); }
                    _trayIconIdle = made;
                    return (Icon)made.Clone();
                }
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            return SystemIcons.Application;
        }

        private void HideToTray()
        {
            Hide();
            WindowState = FormWindowState.Minimized;
            if (!_balloonShown)
            {
                _balloonShown = true;
                ShowBalloon("仍在后台运行",
                            "声音处理不会停止。双击托盘图标随时打开主窗口；右键托盘图标可退出。");
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        /// <summary>托盘菜单里的“退出”：这才是真正结束进程（会先停掉全局环绕并恢复默认播放设备）。</summary>
        private void RealExit()
        {
            _realExit = true;
            Close();
        }

        private void ShowBalloon(string title, string text)
        {
            try
            {
                if (_tray == null) return;
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.ShowBalloonTip(4000);
            }
            catch { }
        }

        // ---------------- 开机自启（注册表 HKCU Run） ----------------
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "Surround71";

        private static bool GetAutostart()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return k?.GetValue(RunValueName) != null;
            }
            catch { return false; }
        }

        private static void SetAutostart(bool on)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (on) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --background");
                else k.DeleteValue(RunValueName, false);
            }
            catch { }
        }

        // ---------------- 启动后自动开始环绕（注册表 HKCU Software\Surround71） ----------------
        private const string AppKeyPath = @"Software\Surround71";

        private static bool GetAutostartGlobal()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppKeyPath);
                return Equals(k?.GetValue("AutostartGlobal"), 1);
            }
            catch { return false; }
        }

        private static void SetAutostartGlobal(bool on)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AppKeyPath);
                k.SetValue("AutostartGlobal", on ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>第二个实例被启动时通知第一个实例把主窗口顶出来（避免出现两个都在跑的进程）。</summary>
        private void RegisterShowRequest()
        {
            try
            {
                var evt = new EventWaitHandle(false, EventResetMode.AutoReset, "Surround71_ShowWindow_Ask");
                System.Threading.ThreadPool.RegisterWaitForSingleObject(evt,
                    (state, timedOut) =>
                    {
                        try { BeginInvoke(new Action(ShowFromTray)); } catch { }
                    },
                    null, -1, false);
            }
            catch { }
        }

        // =====================================================================
        // 界面
        // =====================================================================
        private void BuildUi()
        {
            Text = "PC 7.1 环绕声  —  耳机虚拟 7.1 环绕 / 真实 7.1 音箱";
            try { Icon = LoadAppIcon(); } catch { }
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size((int)DW, (int)DH);
            MinimumSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiThemes.Current.Bg1;

            // ================= ① 输出模式 =================
            _cardMode = new GlassCard { Title = "输出模式" };
            Controls.Add(_cardMode);

            _optHeadphone = new OptionCard { Caption = "输出方式", StatusText = "耳机虚拟 7.1", Icon = GlyphKind.Headphone };
            _optHeadphone.Clicked += (s, e) => SetHeadphoneMode(true);
            _cardMode.Controls.Add(_optHeadphone);

            _optSpeakers = new OptionCard { Caption = "输出方式", StatusText = "7.1 音箱直出", Icon = GlyphKind.Speaker7 };
            _optSpeakers.Clicked += (s, e) => SetHeadphoneMode(false);
            _cardMode.Controls.Add(_optSpeakers);

            // ================= ② 设备与预设 =================
            _cardDevice = new GlassCard { Title = "设备" };
            Controls.Add(_cardDevice);

            _lblOutDev = RowLabel(_cardDevice, "输出设备");
            _cmbOutput = new GlassCombo();
            _cmbOutput.SelectedIndexChanged += (s, e) => OnOutputChanged();
            _cardDevice.Controls.Add(_cmbOutput);

            _lblCapKind = RowLabel(_cardDevice, "采集方式");
            _segCapture = new SegmentedControl { Items = new[] { "虚拟声卡", "系统环回" } };
            _segCapture.SelectedIndexChanged += (s, e) => OnCaptureKindChanged();
            _cardDevice.Controls.Add(_segCapture);

            _lblCapDev = RowLabel(_cardDevice, "采集设备");
            _cmbCapture = new GlassCombo();
            _cardDevice.Controls.Add(_cmbCapture);

            _lblPreset = RowLabel(_cardDevice, "环绕预设");
            _cmbPreset = new GlassCombo();
            _cmbPreset.SelectedIndexChanged += (s, e) => OnPresetChanged();
            _cardDevice.Controls.Add(_cmbPreset);

            // 环回是"从扬声器上再复制一份"，扬声器本身照旧出声 —— 于是耳机和自带喇叭一起响。
            // 默认把它静音掉，只听耳机；想两边都响（比如一起看电影）就把这个开关关掉。
            _lblMuteSrc = new PlainText { LabelText = "环回时静音环回来源", FontPx = 12.5f };
            _cardDevice.Controls.Add(_lblMuteSrc);
            _swMuteSrc = new ToggleSwitch { Checked = true };
            _swMuteSrc.CheckedChanged += (s, e) =>
            {
                ScheduleSave();
                // 运行中改这个开关，立刻生效，不用停了再开
                if (_engine?.IsRunning != true || _engine.Kind != CaptureKind.Loopback) return;
                if (_swMuteSrc.Checked) MuteLoopbackSource(SelectedCapture());
                else RestoreLoopbackSource();
            };
            _cardDevice.Controls.Add(_swMuteSrc);

            _lblAutoSwitch = new PlainText { LabelText = "启动时自动接管默认播放设备", FontPx = 12.5f };
            _cardDevice.Controls.Add(_lblAutoSwitch);
            _swAutoSwitch = new ToggleSwitch { Checked = true };
            _cardDevice.Controls.Add(_swAutoSwitch);

            // 输出音量：控制"输出设备"端点的系统音量。
            // 环回把默认播放设备切到环回来源（还被静音了），音量键改的是那个端点 —— 对耳机里听到的大小毫无作用。
            // 这里给一个真正有效的音量，省得用户以为电脑坏了。
            _slVolume = new ModernSlider
            {
                Label = "输出音量",
                Min = 0, Max = 100, Step = 1,
                LabelWidth = 104f,
                Format = v => v + "%"
            };
            _slVolume.SetValueSilent(100);
            _slVolume.ValueChanged += v =>
            {
                if (_syncing) return;
                try { var d = SelectedOutput(); if (d != null) d.AudioEndpointVolume.MasterVolumeLevelScalar = v / 100f; } catch { }
                ScheduleSave();
            };
            _cardDevice.Controls.Add(_slVolume);

            // ================= ③ 音效参数 =================
            _cardParam = new GlassCard { Title = "音效参数", Hint = "拖动即时生效" };
            Controls.Add(_cardParam);
            AddSlider(_cardParam, 0, 0, "upmix", "上混强度", 0, 100, (int)(_settings.UpmixStrength * 100),
                v => v + "%", v => _settings.UpmixStrength = v / 100f);
            AddSlider(_cardParam, 0, 1, "ext", "外部化 / 空间感", 0, 100, (int)(_settings.Externalize * 100),
                v => v + "%", v => _settings.Externalize = v / 100f);
            AddSlider(_cardParam, 0, 2, "width", "声场宽度", 50, 160, (int)(_settings.Width * 100),
                v => v + "%", v => _settings.Width = v / 100f);
            AddSlider(_cardParam, 0, 3, "bass", "低音管理强度", 0, 100, (int)(_settings.BassManagement * 100),
                v => v + "%", v => _settings.BassManagement = v / 100f);
            AddSlider(_cardParam, 1, 0, "lfe", "低音电平 (LFE)", -120, 120, (int)(_settings.BassBoostDb * 10),
                DbFmt, v => _settings.BassBoostDb = v / 10f);
            AddSlider(_cardParam, 1, 1, "center", "中置电平", -120, 120, (int)(_settings.CenterLevelDb * 10),
                DbFmt, v => _settings.CenterLevelDb = v / 10f);
            AddSlider(_cardParam, 1, 2, "surr", "环绕电平", -120, 120, (int)(_settings.SurroundLevelDb * 10),
                DbFmt, v => _settings.SurroundLevelDb = v / 10f);
            AddSlider(_cardParam, 1, 3, "out", "输出增益", -120, 120, (int)(_settings.OutputGainDb * 10),
                DbFmt, v => _settings.OutputGainDb = v / 10f);

            // ================= ④ 7.1 声场 =================
            _cardField = new GlassCard { Title = "7.1 声场", Hint = "点击音箱单独试听该声道" };
            Controls.Add(_cardField);
            _lblNowPlaying = new PlainText { FontPx = 12f };
            _cardField.Controls.Add(_lblNowPlaying);
            _layout = new TargetPanel();
            _layout.ChannelClicked += ch => SpeakTest(ch);
            _cardField.Controls.Add(_layout);

            // ================= ⑤ 均衡器 =================
            _cardEq = new GlassCard { Title = "均衡器", Hint = "六段参数均衡" };
            Controls.Add(_cardEq);

            _lblEqPreset = new PlainText { LabelText = "预设", FontPx = 11.5f };
            _cardEq.Controls.Add(_lblEqPreset);
            _cmbEqPreset = new GlassCombo();
            _cmbEqPreset.SetItems(EqPresets.All.Select(p => p.Name), 0);
            _cmbEqPreset.SelectedIndexChanged += (s, e) => { if (!_syncing) ApplyEqPreset(_cmbEqPreset.SelectedIndex); };
            _cardEq.Controls.Add(_cmbEqPreset);

            _lblEqEnable = new PlainText { LabelText = "启用", FontPx = 11.5f };
            _cardEq.Controls.Add(_lblEqEnable);
            _swEqEnable = new ToggleSwitch();
            _swEqEnable.CheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _settings.EqEnabled = _swEqEnable.Checked;
                BumpEq();
            };
            _cardEq.Controls.Add(_swEqEnable);

            _eqCurve = new EqCurvePanel { Settings = _settings, Rate = 48000 };
            _eqCurve.Changed += () => BumpEq();
            _eqCurve.BandPicked += b => SyncEqBandRow(b);
            _cardEq.Controls.Add(_eqCurve);

            _eqPreamp = new VSlider
            {
                Caption = "总增益",
                Min = -(int)(EqBands.PreampMaxDb * 10),
                Max = (int)(EqBands.PreampMaxDb * 10),
                Step = 5
            };
            _eqPreamp.ValueChanged += v =>
            {
                if (_syncing) return;
                _settings.EqPreampDb = v / 10f;
                BumpEq();
            };
            _cardEq.Controls.Add(_eqPreamp);

            for (int b = 0; b < EqBands.Count; b++)
            {
                int bb = b;
                var tc = new GlassCombo();
                tc.SetItems(new[] { "低架", "峰值", "高架", "带阻" }, _settings.EqType[bb]);
                tc.SelectedIndexChanged += (s, e) =>
                {
                    if (_syncing) return;
                    _settings.EqType[bb] = tc.SelectedIndex;
                    BumpEq();
                };
                _eqType[bb] = tc;
                _cardEq.Controls.Add(tc);

                var nf = new NumField
                {
                    Min = EqBands.MinFreq[bb],
                    Max = EqBands.MaxFreq[bb],
                    Step = 1,
                    Format = f => EqCurvePanel.FmtFreq(f)
                };
                nf.SetValueSilent(_settings.EqFreq[bb]);
                nf.ValueChanged += v =>
                {
                    if (_syncing) return;
                    _settings.EqFreq[bb] = v;
                    BumpEq();
                };
                _eqFreq[bb] = nf;
                _cardEq.Controls.Add(nf);

                var rl = new PlainText
                {
                    LabelText = $"{HzText(EqBands.MinFreq[bb])} – {HzText(EqBands.MaxFreq[bb])} Hz",
                    FontPx = 9.5f,
                    Align = ContentAlignment.MiddleCenter
                };
                _eqRange[bb] = rl;
                _cardEq.Controls.Add(rl);
            }

            // ================= 底部操作条 =================
            _btnPreview = new PillButton();
            _btnPreview.SetPrimary(false, "试听演示曲", GlyphKind.Play);
            _btnPreview.Clicked += (s, e) => TogglePreview();
            Controls.Add(_btnPreview);

            _btnFile = new PillButton();
            _btnFile.SetPrimary(false, "播放文件", GlyphKind.Folder);
            _btnFile.Clicked += (s, e) => ToggleFile();
            Controls.Add(_btnFile);

            _btnChannelTest = new PillButton();
            _btnChannelTest.SetPrimary(false, "声道测试", GlyphKind.Wave);
            _btnChannelTest.Clicked += (s, e) => ToggleChannelTest();
            Controls.Add(_btnChannelTest);

            _btnLatch = new PillButton { Toggle = true };
            _btnLatch.SetPrimary(false, "延迟更低", GlyphKind.Gear);
            _btnLatch.Clicked += (s, e) => { _btnLatch.SetChecked(!_btnLatch.Checked); ScheduleSave(); };
            Controls.Add(_btnLatch);

            _btnRestore = new PillButton();
            _btnRestore.SetPrimary(false, "恢复默认设备", GlyphKind.Reset);
            _btnRestore.Clicked += (s, e) => RestoreDefault();
            Controls.Add(_btnRestore);

            _btnGlobal = new PillButton();
            _btnGlobal.SetPrimary(true, "开始全局环绕", GlyphKind.Power);
            _btnGlobal.Clicked += (s, e) => ToggleGlobal();
            Controls.Add(_btnGlobal);

            // 主按钮旁边的小字：把"默认设备会被程序接管"这件事说在点之前，而不是等出问题
            _lblAutoHint = new PlainText { FontPx = 11f, Align = ContentAlignment.MiddleLeft };
            Controls.Add(_lblAutoHint);

            // ================= 页脚 =================
            _lblStatus = new PlainText { FontPx = 12.5f };
            Controls.Add(_lblStatus);

            // ================= 隐藏的均衡器说明书 =================
            // 平时不可见；在「7.1 声场」卡片上连点 3 下才出现，点任意位置收起
            _eqHelp = new EqHelpPanel();
            _eqHelp.CloseRequested = () => SetEqHelp(false);
            _eqHelp.Visible = false;
            Controls.Add(_eqHelp);
            _eqHelp.Visible = false;      // 加进控件树之后再压一次，避免父级可见性把它带成可见
            _layout.SecretGesture += () => SetEqHelp(!_eqHelpShown);
        }

        /// <summary>卡片里的一行标签。</summary>
        private static PlainText RowLabel(Control parent, string text)
        {
            var l = new PlainText { LabelText = text, FontPx = 12.5f };
            parent.Controls.Add(l);
            return l;
        }
        // =====================================================================
        // 窗口底：磨砂玻璃（柔和渐变 + 低透明度柔光光斑）。
        // 缓存成一张位图，重画时只贴图，避免缩放窗口时反复渲染背景导致卡顿。
        // =====================================================================
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var t = UiThemes.Current;
            if (_resizing)
            {
                // 拖动窗口大小期间只填纯色，停手后（_reflowTimer）再整体重排重画
                e.Graphics.Clear(t.Bg1);
                return;
            }
            if (_bgCache == null || _bgCache.Width != Math.Max(1, Width) || _bgCache.Height != Math.Max(1, Height))
                BuildBackground();
            if (_bgCache != null) e.Graphics.DrawImageUnscaled(_bgCache, 0, 0);
            else e.Graphics.Clear(t.Bg1);
        }

        private void BuildBackground()
        {
            int w = Math.Max(1, Width), h = Math.Max(1, Height);
            var t = UiThemes.Current;
            try { _bgCache?.Dispose(); } catch { }
            _bgCache = new Bitmap(w, h);
            using (var g = Graphics.FromImage(_bgCache))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, w, h), t.Bg1, t.Bg2, 95f))
                    g.FillRectangle(lg, 0, 0, w, h);
                foreach (var b in t.Blobs)
                    Blob(g, new RectangleF(b.x * w, b.y * h, b.w * w, b.h * h), b.c);
                DrawCardHalos(g, t);
            }
        }

        /// <summary>
        /// 卡片四面对称柔光：从卡片四边各探出一圈慢慢变淡的"晕"（1.5→6 设计单位）。
        /// 画在背景层、四边厚度完全一致 —— 卡片的四个圆角外围明暗才一样。
        /// （以前这层投影画在 GlassCard 自己身上，只会往下掉且被控件底边裁掉一半，
        ///  两个下角就比两个上角多一圈灰，看着像圆角不一样。）
        /// 卡片间距 14 设计单位，左右各探 6.5 也碰不到相邻的卡。
        /// </summary>
        private void DrawCardHalos(Graphics g, UiTheme t)
        {
            if (_cardDevice == null) return;
            float u = _uiScale <= 0.01f ? 1f : _uiScale;
            foreach (var card in new Control[] { _cardMode, _cardDevice, _cardParam, _cardField, _cardEq })
            {
                if (card == null || !card.Visible || card.Width < 8 || card.Height < 8) continue;
                for (int i = 0; i < 4; i++)
                {
                    float ex = (1.5f + i * 1.5f) * u;                 // 1.5/3/4.5/6 设计单位（换算成设备像素）
                    byte a = (byte)Math.Max(0, (t.Shadow.A / 5) * (4 - i));
                    if (a == 0) continue;
                    // 卡片主体比控件边界少 1 个设计单位，光晕从主体外 0.5 个单位起步
                    var r = new RectangleF(card.Left - ex + u, card.Top - ex + u,
                                           card.Width + ex * 2 - u * 2, card.Height + ex * 2 - u * 2);
                    using (var b = new SolidBrush(Color.FromArgb(a, t.Shadow)))
                        Shaper.FillRound(g, b, r, (22f + ex / u) * u);
                }
            }
        }

        /// <summary>一团柔光（中心有颜色、边缘完全透明）。</summary>
        private static void Blob(Graphics g, RectangleF r, Color c)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = c;
                    pgb.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillPath(pgb, path);
                }
            }
        }

        // =====================================================================
        // 设置持久化：改动后防抖落盘；退出时立即落盘
        // =====================================================================
        private void ScheduleSave()
        {
            if (_syncing || _refreshing) return;
            try { _saveTimer.Stop(); _saveTimer.Start(); } catch { }
        }

        private void SavePersist()
        {
            try
            {
                _persist.OutputId = SelectedOutput()?.ID;
                _persist.CaptureId = SelectedCapture()?.ID;
                _persist.PresetIndex = _cmbPreset?.SelectedIndex ?? -1;
                _persist.MuteSrc = _swMuteSrc?.Checked;
                _persist.AutoSwitch = _swAutoSwitch?.Checked;
                _persist.Latch = _btnLatch != null && _btnLatch.Checked;
                _persist.Audio = _settings?.Clone();
                _persist.HaveAudio = _persist.Audio != null;
                SettingsStore.Save(_persist);
            }
            catch { }
        }

        // =====================================================================
        // 「开始全局环绕」按钮：绿色 = 启动，红色 = 停
        // =====================================================================
        private void UpdateGlobalButton(bool running)
        {
            try
            {
                if (_trayGlobal != null) _trayGlobal.Text = running ? "停止全局环绕" : "开始全局环绕";
                if (_tray != null)
                {
                    _tray.Text = running ? "PC 7.1 环绕声（全局运行中）" : "PC 7.1 环绕声";
                    _tray.Icon = MakeTrayIcon(running);   // 右下角绿点：收进托盘也能一眼看出在跑
                }
            }
            catch { }
            if (_btnGlobal == null || _globalBtnRunning == running) return;
            _globalBtnRunning = running;
            _btnGlobal.SetPrimary(true, running ? "停止全局环绕" : "开始全局环绕", running ? GlyphKind.Stop : GlyphKind.Power);
            _btnGlobal.AccentOverride = running ? UiThemes.Current.Danger : (Color?)null;
            // 只有一个输出设备又没装虚拟声卡时，"全局环绕"是无解的 —— 置灰，别让人对着点不动的按钮发愁
            _btnGlobal.Enabled = running || CanRunGlobal();
            _btnGlobal.Invalidate();
        }

        // =====================================================================
        // 参数区滑杆（两列）：标签 + 细轨道 + 圆形手柄 + 数值胶囊
        // =====================================================================
        private static readonly Func<int, string> DbFmt = v => (v / 10f).ToString("0.0") + " dB";

        private void AddSlider(Control parent, int col, int row, string key, string label,
                               int min, int max, int value, Func<int, string> fmt, Action<int> onChange)
        {
            var sl = new ModernSlider
            {
                Label = label,
                Min = min,
                Max = max,
                Step = 1,
                LabelWidth = 104f,
                Format = fmt
            };
            sl.SetValueSilent(Math.Max(min, Math.Min(max, value)));
            sl.ValueChanged += v => { if (_syncing) return; try { onChange(v); } catch { } ScheduleSave(); };
            parent.Controls.Add(sl);
            _sliders[key] = sl;
            _sliderPos[key] = (col, row);
        }

        // =====================================================================
        // DPI / 分辨率自适应
        //   · 界面完全按"设计单位"排（1200×870），最后统一乘 _uiScale
        //   · _uiScale = 系统DPI × 窗口缩放系数（窗口比设计尺寸小时<1，保证全部可见）
        //   · 不再有任何累积缩放，窗口怎么拖都不会漂
        // =====================================================================
        private bool _dpiScaled;

        // ---------------- 全局热键：Ctrl+Alt+7 开 / 关全局环绕 ----------------
        private const int HotKeyId = 0x5171;         // 随便挑的内部 ID，只要在本进程内唯一
        private const uint WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt64() == HotKeyId)
            {
                ToggleGlobal();   // 游戏里不用切窗口就能开关环绕
                return;
            }
            base.WndProc(ref m);
        }


        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotKey(Handle, HotKeyId, MOD_CONTROL | MOD_ALT, 0x37);   // Ctrl+Alt+7
            if (_dpiScaled) return;
            _dpiScaled = true;

            float s = DeviceDpi / 96f;
            if (s <= 1.001f)
            {
                // SystemAware 下 DeviceDpi 可能不准，再问一次屏幕
                try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f; } catch { }
            }
            _dpi = s > 0.01f ? s : 1f;
            _uiScale = _dpi;

            ClientSize = new Size(Rd(DW * _dpi), Rd(DH * _dpi));
            if (ForcedClientSize.HasValue) ClientSize = ForcedClientSize.Value;   // 调试用：--size 宽x高
            MinimumSize = new Size(Rd(700 * _dpi), Rd(500 * _dpi));
            CenterOnScreen();
            if (_layoutReady) Reflow();
        }

        /// <summary>句柄销毁（退出 / DPI 变化重建）时注销热键，句柄重建后 OnHandleCreated 会再注册一次。</summary>
        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { UnregisterHotKey(Handle, HotKeyId); } catch { }
            base.OnHandleDestroyed(e);
        }

        private void CenterOnScreen()
        {
            try
            {
                var wa = Screen.FromControl(this).WorkingArea;
                // 屏幕装不下就缩小到工作区内（全局环绕窗口本身仍然可用）
                if (Width > wa.Width || Height > wa.Height)
                    ClientSize = new Size(Math.Min(ClientSize.Width, wa.Width - 40),
                                          Math.Min(ClientSize.Height, wa.Height - 80));
                Location = new Point(wa.Left + Math.Max(0, (wa.Width - Width) / 2),
                                     wa.Top + Math.Max(0, (wa.Height - Height) / 2));
            }
            catch { }
        }

        private static int Rd(float v) => (int)Math.Round(v);

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            if (!_layoutReady) return;
            // 拖动期间只重画纯色底；停手 90ms 后再重排，避免每像素都重排导致界面卡死
            _resizing = true;
            try { _reflowTimer.Stop(); _reflowTimer.Start(); } catch { }
        }

        private void Reflow()
        {
            if (!_layoutReady || _inRelayout) return;
            float dw = DW * _dpi, dh = DH * _dpi;
            float zoom = Math.Min(1.6f, Math.Min(ClientSize.Width / Math.Max(1f, dw),
                                                  ClientSize.Height / Math.Max(1f, dh)));
            zoom = Math.Max(0.40f, zoom);
            _zoom = zoom;
            _uiScale = _dpi * zoom;
            Relayout();
        }

        /// <summary>设计单位 → 实际像素。</summary>
        private int SRf(float v) => (int)Math.Round(v * _uiScale);

        private void Place(Control c, float x, float y, float w, float h)
        {
            if (c == null) return;
            c.SetBounds(SRf(x), SRf(y), Math.Max(1, SRf(w)), Math.Max(1, SRf(h)));
            if (c is IUiScaled us) us.SetUiScale(_uiScale);
        }

        // =====================================================================
        // 重排：两列卡片 + 底部操作条 + 页脚
        // =====================================================================
        private void Relayout()
        {
            if (!_layoutReady) return;
            if (_cardMode == null || _cardDevice == null || _cardParam == null || _cardField == null || _cardEq == null) return;
            _inRelayout = true;
            try
            {
                float u = _uiScale <= 0.01f ? 1f : _uiScale;
                float cw = ClientSize.Width / u;                  // 当前窗口尺寸换算成设计单位
                float ch = ClientSize.Height / u;
                float extraW = Math.Max(0f, cw - DW);
                float extraH = Math.Max(0f, ch - DH);

                const float m = 16f, gap = 14f, top = 14f;
                float leftW = 640f + extraW * 0.55f;
                float rightX = m + leftW + gap;
                float rightW = Math.Max(300f, cw - rightX - m);

                bool showModeCard = _hasSurroundOut;
                if (_cardMode != null) _cardMode.Visible = showModeCard;
                const float modeH = 104f;
                float modeH2 = showModeCard ? modeH : 0f;

                // 隐藏"输出模式"卡片后省出来的高度，要还给设备卡 / 参数卡（右列的声场 / EQ 卡共用
                // 这两个增量），否则底部操作条会上移、窗口底部空出一大块 —— 上紧下松。
                // 省的不只是 104 的卡高，还有它与设备卡之间那 14 的间隙；同时隐藏时左列第一张
                // 卡的起点回归 top（与右列声场卡顶边对齐），别再低 14 个单位"探出头"。
                float reclaim = modeH - modeH2 + (showModeCard ? 0f : gap);
                float devExtra = extraH * 0.30f + reclaim * 0.35f;
                float paramExtra = extraH * 0.70f + reclaim * 0.65f;
                float devH = 264f + devExtra;
                float paramH = 256f + paramExtra;
                // 右列第一张卡（声场）与左列第一张卡（设备）同高：两列的"上边 + 中间分界线"
                // 完全对齐，右边 2 张和左边 2 张四个区域才对得整齐
                float fieldH = devH;

                float devY = showModeCard ? top + modeH2 + gap : top;
                float paramY = devY + devH + gap;
                float bottom = paramY + paramH;
                // EQ 卡高度 = 窗口底部减掉它的起点，与左列参数卡同一直到底（内部排版按这个
                // 实际高度反推，见下面 curveH）—— 以前按固定 356 排版，卡片一被 reclaim 撑高
                // 内容就冒出卡外（曲线面板下沿、六个波段框挂在卡边外面）
                float eqCardH = Math.Max(240f, bottom - (top + fieldH + gap));
                float actionH = 50f;
                float actionY = bottom + gap;
                float footY = actionY + actionH + 8f;

                // ---------------- 卡片 ----------------
                Place(_cardMode, m, top, leftW, modeH2);
                Place(_cardDevice, m, devY, leftW, devH);
                Place(_cardParam, m, paramY, leftW, paramH);
                Place(_cardField, rightX, top, rightW, fieldH);
                Place(_cardEq, rightX, top + fieldH + gap, rightW, eqCardH);

                // ---------------- ① 输出模式 ----------------
                // 没有 8 声道输出端点时，"7.1 音箱直出"落不了地：只留耳机那一张，占满整行，
                // 别再摆一个点不动的选项让人犹豫
                bool showSpeakerMode = _hasSurroundOut;
                if (_optSpeakers != null) _optSpeakers.Visible = showSpeakerMode;
                float ow = showSpeakerMode ? (leftW - 36f - 12f) / 2f : leftW - 36f;
                Place(_optHeadphone, 18f, 32f, ow, 64f);
                Place(_optSpeakers, 18f + ow + 12f, 32f, ow, 64f);

                // ---------------- ② 设备 ----------------
                // "采集方式"这一行只在保留虚拟声卡时才出现；删掉这条路的机器上整行拿掉，
                // 剩下的 4 行重新均分高度，别在中间留一条莫名其妙的空白。
                bool showKind = AllowCable && HasVirtualCable();
                if (_lblCapKind != null) _lblCapKind.Visible = showKind;
                if (_segCapture != null) _segCapture.Visible = showKind;

                float dpitch = Math.Max(38f, (devH - 50f) / ((showKind ? 6f : 5f) + 1f));   // +1 = 输出音量行
                const float rowH = 32f, ry = 38f;
                float rx2 = 112f, rw2 = leftW - 112f - 18f;
                int row = 0;
                Place(_lblOutDev, 18f, ry, 86f, rowH);
                Place(_cmbOutput, rx2, ry, rw2, rowH);
                if (showKind)
                {
                    row++;
                    Place(_lblCapKind, 18f, ry + dpitch * row, 86f, rowH);
                    Place(_segCapture, rx2, ry + dpitch * row, Math.Min(300f, rw2), rowH);
                }
                row++;
                Place(_lblCapDev, 18f, ry + dpitch * row, 86f, rowH);
                Place(_cmbCapture, rx2, ry + dpitch * row, rw2, rowH);
                row++;
                Place(_lblPreset, 18f, ry + dpitch * row, 86f, rowH);
                Place(_cmbPreset, rx2, ry + dpitch * row, rw2, rowH);
                row++;
                Place(_lblMuteSrc, 18f, ry + dpitch * row + 3f, leftW - 100f, 26f);
                Place(_swMuteSrc, leftW - 18f - 46f, ry + dpitch * row + 3f, 46f, 26f);
                row++;
                Place(_lblAutoSwitch, 18f, ry + dpitch * row + 3f, leftW - 100f, 26f);
                Place(_swAutoSwitch, leftW - 18f - 46f, ry + dpitch * row + 3f, 46f, 26f);
                row++;
                Place(_slVolume, 18f, ry + dpitch * row - 2f, leftW - 36f, rowH + 4f);

                // ---------------- ③ 音效参数 ----------------
                float colW = (leftW - 36f - 16f) / 2f;
                float spitch = Math.Max(40f, (paramH - 52f) / 4f);
                foreach (var kv in _sliderPos)
                {
                    if (!_sliders.TryGetValue(kv.Key, out var sl)) continue;
                    Place(sl, 18f + kv.Value.col * (colW + 16f), 38f + kv.Value.row * spitch, colW, 30f);
                }

                // ---------------- ④ 7.1 声场 ----------------
                Place(_lblNowPlaying, 18f, 30f, rightW - 36f, 22f);
                Place(_layout, 12f, 56f, rightW - 24f, Math.Max(110f, fieldH - 70f));

                // ---------------- ⑤ 均衡器 ----------------
                float togR = rightW - 18f - 46f;
                Place(_swEqEnable, togR, 9f, 46f, 26f);
                Place(_lblEqEnable, togR - 8f - 40f, 9f, 40f, 26f);
                float cmbR = togR - 8f - 40f - 12f;
                Place(_cmbEqPreset, cmbR - 150f, 8f, 150f, 28f);
                Place(_lblEqPreset, cmbR - 150f - 12f - 34f, 9f, 34f, 26f);

                float preW = 60f;
                float curveW = Math.Max(160f, rightW - 18f - 18f - 12f - preW);
                // 曲线高度按 EQ 卡的实际高度反推：标题行 42 + 间隙 8 + 波段栈 84 + 底部 14，
                // 扣掉的就是曲线面板的高度 —— 六个波段框整整齐齐收在卡里，不会冒出卡外
                float curveH = Math.Max(90f, eqCardH - 42f - 8f - 84f - 14f);
                float curveY = 42f;
                Place(_eqCurve, 18f, curveY, curveW, curveH);
                Place(_eqPreamp, 18f + curveW + 12f, curveY, preW, curveH);

                float bandY = curveY + curveH + 8f;
                float bcw = (rightW - 36f - 40f) / EqBands.Count;
                for (int b = 0; b < EqBands.Count; b++)
                {
                    float bx = 18f + b * (bcw + 8f);
                    Place(_eqType[b], bx, bandY, bcw, 26f);
                    Place(_eqFreq[b], bx, bandY + 30f, bcw, 32f);
                    Place(_eqRange[b], bx, bandY + 64f, bcw, 16f);
                }

                // ---------------- 底部操作条 ----------------
                float bx2 = m;
                Place(_btnPreview, bx2, actionY, 148f, actionH); bx2 += 158f;
                Place(_btnFile, bx2, actionY, 138f, actionH); bx2 += 148f;
                Place(_btnChannelTest, bx2, actionY, 138f, actionH); bx2 += 148f;
                Place(_btnLatch, bx2, actionY, 148f, actionH);
                Place(_btnGlobal, cw - m - 178f, actionY, 178f, actionH);
                Place(_btnRestore, cw - m - 178f - 10f - 156f, actionY, 156f, actionH);

                // 中间那段空位放一句提示：说清楚"点开始之后默认设备会被程序接管"
                float hintX = bx2 + 148f + 12f;
                float hintW = (cw - m - 178f - 10f - 156f - 10f) - hintX;
                Place(_lblAutoHint, hintX, actionY, Math.Max(0f, hintW), actionH);

                // ---------------- 页脚 ----------------
                // 主题按钮已去除（只有白色一套），状态栏用满整行
                Place(_lblStatus, m, footY, Math.Max(80f, cw - m * 2f), 22f);

                // 均衡器说明书：铺满整个客户区（浮层，平时隐藏）
                if (_eqHelp != null)
                {
                    _eqHelp.SetUiScale(u);
                    _eqHelp.Bounds = OverlayBounds();
                    _eqHelp.Visible = _eqHelpShown;
                }

                UpdateAutoHint();
            }
            finally
            {
                _inRelayout = false;
                // 卡片位置/可见性变了：背景缓存里的对称柔光要重画（下次 Background 重建）
                _bgCache = null;
            }
        }

        // 设备
        // =====================================================================
        private void LoadDevices()
        {
            EnumDevices();
            RefreshLists();
            FillPresetCombo();
            try
            {
                var def = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _savedDefaultId = def.ID;
                _savedDefaultName = SafeName(def);
            }
            catch { }
            // 没装虚拟声卡时，"虚拟声卡"这条路根本走不通 —— 直接把采集方式切到系统环回，
            // 用户打开程序看到的第一个状态就是能用的状态，而不是"麦克风阵列"这种莫名其妙的东西
            if (!HasVirtualCable() && _renderDevices.Count >= 2 && _segCapture != null && _segCapture.SelectedIndex != 1)
                _segCapture.SelectedIndex = 1;
            // 首次加载就把设备配好（输出=耳机、环回来源=另一个），打开就能直接点开始
            // 本机没有 8 声道端点时，强制回到耳机模式 —— 音箱直出这条路等于不存在
            if (!_hasSurroundOut) { _settings.HeadphoneMode = true; SyncModeCards(); }
            AutoPairDevices(false);
        }

        /// <summary>机器上有没有虚拟声卡（VB-Cable 之类）。没有就只能走系统环回。</summary>
        private bool HasVirtualCable()
        {
            if (!AllowCable) return false;      // 这条路已经在产品里删掉了，一律按"没有"处理
            foreach (var d in _captureDevices)
                if (SafeName(d).IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var d in _renderDevices)
                if (SafeName(d).IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>全局环绕能不能跑：要么有虚拟声卡，要么有两个输出端点（一个抓、一个听）。</summary>
        private bool CanRunGlobal() => HasVirtualCable() || _renderDevices.Count >= 2;

        /// <summary>重建下拉框，尽量保留用户当前选择的设备；用于设备插拔后的自动刷新。</summary>
        private void RefreshLists()
        {
            // 第一次填充时按上次运行的设备 ID 还原（存的是端点 ID，插拔后索引会错位）
            bool first = _firstDeviceFill;
            _firstDeviceFill = false;
            string outId = first ? _persist.OutputId : SelectedOutput()?.ID;
            bool loopback = CaptureIsLoopback;
            string capId = first ? _persist.CaptureId : SelectedCapture()?.ID;
            _refreshing = true;
            try
            {
                FillOutputCombo(outId);
                FillCaptureCombo(capId, loopback);
                _hasSurroundOut = DetectSurroundOutput();
                VolumeSyncFromDevice();
            }
            finally { _refreshing = false; }
        }

        /// <summary>
        /// 本机有没有能直出 8 声道的输出端点（HDMI 接功放 / 独立 7.1 声卡）。
        /// 没有的话"7.1 音箱直出"就是个摆设 —— 选了也只能往 2 声道设备上硬塞，界面里就不该出现。
        /// 只在设备列表刷新时算一次：读 MixFormat 要激活 AudioClient，放进每次重排里会卡。
        /// </summary>
        private bool DetectSurroundOutput()
        {
            foreach (var d in _renderDevices)
                if (TryChannels(d) >= 8) return true;
            return false;
        }

        /// <summary>设备变动通知到达（UI 线程）：稍微去抖一下再刷新，避免短时间内反复重建下拉框。</summary>
        internal void NotifyDeviceChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { _deviceTimer.Stop(); _deviceTimer.Start(); } catch { }
        }

        /// <summary>重新枚举设备并刷新下拉框；保留当前选择，也不打断正在进行的播放。</summary>
        private void RefreshDevices()
        {
            EnumDevices();
            RefreshLists();
            CheckRunningEndpoints();   // 正在用的端点是不是被拔掉了
            GuardDefaultDevice();      // 默认播放设备是不是被系统抢走了
        }

        private void EnumDevices()
        {
            _renderDevices.Clear();
            _captureDevices.Clear();
            try
            {
                foreach (var d in _en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    if (Usable(d)) _renderDevices.Add(d);
            }
            catch { }
            try
            {
                foreach (var d in _en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    if (Usable(d)) _captureDevices.Add(d);
            }
            catch { }
        }

        /// <summary>
        /// 端点是否可用。驱动被卸载后 Windows 常留下"幽灵端点"——还能枚举到、连波形格式都能取到，
        /// 但属性存储读不出名字（错误 0xE000020B），真去Started 会立刻失败。这种直接从列表里剔除，
        /// 免得被默认选中然后报一个看不懂的错误。
        /// </summary>
        private static bool Usable(MMDevice d)
        {
            try { if (!string.IsNullOrWhiteSpace(d.FriendlyName)) return true; } catch { }
            try { if (!string.IsNullOrWhiteSpace(d.DeviceFriendlyName)) return true; } catch { }
            return false;
        }

        private static int IndexOfId(List<MMDevice> list, string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].ID, id, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private void FillOutputCombo(string preferId)
        {
            var items = new List<string>();
            for (int i = 0; i < _renderDevices.Count; i++)
                items.Add($"[{i}] {SafeName(_renderDevices[i])}  ({TryChannels(_renderDevices[i])} 声道)");

            // 刷新时优先还原之前的选择
            int sel = IndexOfId(_renderDevices, preferId);
            if (sel < 0)
            {
                // 默认选一个"不是虚拟声卡"的真实输出；虚拟声卡作为输出没有意义
                sel = 0;
                bool eightChannel = false;
                for (int i = 0; i < _renderDevices.Count; i++)
                {
                    if (SafeName(_renderDevices[i]).Contains("CABLE")) continue;
                    int ch = TryChannels(_renderDevices[i]);
                    if (ch >= 8 && !eightChannel) { sel = i; eightChannel = true; }
                    else if (!eightChannel && sel == 0) sel = i;
                }
            }
            if (items.Count == 0) { _cmbOutput.SetItems(items, -1); return; }
            _cmbOutput.SetItems(items, Math.Max(0, Math.Min(items.Count - 1, sel)));
        }

        private static int TryChannels(MMDevice d)
        {
            try { return d.AudioClient.MixFormat.Channels; } catch { return 0; }
        }

        /// <summary>根据"采集方式"填充采集设备下拉框。</summary>
        private void FillCaptureCombo(string preferId, bool loopback)
        {
            var list = loopback ? _renderDevices : _captureDevices;
            var items = new List<string>();
            for (int i = 0; i < list.Count; i++)
                items.Add($"[{i}] {SafeName(list[i])}  ({TryChannels(list[i])} 声道)");

            int sel = IndexOfId(list, preferId);
            if (sel < 0)
            {
                sel = 0;
                if (loopback)
                {
                    // 环回：默认抓系统默认播放设备
                    try
                    {
                        var def = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        for (int i = 0; i < _renderDevices.Count; i++)
                            if (string.Equals(_renderDevices[i].ID, def.ID, StringComparison.OrdinalIgnoreCase)) { sel = i; break; }
                    }
                    catch { }
                }
                else
                {
                    // 虚拟声卡：默认选 CABLE 的采集端
                    for (int i = 0; i < _captureDevices.Count; i++)
                        if (SafeName(_captureDevices[i]).Contains("CABLE")) { sel = i; break; }
                }
            }
            if (items.Count == 0) { _cmbCapture.SetItems(items, -1); UpdateCaptureUi(loopback); return; }
            _cmbCapture.SetItems(items, Math.Max(0, Math.Min(items.Count - 1, sel)));
            UpdateCaptureUi(loopback);
        }

        private bool CaptureIsLoopback => !AllowCable || (_segCapture != null && _segCapture.SelectedIndex == 1);

        /// <summary>音量滑杆 ↔ 输出端点：换设备 / 启动时把滑杆同步成端点当前音量。</summary>
        private void VolumeSyncFromDevice()
        {
            if (_slVolume == null) return;
            var dev = SelectedOutput();
            if (dev == null) return;
            try
            {
                _syncing = true;
                _slVolume.SetValueSilent((int)Math.Round(dev.AudioEndpointVolume.MasterVolumeLevelScalar * 100f));
            }
            catch { }
            finally { _syncing = false; }
        }

        private void UpdateCaptureUi(bool loopback)
        {
            if (!AllowCable) loopback = true;   // 没有"采集方式"可选了，只有环回
            if (_lblCapDev != null) _lblCapDev.LabelText = loopback ? "环回来源" : "采集设备";
            // 两条路都要"接管默认播放设备"：环回切到环回来源，虚拟声卡切到 CABLE Input
            if (_lblAutoSwitch != null)
                _lblAutoSwitch.LabelText = loopback ? "启动时自动接管默认播放设备" : "启动时自动切到虚拟声卡输入";
            UpdateAutoHint();
        }

        private void FillPresetCombo()
        {
            var all = SurroundPresets.All();
            var items = new List<string>();
            foreach (var p in all) items.Add(p.Name + "  —  " + p.Description);
            // 还原上次用的预设；存的索引越界（预设列表变了）就回到默认的电影环绕
            int pIdx = _persist.PresetIndex;
            _cmbPreset.SetItems(items, pIdx >= 0 && pIdx < items.Count ? pIdx : 0);
        }

        private void OnCaptureKindChanged()
        {
            if (_cmbCapture == null || _refreshing) return;
            bool loopback = CaptureIsLoopback;
            bool wasRunning = _preview != null || _engine != null;   // 看对象，不看 IsRunning（同 StopAll）
            if (wasRunning) StopAll();
            _cmbCapture.SetItems(new List<string>(), -1);
            FillCaptureCombo(null, loopback);
            // 切到系统环回时，顺手把"输出=耳机、环回来源=另一个"配好，用户不用自己挑两个框
            if (loopback) AutoPairDevices(false);
        }

        private static string SafeName(MMDevice d)
        {
            try { var n = d.FriendlyName; if (!string.IsNullOrWhiteSpace(n)) return n; } catch { }
            try { var n = d.DeviceFriendlyName; if (!string.IsNullOrWhiteSpace(n)) return n; } catch { }
            return "(设备名不可用)";
        }

        private MMDevice SelectedOutput()
        {
            int i = _cmbOutput.SelectedIndex;
            if (i < 0 || i >= _renderDevices.Count) return null;
            return _renderDevices[i];
        }

        private MMDevice SelectedCapture()
        {
            int i = _cmbCapture.SelectedIndex;
            if (i < 0) return null;
            var list = CaptureIsLoopback ? _renderDevices : _captureDevices;
            if (i >= list.Count) return null;
            return list[i];
        }

        private void OnOutputChanged()
        {
            if (_cmbOutput == null || _refreshing) return;
            ScheduleSave();
            VolumeSyncFromDevice();   // 换输出设备后，音量滑杆跟着新端点走
            bool wasRunning = _preview != null || _engine != null;   // 看对象，不看 IsRunning（同 StopAll）
            if (wasRunning) StopAll();
        }

        /// <summary>切换"耳机虚拟 7.1 / 7.1 音箱直出"。</summary>
        private void SetHeadphoneMode(bool headphone)
        {
            if (!_hasSurroundOut) headphone = true;    // 没有 8 声道端点，切过去也没意义
            if (_settings.HeadphoneMode == headphone)
            {
                SyncModeCards();
                return;
            }
            bool wasRunning = _preview != null || _engine != null;   // 看对象，不看 IsRunning（同 StopAll）
            if (wasRunning) StopAll();
            _settings.HeadphoneMode = headphone;
            SyncModeCards();
        }

        private void SyncModeCards()
        {
            if (_optHeadphone == null || _optSpeakers == null) return;
            _optHeadphone.SetActive(_settings.HeadphoneMode);
            _optSpeakers.SetActive(!_settings.HeadphoneMode);
        }

        private void OnPresetChanged()
        {
            if (_refreshing || _syncing) return;
            int i = _cmbPreset.SelectedIndex;
            var all = SurroundPresets.All();
            if (i < 0 || i >= all.Length) return;

            // 预设一律"就地"改参数，不换 _settings 对象 —— 正在跑的 DSP 持有它的引用，
            // 换对象就得停下来重开才能生效。所以这里能一边播一边换预设，听感立刻变。
            var src = all[i];
            bool newHp = _settings.HeadphoneMode;                        // 预设默认不动输出模式
            if (i == all.Length - 1 && _hasSurroundOut) newHp = src.HeadphoneMode;   // 最后一个是"真实 7.1 直通"

            // 唯一必须重开的例外：输出模式跟着变了（双耳化 ⇄ 8 声道直出是两条不同的处理链）
            bool wasRunning = _preview != null || _engine != null;   // 看对象，不看 IsRunning（同 StopAll）
            if (wasRunning && newHp != _settings.HeadphoneMode) StopAll();

            _settings.CopyFrom(src);
            _settings.HeadphoneMode = newHp;
            _eqCurve.Settings = _settings;
            ApplySettingsToUi();
            ScheduleSave();
        }

        /// <summary>把这些设置值刷到界面上（不回写设置）。</summary>
        private void ApplySettingsToUi()
        {
            _syncing = true;
            try
            {
                SetSlider("upmix", (int)(_settings.UpmixStrength * 100));
                SetSlider("ext", (int)(_settings.Externalize * 100));
                SetSlider("width", (int)(_settings.Width * 100));
                SetSlider("bass", (int)(_settings.BassManagement * 100));
                SetSlider("lfe", (int)(_settings.BassBoostDb * 10));
                SetSlider("center", (int)(_settings.CenterLevelDb * 10));
                SetSlider("surr", (int)(_settings.SurroundLevelDb * 10));
                SetSlider("out", (int)(_settings.OutputGainDb * 10));
                SyncModeCards();

                // 均衡器
                if (_eqCurve != null) _eqCurve.Settings = _settings;
                if (_swEqEnable != null) _swEqEnable.Checked = _settings.EqEnabled;
                if (_eqPreamp != null) _eqPreamp.SetValueSilent((int)Math.Round(_settings.EqPreampDb * 10));
                for (int b = 0; b < EqBands.Count; b++) SyncEqBandRow(b);
                if (_cmbEqPreset != null)
                {
                    int pi = -1;
                    for (int i = 0; i < EqPresets.All.Length; i++)
                        if (EqPresetMatches(EqPresets.All[i])) { pi = i; break; }
                    if (pi >= 0) _cmbEqPreset.SelectedIndex = pi;
                }
                if (_eqCurve != null) _eqCurve.Invalidate();
            }
            finally
            {
                _syncing = false;
            }
        }

        private void SetSlider(string key, int value)
        {
            if (!_sliders.TryGetValue(key, out var sl)) return;
            sl.SetValueSilent(value);
        }

        // =====================================================================
        // 均衡器：界面 ↔ 设置
        // =====================================================================
        /// <summary>参数变了：版本号 +1，正在播放时 DSP 下一块就重算系数。</summary>
        private void BumpEq()
        {
            unchecked { _settings.EqVersion++; }
            if (_eqCurve != null) _eqCurve.Invalidate();
            ScheduleSave();
        }

        private void SyncEqBandRow(int b)
        {
            if (b < 0 || b >= EqBands.Count) return;
            if (_eqFreq[b] != null && _settings.EqFreq != null && b < _settings.EqFreq.Length)
                _eqFreq[b].SetValueSilent(_settings.EqFreq[b]);
            if (_eqType[b] != null && _settings.EqType != null && b < _settings.EqType.Length)
                _eqType[b].SelectedIndex = _settings.EqType[b];
        }

        private bool EqPresetMatches(EqPresets.Preset p)
        {
            if (p?.Gains == null || p.Gains.Length != EqBands.Count) return false;
            if (Math.Abs(p.PreampDb - _settings.EqPreampDb) > 0.06f) return false;
            for (int i = 0; i < EqBands.Count; i++)
                if (Math.Abs(p.Gains[i] - _settings.EqGains[i]) > 0.06f) return false;
            return true;
        }

        private void ApplyEqPreset(int index)
        {
            if (index < 0 || index >= EqPresets.All.Length) return;
            var p = EqPresets.All[index];
            for (int i = 0; i < EqBands.Count; i++)
            {
                _settings.EqGains[i] = i < p.Gains.Length ? p.Gains[i] : 0f;
                _settings.EqFreq[i] = EqBands.DefaultFreq[i];
                _settings.EqType[i] = (int)EqBands.DefaultType[i];
                _settings.EqBandOn[i] = true;
            }
            _settings.EqPreampDb = p.PreampDb;
            _syncing = true;
            try
            {
                if (_eqPreamp != null) _eqPreamp.SetValueSilent((int)Math.Round(p.PreampDb * 10));
                for (int i = 0; i < EqBands.Count; i++) SyncEqBandRow(i);
            }
            finally { _syncing = false; }
            BumpEq();
            if (_eqCurve != null)
            {
                _eqCurve.SelectedBand = -1;
                _eqCurve.Invalidate();
            }
        }

        // 试听 / 全局
        // =====================================================================
        private int Latency()
        {
            int baseMs = _btnLatch != null && _btnLatch.Checked ? 30 : 55;
            return baseMs;
        }

        /// <summary>
        /// 启动试听链（演示曲 / 文件 / 声道测试共用）。成功返回 true。
        /// 注意：里面第一件事就是 StopAll() —— 试听和全局环绕互斥，同一台设备上两路声音
        /// 会叠在一起，所以"开新的"必然先"停旧的"。
        /// </summary>
        private bool StartPreview(Func<int, ISampleProvider> factory, string what, PreviewKind kind)
        {
            StopAll();
            _stoppedNotice = null;      // 上一轮的"被系统中断"提示不该带进新一轮
            var dev = SelectedOutput();
            if (dev == null)
            {
                MessageBox.Show("请先选择一个输出设备。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                int rate = dev.AudioClient.MixFormat.SampleRate;
                var src = factory(rate);
                _currentSource = src;
                _preview = new SurroundPipeline(dev) { LatencyMs = Latency() };
                _preview.Start(src, _settings);
                _previewKind = kind;
                _previewStartedAt = Environment.TickCount64;
                _lblNowPlaying.LabelText = what;
                return true;
            }
            catch (Exception ex)
            {
                StopAll();
                MessageBox.Show("启动失败：\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static string HzText(float v) =>
            v >= 1000f ? (v / 1000f).ToString("0.#") + "K" : v.ToString("0");

        /// <summary>调试用：把日志追加到 %TEMP%\Surround71_dbg.log（只在带 --demo/--menutest/--traytest 时用）。</summary>
        private static void DbgLog(string s)
        {
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Surround71_dbg.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\r\n");
            }
            catch { }
        }

        /// <summary>调试用：控件在客户区里的位置/尺寸速查（--snap 时打进日志，供像素核对）。</summary>
        private static string Bb(Control c) => c == null ? "null" :
            $"{c.Left},{c.Top} {c.Width}x{c.Height} vis={c.Visible}";

        /// <summary>调试用：把菜单/弹层画成 PNG，方便核对样式（不依赖窗口是否在前台）。</summary>
        private static void DbgSnap(Control c, string tag)
        {
            try
            {
                if (c == null) { DbgLog(tag + " snap: null"); return; }
                c.PerformLayout();
                var size = c.Size;
                DbgLog(tag + " snap: size=" + size.Width + "x" + size.Height + " visible=" + c.Visible);
                if (size.Width < 4 || size.Height < 4) return;
                using (var b = new Bitmap(size.Width, size.Height))
                {
                    c.DrawToBitmap(b, new Rectangle(Point.Empty, size));
                    b.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dbg_" + tag + ".png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                    DbgLog(tag + " snap: saved");
                }
            }
            catch (Exception ex) { DbgLog(tag + " snap: " + ex.Message); }
        }

        private void TogglePreview()
        {
            // 只认"演示曲"这一路：正在放演示曲 → 停；否则（空闲 / 在放文件 / 在跑声道测试）→ 切到演示曲
            if (_previewKind == PreviewKind.Demo)
            {
                StopAll();
                return;
            }
            if (StartPreview(rate => new TestTuneProvider(rate), "试听：演示曲（经 7.1 环绕处理）", PreviewKind.Demo))
                _btnPreview.SetPrimary(false, "停止试听", GlyphKind.Stop);
        }

        private void ToggleFile()
        {
            // 再点一次"停止播放"就停，不弹文件框 —— 以前这里必然弹框，想停都停不掉
            if (_previewKind == PreviewKind.File)
            {
                StopAll();
                return;
            }
            using var ofd = new OpenFileDialog
            {
                Title = "选择音频文件",
                Filter = "音频文件|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.aiff;*.ogg|所有文件|*.*"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            FileLoopSource src = null;
            try
            {
                src = new FileLoopSource(ofd.FileName, true);
                if (StartPreview(_ => src, "试听：" + Path.GetFileName(ofd.FileName), PreviewKind.File))
                    _btnFile.SetPrimary(false, "停止播放", GlyphKind.Stop);
                else
                {
                    // 启动没成功（例如没选输出设备）：这个文件句柄得自己收掉，
                    // 否则每试一次就漏一个文件占用，用户会删不掉那个音频文件
                    src.Dispose();
                }
            }
            catch (Exception ex)
            {
                try { src?.Dispose(); } catch { }
                MessageBox.Show("无法播放该文件：\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleChannelTest()
        {
            if (_previewKind == PreviewKind.Channel)
            {
                StopAll();
                return;
            }
            if (StartPreview(rate =>
                {
                    var test = new ChannelTestProvider(rate);
                    _testSource = test;
                    return test;
                }, "声道测试：依次 FL → FR → FC → LFE → BL → BR → SL → SR", PreviewKind.Channel))
            {
                _btnChannelTest.SetPrimary(false, "停止声道测试", GlyphKind.Stop);
            }
        }

        private void SpeakTest(int channel)
        {
            // 声道测试已经在跑：只换"独奏哪个声道"，不重启链路（重启会有一声咔哒）。
            // 再点同一个音箱 = 取消独奏，回到自动遍历 —— 不用专门去点"停止声道测试"。
            if (_testSource != null && _previewKind == PreviewKind.Channel)
            {
                if (_testSource.SoloChannel == channel)
                {
                    _testSource.SoloChannel = -1;
                    _lblNowPlaying.LabelText = "声道测试：依次 FL → FR → FC → LFE → BL → BR → SL → SR";
                }
                else
                {
                    _testSource.SoloChannel = channel;
                    _lblNowPlaying.LabelText = $"试听：{Layout71.ShortNames[channel]}  {Layout71.Names[channel]}";
                }
                _layout.ActiveChannel = channel;
                return;
            }
            if (StartPreview(rate =>
                {
                    var test = new ChannelTestProvider(rate) { SoloChannel = channel };
                    _testSource = test;
                    return test;
                }, $"试听：{Layout71.ShortNames[channel]}  {Layout71.Names[channel]}", PreviewKind.Channel))
            {
                _btnChannelTest.SetPrimary(false, "停止声道测试", GlyphKind.Stop);
                _layout.ActiveChannel = channel;
            }
        }

        private static string StartupFailedText(Exception ex)
        {
            string msg = ex?.Message ?? "";
            if (msg.Contains("E000020B") || msg.Contains("端点"))
                return "这个音频端点已失效（多半是它的驱动被卸载了，连设备名都读不出来）。\n\n" +
                       "处理办法（二选一）：\n" +
                       "  · 换一个输出设备再试（下拉框里选另一个）；\n" +
                       "  · 重启电脑（失效的设备端点多半会随之消失）。\n\n原始错误：" + msg;
            return "启动失败：\n" + msg;
        }

        private void ToggleGlobal()
        {
            // 和按钮/状态栏同一个口径：引擎对象在 = 认为在跑，点下去就是停
            if (_engine != null)
            {
                StopAll();
                return;
            }
            // 试听 / 声道测试还在跑就先停掉：两路声音同时送到同一个设备会叠在一起，
            // 用户会以为"全局已经开了"，而按钮其实还写着"开始"。
            // 判断用"对象还在不在"而不是 IsRunning —— 试听链可能已经停了但对象还在，
            // 那样按钮会一直停在"停止试听"上，看着像还在放。
            DbgLog($"ToggleGlobal: preview={_preview != null} test={_testSource != null}");
            if (_preview != null || _testSource != null) StopAll();
            var outDev = SelectedOutput();
            if (outDev == null)
            {
                if (_silentStart) return;
                MessageBox.Show("请先选择一个输出设备（耳机或 7.1 音箱）。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool loopback = CaptureIsLoopback;
            MMDevice capDev = SelectedCapture();
            // ---------------- 方式 B：系统环回 ----------------
            if (loopback)
            {
                if (capDev == null)
                {
                    if (_silentStart) return;
                    MessageBox.Show("请先选择“环回来源”设备。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (SurroundEngine.IsFeedbackRisk(capDev, outDev))
                {
                    if (_silentStart) return;   // 开机自动开始时遇到啸叫风险：安静退出，不弹窗打断登录
                    // 不再只丢一段"你自己去改"的说明 —— 直接给一键配好
                    if (_autoFixDepth > 0)
                    {
                        MessageBox.Show(
                            "这台机器只有一个可用的输出设备，没法用「系统环回」。\n\n" +
                            "两条路选一条：\n" +
                            "  · 再插一个输出设备（例如耳机插前面板、扬声器用后面板），让一个抓、一个听；\n" +
                            "  · 装一次免费的 VB-Cable，用「虚拟声卡」方式。\n\n" +
                            "现在也可以先用「播放文件」或「试听演示曲」—— 它们不需要两个设备。",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    var fix = MessageBox.Show(
                        "环回来源和输出设备是同一个（" + SafeName(outDev) + "），会形成啸叫。\n\n" +
                        "要不要我自动配好？\n" +
                        "  · 输出设备 = 耳机\n" +
                        "  · 环回来源 = 另一个输出设备（通常是扬声器）",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (fix != DialogResult.Yes) return;
                    _autoFixDepth++;
                    try { AutoPairDevices(true); }
                    finally { _autoFixDepth--; }
                    ToggleGlobal();     // 配好了，用新选择重新走一遍
                    return;
                }
                try
                {
                    // 声音得先流到「环回来源」这个设备上才抓得到，所以启动时把默认播放设备
                    // 临时切过去；停止时 StopAll 会切回原来的
                    if (_swAutoSwitch == null || _swAutoSwitch.Checked) TakeOverDefault(capDev);
                    else SaveDefaultIfNeeded();

                    _engine = new SurroundEngine(capDev, outDev);
                    _engine.Start(_settings, Latency(), CaptureKind.Loopback);
                    _runCapId = capDev.ID;
                    _runOutId = outDev.ID;
                    // 抓的是「环回来源」，它自己也在正常放音 —— 静音掉，免得喇叭和耳机一起响
                    MuteLoopbackSource(capDev);
                    _lblNowPlaying.LabelText = $"全局环回：{SafeName(capDev)}  →  {SafeName(outDev)}";
                    UpdateAutoHint();
                }
                catch (Exception ex)
                {
                    StopAll();
                    if (!_silentStart) MessageBox.Show(StartupFailedText(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
            // ---------------- 方式 A：虚拟声卡 ----------------
            if (capDev == null)
            {
                MessageBox.Show(
                    "没有找到虚拟声卡的采集端（“CABLE Output”）。\n\n" +
                    "两条路选一条：\n" +
                    "  1) 安装免费的 VB-Cable（一次即可），或者\n" +
                    "  2) 把“采集方式”改成“系统环回 (WASAPI，无需装驱动)”——\n" +
                    "     这需要你有两个输出设备（抓一个、听另一个）。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // 虚拟声卡路径必须用“虚拟”采集端，防止误选成麦克风造成回声
            if (!SafeName(capDev).Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
                !SafeName(capDev).Contains("Cable", StringComparison.OrdinalIgnoreCase))
            {
                var r = MessageBox.Show(
                    "虚拟声卡路径需要一个“虚拟音频设备”的采集端（例如 VB-Cable 的 CABLE Output）。\n\n" +
                    "你选择的采集设备是：“" + SafeName(capDev) + "”。\n" +
                    "如果它其实是真实的麦克风/线路输入，会把外界声音再送出去。\n\n" +
                    "它是一个虚拟声卡设备吗？继续启动会立即采集它。", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            try
            {
                // 把默认播放设备切到虚拟声卡的"输入"端，让其他程序的声音流进来
                if (_swAutoSwitch == null || _swAutoSwitch.Checked)
                {
                    var cableIn = _renderDevices.FirstOrDefault(d => SafeName(d).Contains("CABLE Input"));
                    if (cableIn != null) TakeOverDefault(cableIn);
                    else SaveDefaultIfNeeded();
                }
                else
                {
                    SaveDefaultIfNeeded();
                }
                _engine = new SurroundEngine(capDev, outDev);
                _engine.Start(_settings, Latency(), CaptureKind.VirtualCable);
                _runCapId = capDev.ID;
                _runOutId = outDev.ID;
                _lblNowPlaying.LabelText = $"全局：{SafeName(capDev)}  →  {SafeName(outDev)}";
                UpdateAutoHint();
            }
            catch (Exception ex)
            {
                StopAll();
                if (!_silentStart) MessageBox.Show(StartupFailedText(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveDefaultIfNeeded()
        {
            if (!string.IsNullOrEmpty(_savedDefaultId)) return;
            try
            {
                var def = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _savedDefaultId = def.ID;
                _savedDefaultName = SafeName(def);
            }
            catch { }
        }

        private void RestoreDefault()
        {
            if (string.IsNullOrEmpty(_savedDefaultId))
            {
                MessageBox.Show("没有记录到原来的默认播放设备。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool ok = PolicyConfig.SetDefaultPlayback(_savedDefaultId);
            MessageBox.Show((ok ? "已恢复默认播放设备：\n" : "恢复失败：\n") + _savedDefaultName,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // =====================================================================
        // 默认播放设备：自动接管 / 归还 / 守护
        //   系统环回必须"声音先流到环回来源那个设备上"才抓得到，所以启动时把默认播放设备
        //   临时切到环回来源，停止时再切回去 —— 用户不需要再去右下角手动点。
        // =====================================================================
        /// <summary>把 Windows 默认播放设备临时切到 target，并记下原来的（只记第一次）。</summary>
        private void TakeOverDefault(MMDevice target)
        {
            if (target == null) return;
            SaveDefaultIfNeeded();
            _guardSuppress = true;
            try { PolicyConfig.SetDefaultPlayback(target.ID); }
            catch { }
            finally { _guardSuppress = false; }
            _defaultTakenOver = true;
            _guardBalloonShown = false;
        }

        /// <summary>
        /// 环回时把「环回来源」这个端点静音。
        ///
        /// 环回的原理是"从扬声器上再复制一份声音出来"，扬声器本身照样在放 ——
        /// 于是耳机里是处理过的 7.1、喇叭里是原始立体声，两边一起响。
        /// WASAPI 的环回取的是端点混音之后、音量/静音之前的信号，所以静音端点不影响抓取。
        /// </summary>
        private void MuteLoopbackSource(MMDevice dev)
        {
            if (dev == null || _mutedCapDev != null) return;
            if (_swMuteSrc != null && !_swMuteSrc.Checked) return;
            try
            {
                var vol = dev.AudioEndpointVolume;
                if (vol == null) return;
                _mutedWasOn = vol.Mute;
                if (!_mutedWasOn) vol.Mute = true;
                _mutedCapDev = dev;
            }
            catch { }
        }

        /// <summary>停止时把环回来源恢复成原来的静音状态（本来静音着就别给人家打开）。</summary>
        private void RestoreLoopbackSource()
        {
            if (_mutedCapDev == null) return;
            try { _mutedCapDev.AudioEndpointVolume.Mute = _mutedWasOn; } catch { }
            _mutedCapDev = null;
        }

        /// <summary>把默认播放设备还给用户原来的那个。</summary>
        private void ReleaseDefault()
        {
            if (!_defaultTakenOver) return;
            _defaultTakenOver = false;
            if (string.IsNullOrEmpty(_savedDefaultId)) return;
            _guardSuppress = true;
            try { PolicyConfig.SetDefaultPlayback(_savedDefaultId); }
            catch { }
            finally { _guardSuppress = false; }
        }

        /// <summary>设备名去掉括号里的驱动后缀，太长的名字塞不进小字。</summary>
        private static string ShortDevName(MMDevice d)
        {
            string n = SafeName(d);
            int p = n.IndexOf('(');
            if (p > 1) n = n.Substring(0, p).Trim();
            return n;
        }

        /// <summary>按钮旁边那句小字：说清"点开始之后默认设备会被怎么处理"。</summary>
        private void UpdateAutoHint()
        {
            if (_lblAutoHint == null) return;
            bool auto = _swAutoSwitch == null || _swAutoSwitch.Checked;
            if (_engine?.IsRunning == true)
            {
                _lblAutoHint.LabelText = _defaultTakenOver ? "默认设备已接管" : "运行中";
                return;
            }
            _lblAutoHint.LabelText = auto ? "自动接管默认设备" : "需手动切默认设备";        }

        /// <summary>
        /// 自动配对：耳机模式 + 系统环回时，把「输出设备」配成耳机、「环回来源」配成另一个设备。
        /// 只在首次加载 / 切换采集方式 / 一键修复时调用，不跟用户后来的手动选择打架。
        /// </summary>
        private void AutoPairDevices(bool force)
        {
            if (_refreshing || _renderDevices.Count < 2) return;
            if (!CaptureIsLoopback || !_settings.HeadphoneMode) return;

            var outDev = SelectedOutput();
            var capDev = SelectedCapture();
            bool broken = outDev == null || capDev == null ||
                          string.Equals(outDev.ID, capDev.ID, StringComparison.OrdinalIgnoreCase);
            if (!force && !broken) return;

            MMDevice hp = FindRender("耳机", "headphone", "headset", "earphone");
            MMDevice sp = FindRender("扬声器", "speaker");

            MMDevice newOut = hp ?? outDev;
            if (newOut == null) return;
            MMDevice newCap = (sp != null && !SameId(sp, newOut))
                ? sp
                : _renderDevices.FirstOrDefault(d => !SameId(d, newOut));
            if (newCap == null) return;

            _refreshing = true;
            try
            {
                int oi = IndexOfId(_renderDevices, newOut.ID);
                if (oi >= 0) _cmbOutput.SelectedIndex = oi;
                int ci = IndexOfId(_renderDevices, newCap.ID);
                if (ci >= 0) _cmbCapture.SelectedIndex = ci;
            }
            finally { _refreshing = false; }
            UpdateAutoHint();
        }

        private static bool SameId(MMDevice a, MMDevice b)
        {
            if (a == null || b == null) return false;
            try { return string.Equals(a.ID, b.ID, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>按名字关键字找输出设备（不区分大小写）。</summary>
        private MMDevice FindRender(params string[] keys)
        {
            foreach (var d in _renderDevices)
            {
                string n = SafeName(d).ToLowerInvariant();
                foreach (var k in keys)
                    if (n.Contains(k.ToLowerInvariant())) return d;
            }
            return null;
        }

        /// <summary>
        /// 默认设备守护：运行中被系统改掉（最常见就是插拔耳机，Realtek 会自动切到耳机端点），
        /// 立刻切回环回来源，否则环回就抓不到声音了 —— 用户只会觉得"突然没声了"。
        /// </summary>
        private void GuardDefaultDevice()
        {
            if (_guardSuppress || !_defaultTakenOver) return;
            if (_engine?.IsRunning != true || _engine.Kind != CaptureKind.Loopback) return;
            MMDevice cap = SelectedCapture();
            if (cap == null) return;

            MMDevice def = null;
            try { def = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch { return; }
            if (def != null && SameId(def, cap)) return;

            _guardSuppress = true;
            try { PolicyConfig.SetDefaultPlayback(cap.ID); }
            catch { }
            finally { _guardSuppress = false; }

            if (!_guardBalloonShown)
            {
                _guardBalloonShown = true;
                ShowBalloon("已自动切回默认设备",
                    "Windows 把默认播放设备改掉了（多半是插拔了耳机）。\n" +
                    "为保证环绕声不断，已切回「" + ShortDevName(cap) + "」。");
            }
        }

        /// <summary>正在用的端点被拔掉 / 驱动被卸载时，自动停下来，而不是静默无声地"运行中"。</summary>
        private void CheckRunningEndpoints()
        {
            if (_engine?.IsRunning != true) return;
            var capList = _engine.Kind == CaptureKind.Loopback ? _renderDevices : _captureDevices;
            bool outGone = IndexOfId(_renderDevices, _runOutId) < 0;
            bool capGone = IndexOfId(capList, _runCapId) < 0;
            if (!outGone && !capGone) return;

            string what = outGone ? "输出设备" : "环回来源";
            StopAll();
            ShowBalloon("设备已断开", what + "已经不在了，全局环绕已自动停止。\n重新接好后点「开始全局环绕」即可。");
        }

        /// <summary>
        /// 浮层用的整窗矩形。四周各外扩一圈 —— 贴着客户区边界时，
        /// 窗口边缘会漏出 1~2 个像素的底层内容（实测左侧有一条窄缝），扩出去就不会了。
        /// </summary>
        private Rectangle OverlayBounds()
        {
            const int bleed = 12;
            return new Rectangle(-bleed, -bleed, ClientSize.Width + bleed * 2, ClientSize.Height + bleed * 2);
        }

        /// <summary>在声场图上连点 3 下：打开 / 收起均衡器使用说明。</summary>
        private void SetEqHelp(bool show)
        {
            if (_eqHelp == null) return;
            _eqHelpShown = show;
            _eqHelp.Bounds = OverlayBounds();
            _eqHelp.Visible = show;
            if (show) _eqHelp.BringToFront();
        }

        private void StopAll()
        {
            // 用"引擎对象在不在"判断，不能用 IsRunning —— 采集被系统掐断时 IsRunning 已经是 false，
            // 但默认播放设备还指着环回来源、环回来源还被我们静音着。若这里判 false 就跳过收尾，
            // 用户的电脑会变成"默认设备指向一个不出声的端点 + 该端点还是静音"，看起来像彻底没声了。
            bool wasGlobal = _engine != null;
            try { _engine?.Stop(); } catch { }
            _engine = null;
            _runCapId = null;
            _runOutId = null;
            try { _preview?.Stop(); } catch { }
            _preview = null;
            _previewKind = PreviewKind.None;
            _previewStartedAt = 0;
            _zeroCapAt = 0;
            try { (_currentSource as IDisposable)?.Dispose(); } catch { }
            _currentSource = null;
            _testSource = null;
            // 注意：PillButton 的文字是 LabelText，不是 Control.Text ——
            // 以前这里写 _btnPreview.Text = ... 只改了基类那个没人画的字段，
            // 结果停止之后按钮还显示"停止试听"，看着像还在放。
            _btnPreview.SetPrimary(false, "试听演示曲", GlyphKind.Play);
            if (_btnFile != null) _btnFile.SetPrimary(false, "播放文件", GlyphKind.Folder);
            _btnChannelTest.SetPrimary(false, "声道测试", GlyphKind.Wave);
            _btnGlobal.SetPrimary(true, "开始全局环绕", GlyphKind.Power);
            _globalBtnRunning = false;
            _btnGlobal.AccentOverride = null;
            _btnGlobal.Enabled = CanRunGlobal();
            _layout.ActiveChannel = -1;
            if (_lblNowPlaying != null) _lblNowPlaying.LabelText = "";
            // 全局停了就把默认播放设备还给用户原来的那个，环回来源也解除静音
            if (wasGlobal) { ReleaseDefault(); RestoreLoopbackSource(); }
            UpdateAutoHint();
        }

        private void Shutdown()
        {
            try { if (_notifier != null) _en.UnregisterEndpointNotificationCallback(_notifier); } catch { }
            _notifier = null;
            _deviceTimer.Stop();
            _uiTimer.Stop();
            _eqTimer.Stop();
            bool wasGlobal = _engine != null;   // 同 StopAll：看对象，不看 IsRunning
            StopAll();                          // 内部会 ReleaseDefault + 解除环回来源静音
            try { _saveTimer.Stop(); } catch { }
            SavePersist();                      // 退出前把设置落盘（设备/预设/滑杆/均衡器/开关）
            // 兜底：万一 ReleaseDefault 因为 ID 失效没能切回去，退出前再试一次
            if (wasGlobal && !string.IsNullOrEmpty(_savedDefaultId))
            {
                try { PolicyConfig.SetDefaultPlayback(_savedDefaultId); } catch { }
            }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } } catch { }
        }

        // =====================================================================
        // 状态刷新
        // =====================================================================
        /// <summary>
        /// 均衡器实时频谱：播放中每 33ms 灌一次数据；停播后再推约 1.2 秒的 0（动画淡出）就停表。
        /// </summary>
        private void EqTick()
        {
            var curve = _eqCurve;
            if (curve == null || IsDisposed) { _eqTimer.Stop(); return; }

            SurroundPipeline pipe = _engine?.Pipeline ?? _preview;
            bool running = (_engine?.IsRunning == true) || (_preview?.IsRunning == true);
            if (running && pipe != null)
            {
                curve.SetLevels(pipe.BandLevels, pipe.BandCenters);
                _eqIdleTicks = 0;
            }
            else
            {
                curve.SetLevels(ZeroLevels, pipe?.BandCenters);
                if (++_eqIdleTicks > 36)
                {
                    _eqTimer.Stop();
                    curve.ClearLevels();
                }
            }
        }

        private void UiTick()
        {
            // 采集被系统悄悄掐掉时（驱动重启、设备被切走），引擎对象还在但已经不出声了。
            // 给它 1.5 秒缓冲（Start 之后有一小段时间 IsRunning 还是 false），超时就当作已停止 ——
            // 否则界面会一边显示"运行中"、一边把按钮留在"开始"，最容易误判。
            if (_engine != null && !_engine.IsRunning)
            {
                if (_engineLostAt == 0) _engineLostAt = Environment.TickCount64;
                else if (Environment.TickCount64 - _engineLostAt > 1500)
                {
                    _engineLostAt = 0;
                    StopAll();
                    _stoppedNotice = "已停止：采集被系统中断（设备被切走或驱动重启）—— 点「开始全局环绕」可重新启动";
                }
            }
            else _engineLostAt = 0;

            // 试听链也一样：输出设备被拔掉 / 独占被别的程序抢走时，WasapiOut 会自己停，
            // 但 _preview 对象还在 —— 于是按钮一直停在"停止试听"、状态栏一直写"试听中 峰值 0.00"。
            // 用户看到的就是"按了停止却没反应"。同样给 1.5 秒缓冲（刚 Start 时状态可能还没落定）。
            if (_preview != null && !_preview.IsRunning &&
                _previewStartedAt > 0 && Environment.TickCount64 - _previewStartedAt > 1500)
            {
                if (_previewLostAt == 0) _previewLostAt = Environment.TickCount64;
                else if (Environment.TickCount64 - _previewLostAt > 1500)
                {
                    _previewLostAt = 0;
                    string what = _previewKind == PreviewKind.File ? "播放"
                                : _previewKind == PreviewKind.Channel ? "声道测试" : "试听";
                    StopAll();
                    _stoppedNotice = $"已停止：{what}的输出被系统中断（设备被拔掉或被别的程序独占）—— 重新点一次即可";
                }
            }
            else _previewLostAt = 0;

            // 看门狗可能刚把链路收掉了，所以"在不在响"放到这之后再取
            bool running = _preview?.IsRunning == true || _engine?.IsRunning == true;

            // 主按钮的文案/配色只跟随"全局环绕"状态；试听/声道测试时不该变成"停止全局环绕"（点下去其实会启动全局环绕）
            // 判断口径和状态栏保持一致（看引擎对象在不在），别再出现"状态栏说运行中、按钮还写着开始"
            UpdateGlobalButton(_engine != null);
            // 有声音 + 窗口在眼前时才开 30fps 的频谱动画
            if (running && Visible && WindowState != FormWindowState.Minimized && !_eqTimer.Enabled)
            {
                _eqIdleTicks = 0;
                _eqTimer.Start();
            }
            if (running)
            {
                _peakL = _engine?.PeakL ?? _preview?.PeakL ?? 0f;
                _peakR = _engine?.PeakR ?? _preview?.PeakR ?? 0f;
            }
            else
            {
                _peakL *= 0.8f;
                _peakR *= 0.8f;
            }
            _layout.PeakL = _peakL;
            _layout.PeakR = _peakR;
            if (_testSource != null)
            {
                _layout.ActiveChannel = _testSource.CurrentChannel;
                _lblNowPlaying.LabelText = _testSource.SoloChannel >= 0
                    ? $"试听：{Layout71.ShortNames[_testSource.SoloChannel]}  {Layout71.Names[_testSource.SoloChannel]}"
                    : (_testSource.CurrentChannel >= 0
                        ? $"声道测试：{Layout71.ShortNames[_testSource.CurrentChannel]}  {Layout71.Names[_testSource.CurrentChannel]}"
                        : "声道测试：…");
            }
            if (_engine != null)
            {
                // 采集 0 帧不是坏了：环回只抓"真的流到环回来源上的声音"。
                // 给它 3 秒缓冲再解释，别让用户对着"采集 0 帧"以为程序死了。
                if (_engine.CapturedFrames > 0) _zeroCapAt = 0;
                else if (_zeroCapAt == 0) _zeroCapAt = Environment.TickCount64;
                string zeroHint = (_zeroCapAt > 0 && Environment.TickCount64 - _zeroCapAt > 3000)
                    ? " · 还没抓到声音：播放器没在放，或它输出到了别的端点"
                    : "";
                string kind = _engine.Kind == CaptureKind.Loopback ? "系统环回" : "虚拟声卡";
                string chain = _defaultTakenOver ? " · 默认设备已接管" : "";
                if (_mutedCapDev != null) chain += " · 环回来源已静音";
                _lblStatus.LabelText = $"全局运行中（{kind}） · {_engine.CaptureDeviceName} → {_engine.RenderDeviceName}" +
                                       $"{chain} · 采集 {_engine.CapturedFrames:N0} 帧 · 丢帧 {_engine.DroppedSamples}{zeroHint}";
            }
            else if (_preview != null)
            {
                _lblStatus.LabelText = $"试听中 · 输出 {_preview.OutputDeviceName} · {_preview.OutputFormat} · " +
                                       $"峰值 L {_peakL:0.00} / R {_peakR:0.00}";
            }
            else
            {
                string mode = _settings.HeadphoneMode ? "耳机虚拟 7.1" : "真实 7.1 音箱";
                // 刚被系统掐断时先说这一句，别让用户以为是自己按错了
                if (_stoppedNotice != null)
                {
                    _lblStatus.LabelText = _stoppedNotice;
                    _stoppedNotice = null;
                    _layout.Invalidate();
                    return;
                }
                // 空闲时把"点开始会发生什么"直接写在状态栏上，省得用户去翻说明
                string tip = "";
                if (!CanRunGlobal())
                {
                    _lblStatus.LabelText = "就绪 · 本机只有一个输出设备，全局环绕用不了 —— 可以直接用「播放文件」听 7.1 效果";
                    _layout.Invalidate();
                    return;
                }
                if (CaptureIsLoopback)
                {
                    var cap = SelectedCapture();
                    if (cap != null && (_swAutoSwitch == null || _swAutoSwitch.Checked))
                        tip = $" · 点开始后会把默认播放设备切到「{ShortDevName(cap)}」，停止时切回";
                    else if (cap != null)
                        tip = $" · 需要你手动把默认播放设备切到「{ShortDevName(cap)}」";
                }
                else if (_swAutoSwitch == null || _swAutoSwitch.Checked)
                {
                    tip = " · 点开始后会把默认播放设备切到「CABLE Input」，停止时切回";
                }
                _lblStatus.LabelText = $"就绪 · 模式：{mode}{tip}";
            }
            _layout.Invalidate();
        }
    }
    // =========================================================================
    // 设备热插拔回调
    // =========================================================================
    /// <summary>
    /// Windows 在设备插入 / 拔出 / 驱动重装时通过此回调通知我们；回调可能发生在任意线程，
    /// 所以这里只负责把消息安全地丢回 UI 线程（BeginInvoke），由主窗体去抖后再刷新设备列表。
    /// </summary>
    internal sealed class DeviceChangeNotifier : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        private readonly MainForm _owner;

        public DeviceChangeNotifier(MainForm owner) { _owner = owner; }
        void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnDeviceStateChanged(string deviceId, NAudio.CoreAudioApi.DeviceState newState) => Ping();
        void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnDeviceAdded(string deviceId) => Ping();
        void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnDeviceRemoved(string deviceId) => Ping();
        // 默认播放设备被切换（插拔耳机时 Realtek 会自动切）也要通知 —— 全局环回正在跑时要立刻切回来
        void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow dataFlow, NAudio.CoreAudioApi.Role deviceRole, string defaultDeviceId) => Ping();
        void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, NAudio.CoreAudioApi.PropertyKey propertyKey) { }

        private void Ping()
        {
            try
            {
                var f = _owner;
                if (f == null || f.IsDisposed || !f.IsHandleCreated) return;
                f.BeginInvoke(new Action(() => f.NotifyDeviceChanged()));
            }
            catch { }
        }
    }
    // =========================================================================
}
