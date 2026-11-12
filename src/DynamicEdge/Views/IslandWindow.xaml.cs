using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicEdge.Core;
using DynamicEdge.Services;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace DynamicEdge.Views;

public sealed class Toast
{
    public string Key = "", Glyph = "", Title = "", Subtitle = "", Right = "";
    public System.Windows.Media.ImageSource? Image;
    public double? Progress;
    public Color Accent = Colors.White;
    public int DurationMs = 4000;
    public bool Urgent, Shake, Pulse;
    public FaceMood? Mood;
}

public sealed class ClipEntry
{
    public string Full { get; init; } = "";
    public string Preview => Full.Replace("\r", " ").Replace("\n", " ⏎ ").Trim() is { Length: > 90 } s ? s[..90] + "…" : Full.Replace("\r", " ").Replace("\n", " ⏎ ").Trim();
}

public partial class IslandWindow : Window
{
    enum Mode { Idle, Hover, Toast, Expanded }

    static AppSettings S => SettingsService.Current;
    Mode _mode = Mode.Idle;
    double _curW = 120, _curH = 34;
    FrameworkElement[] _layers = Array.Empty<FrameworkElement>();
    FrameworkElement _layerTarget = null!;

    readonly System.Collections.Generic.List<Toast> _queue = new();
    Toast? _current;
    readonly DispatcherTimer _toastTimer = new();
    readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer _sysTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer _progTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    string _presetKey = "", _orderKey = "";
    int _animToken, _wd;
    FaceBrain _brain = null!;
    /// Пока true, развёрнутая панель не сворачивается сама (предпросмотр из настроек).
    public bool Hold { get; set; }

    bool _userHidden, _fsHidden, _suppressClip;
    string _hkKey = "";
    readonly ObservableCollection<ClipEntry> _clips = new();

    public IslandWindow()
    {
        InitializeComponent();
        _layers = new FrameworkElement[] { IdleLayer, ToastLayer, ExpLayer };
        _layerTarget = IdleLayer;
        _brain = new FaceBrain(IdleFace);
        ClipList.ItemsSource = _clips;
        SourceInitialized += OnSourceInitialized;

        _toastTimer.Tick += (_, _) => OnToastTimer();
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); if (_mode == Mode.Idle && Capsule.IsMouseOver) SetMode(Mode.Hover); };
        _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); if (_mode == Mode.Hover && !Capsule.IsMouseOver) SetMode(Mode.Idle); };
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); if (_mode == Mode.Expanded && !Capsule.IsMouseOver && !Hold) SetMode(Mode.Idle); };
        _sysTimer.Tick += (_, _) => { SampleSys(); _sysTimer.Interval = TimeSpan.FromMilliseconds(1500); };
        _progTimer.Tick += (_, _) => UpdateProgress();
        _watchTimer.Tick += (_, _) => Watchdog();
        _idleTimer.Tick += (_, _) => UpdateIdle(false);
    }

    // ------------------------------------------------------------------ init
    void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetToolWindowNoActivate(hwnd);
        PositionWindow();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionWindow);
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(PositionWindow);

        var sv = AppServices.Media; sv.Changed += OnMedia;
        AppServices.Battery.Event += OnBattery;
        AppServices.Output.Changed += (v, m) => Dispatcher.BeginInvoke(() => OnVolume(v, m));
        AppServices.Input.Changed += (v, m) => Dispatcher.BeginInvoke(() => OnMicMute(m));
        AppServices.MicUsage.Changed += OnMicUsage;
        AppServices.Notifications.Received += OnNotification;
        AppServices.Msg.ClipboardUpdated += OnClipboard;
        AppServices.Msg.HotkeyPressed += OnHotkey;
        AppServices.Timer.Tick += OnTimerTick;
        AppServices.Timer.Started += OnTimerStarted;
        AppServices.Timer.Completed += OnTimerDone;
        SettingsService.Changed += OnSettingsChanged;

        ReadClipboardInitial();
        OnSettingsChanged();
        _watchTimer.Start();
    }

    // ------------------------------------------------------------------ positioning / visibility
    void PositionWindow()
    {
        try
        {
            var screens = WF.Screen.AllScreens;
            var scr = S.Monitor >= 0 && S.Monitor < screens.Length ? screens[S.Monitor] : WF.Screen.PrimaryScreen ?? screens[0];
            var b = scr.Bounds;
            double sc = Native.MonitorScale(b.X + b.Width / 2, b.Y + 10);
            int wPx = (int)Math.Round(Width * sc), hPx = (int)Math.Round(Height * sc);
            int x = b.X + (b.Width - wPx) / 2 + (int)Math.Round(S.OffsetX * sc);
            int y = b.Y + (int)Math.Round((S.OffsetY - 6) * sc);
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, wPx, hPx, Native.SWP_NOACTIVATE);
        }
        catch (Exception ex) { SettingsService.Log("Position: " + ex.Message); }
    }

    void Watchdog()
    {
        // скрытие при полноэкранных приложениях/играх/презентациях
        bool fs = false;
        if (S.HideInFullscreen && Native.SHQueryUserNotificationState(out int st) == 0) fs = st is 2 or 3 or 4;
        if (fs != _fsHidden) { _fsHidden = fs; ApplyVisibility(); }
        // поддерживаем Topmost поверх других topmost-окон
        if (IsVisible) Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        if (++_wd % 5 == 0) { AppServices.Output.RecheckDeviceAsync(); AppServices.Input.RecheckDeviceAsync(); }
    }

    void ApplyVisibility()
    {
        bool vis = !_userHidden && !_fsHidden;
        if (vis && Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            PositionWindow();
            Fade(Shell, 1, 220);
        }
        else if (!vis && Visibility == Visibility.Visible)
        {
            Fade(Shell, 0, 180, 0, () => { if (_userHidden || _fsHidden) Visibility = Visibility.Hidden; });
        }
    }

    public void ToggleHidden() { _userHidden = !_userHidden; ApplyVisibility(); if (!_userHidden) Post(new Toast { Key = "vis", Mood = FaceMood.Happy, Glyph = "\uE7B3", Title = "Dynamic Edge", Subtitle = "Капсула снова видна", Accent = Colors.White, DurationMs = 1600 }); }
    public bool IsUserHidden => _userHidden;

    // ------------------------------------------------------------------ settings
    void OnSettingsChanged()
    {
        ThemeManager.Apply();
        CapScale.ScaleX = CapScale.ScaleY = S.Scale;
        Capsule.BorderThickness = new Thickness(S.BorderWidth);
        CapShadow.Opacity = S.ShadowStrength;
        Capsule.Effect = S.ShadowStrength > 0.01 ? CapShadow : null;
        Capsule.CornerRadius = new CornerRadius(Math.Min(Capsule.ActualHeight / 2, S.CornerRadiusMax));
        ExpLayer.Width = S.ExpandedWidth - 32;
        BuildPresets();
        IdleFace.Unit = S.FaceSize * S.IdleHeight / 34.0;
        if (S.FaceEnabled) _brain.Start(); else _brain.Stop();
        bool needIdle = S.IdleShowClock || S.IdleShowBattery;
        if (needIdle && !_idleTimer.IsEnabled) _idleTimer.Start(); else if (!needIdle && _idleTimer.IsEnabled) _idleTimer.Stop();
        PositionWindow();
        AppServices.Battery.Enabled = S.EnableBattery;
        AppServices.MicUsage.Enabled = S.EnableMic;
        RegisterHotkeys();
        if (!S.EnableClipboard) _clips.Clear();
        UpdateIdle(true);
        if (_mode == Mode.Expanded) { RefreshExpanded(); ResizeExpanded(); }
        ApplyIdleAutoHide();
        ApplyVisibility();
    }

    void RegisterHotkeys()
    {
        string key = $"{S.HotkeyToggle}|{S.HotkeyHide}|{S.HotkeyTimer}";
        if (key == _hkKey) return;
        _hkKey = key;
        var m = AppServices.Msg;
        m.UnregisterAll();
        string failed = "";
        if (!m.Register(1, S.HotkeyToggle)) failed += S.HotkeyToggle + " ";
        if (!m.Register(2, S.HotkeyHide)) failed += S.HotkeyHide + " ";
        if (!m.Register(3, S.HotkeyTimer)) failed += S.HotkeyTimer + " ";
        if (failed.Length > 0) SettingsService.Log("Hotkeys not registered: " + failed);
    }

    void OnHotkey(int id)
    {
        switch (id)
        {
            case 1: if (_mode == Mode.Expanded) SetMode(Mode.Idle); else if (!_userHidden) SetMode(Mode.Expanded); break;
            case 2: ToggleHidden(); break;
            case 3:
                if (!S.EnableTimer) break;
                if (AppServices.Timer.Running) AppServices.Timer.Stop();
                else AppServices.Timer.Start(TimeSpan.FromMinutes(S.DefaultTimerMinutes));
                break;
        }
    }

    public void ToggleExpanded() { if (_userHidden) { _userHidden = false; ApplyVisibility(); } if (_mode == Mode.Expanded) SetMode(Mode.Idle); else SetMode(Mode.Expanded); }

    // ------------------------------------------------------------------ animation helpers
    /// Длительность с учётом скорости анимаций из настроек.
    static TimeSpan Dur(int ms)
    {
        double k = Math.Max(0.3, S.AnimSpeed) * (S.AnimStyle == "Instant" ? 4 : 1);
        return TimeSpan.FromMilliseconds(ms / k);
    }

    void Fade(UIElement el, double to, int ms, int delay = 0, Action? done = null)
    {
        if (!S.Animations) { el.BeginAnimation(OpacityProperty, null); el.Opacity = to; done?.Invoke(); return; }
        var a = new DoubleAnimation(to, Dur(ms)) { BeginTime = Dur(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (done != null) a.Completed += (_, _) => done();
        el.BeginAnimation(OpacityProperty, a);
    }

    void AnimateSize(double w, double h, bool grow)
    {
        _curW = w; _curH = h;
        if (!S.Animations || S.AnimStyle == "Instant")
        {
            Capsule.BeginAnimation(WidthProperty, null); Capsule.BeginAnimation(HeightProperty, null);
            Capsule.Width = w; Capsule.Height = h; return;
        }
        IEasingFunction ease; int ms;
        if (S.AnimStyle == "Spring" && grow) { ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = S.SpringAmount }; ms = 480; }
        else if (S.AnimStyle == "Spring") { ease = new CubicEase { EasingMode = EasingMode.EaseInOut }; ms = 300; }
        else { ease = new SineEase { EasingMode = EasingMode.EaseInOut }; ms = 420; }
        var dur = Dur(ms);
        // Тень (Effect) на прозрачном окне считается программно и тормозит анимацию размера:
        // на время анимации убираем её, потом плавно возвращаем.
        SuspendShadow();
        int token = ++_animToken;
        var wAnim = new DoubleAnimation(w, dur) { EasingFunction = ease };
        wAnim.Completed += (_, _) => { if (token == _animToken) RestoreShadow(); };
        Capsule.BeginAnimation(WidthProperty, wAnim);
        Capsule.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
    }

    void SuspendShadow()
    {
        if (Capsule.Effect == null) return;
        CapShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
        Capsule.Effect = null;
    }

    void RestoreShadow()
    {
        if (S.ShadowStrength <= 0.01 || Capsule.Effect != null) return;
        CapShadow.Opacity = S.ShadowStrength;
        Capsule.Effect = CapShadow;
        if (S.Animations)
            CapShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                new DoubleAnimation(0, S.ShadowStrength, Dur(260)) { FillBehavior = FillBehavior.Stop });
    }

    void Capsule_SizeChanged(object sender, SizeChangedEventArgs e) => Capsule.CornerRadius = new CornerRadius(Math.Min(e.NewSize.Height / 2, S.CornerRadiusMax));

    void CrossFade(FrameworkElement target)
    {
        foreach (var l in _layers)
        {
            if (l == target)
            {
                if (l.Visibility != Visibility.Visible)
                {
                    l.BeginAnimation(OpacityProperty, null); l.Opacity = 0; l.Visibility = Visibility.Visible;
                    Fade(l, 1, 240, 110);
                    if (S.Animations && l.RenderTransform is TranslateTransform tt)
                        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, Dur(260)) { BeginTime = Dur(110), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
            }
            else if (l.Visibility == Visibility.Visible)
            {
                var layer = l;
                Fade(l, 0, 110, 0, () => { if (_layerTarget != layer) layer.Visibility = Visibility.Collapsed; });
            }
        }
        _layerTarget = target;
    }

    void Shake()
    {
        if (!S.Animations) return;
        var a = new DoubleAnimationUsingKeyFrames();
        foreach (var (t, v) in new[] { (0, 0), (60, -9), (130, 9), (200, -7), (270, 7), (340, -3), (420, 0) })
            a.KeyFrames.Add(new LinearDoubleKeyFrame(v, TimeSpan.FromMilliseconds(t)));
        CapShift.BeginAnimation(TranslateTransform.XProperty, a);
    }

    void SetBar(ScaleTransform st, double v)
    {
        v = Math.Clamp(v, 0, 1);
        if (!S.Animations) { st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.ScaleX = v; return; }
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(v, Dur(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ------------------------------------------------------------------ mode machine
    void SetMode(Mode m)
    {
        _mode = m;
        _brain.Hover = m == Mode.Hover;
        _collapseTimer.Stop(); _sysTimer.Stop(); _progTimer.Stop();
        double w, h; FrameworkElement target;
        switch (m)
        {
            case Mode.Toast: target = ToastLayer; w = 372; h = 70; break;
            case Mode.Expanded:
                target = ExpLayer; RefreshExpanded();
                (w, h) = ExpandedSize();
                if (S.EnableSysInfo) { ApplyTempColumn(AppServices.SysInfo.Sample().TempC.HasValue); _sysTimer.Interval = TimeSpan.FromMilliseconds(600); _sysTimer.Start(); }
                _progTimer.Start(); UpdateProgress();
                break;
            default:
                target = IdleLayer; UpdateIdle(false);
                w = IdleWidth(); h = m == Mode.Hover ? S.IdleHeight + 4 : S.IdleHeight; break;
        }
        bool grow = w * h > _curW * _curH;
        CrossFade(target);
        AnimateSize(w, h, grow);
        ApplyIdleAutoHide();
        if (m == Mode.Expanded && !Capsule.IsMouseOver) _collapseTimer.Start();
    }

    (double, double) ExpandedSize()
    {
        // Collapsed-элемент измеряется как 0×0, поэтому для замера делаем его Hidden (он занимает место, но не рисуется)
        if (ExpLayer.Visibility == Visibility.Collapsed) ExpLayer.Visibility = Visibility.Hidden;
        ExpLayer.Measure(new Size(S.ExpandedWidth - 32, double.PositiveInfinity));
        return (S.ExpandedWidth, Math.Max(80, ExpLayer.DesiredSize.Height + 36));
    }

    void ResizeExpanded() { var (w, h) = ExpandedSize(); AnimateSize(w, h, false); }

    void Capsule_Click(object sender, MouseButtonEventArgs e)
    {
        Hold = false;
        if (_mode == Mode.Expanded) SetMode(Mode.Idle);
        else { _hoverTimer.Stop(); SetMode(Mode.Expanded); }
    }

    void Capsule_Enter(object sender, MouseEventArgs e)
    {
        _leaveTimer.Stop(); _collapseTimer.Stop();
        _brain.Poke();
        if (_mode == Mode.Idle) { _hoverTimer.Stop(); _hoverTimer.Start(); }
        ApplyIdleAutoHide();
    }

    void Capsule_Leave(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        if (_mode == Mode.Hover) { _leaveTimer.Stop(); _leaveTimer.Start(); }
        if (_mode == Mode.Expanded) { _collapseTimer.Stop(); _collapseTimer.Start(); }
        ApplyIdleAutoHide();
    }

    // ------------------------------------------------------------------ idle layer
    bool MusicOn => S.EnableMusic && AppServices.Media.Current.HasSession;
    bool TimerOn => S.EnableTimer && AppServices.Timer.Running;
    bool NothingToShow => !S.FaceEnabled && !(MusicOn && (S.IdleShowArt || S.IdleShowBars)) && BuildIdleText(false).Length == 0 && !(S.EnableMic && AppServices.MicUsage.Active);

    void ApplyIdleAutoHide()
    {
        if (!S.AutoHideIdle && Capsule.Opacity >= 1) return;
        bool hide = S.AutoHideIdle && _mode == Mode.Idle && NothingToShow && !Capsule.IsMouseOver;
        Fade(Capsule, hide ? 0 : 1, 250);
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    string BuildIdleText(bool hover)
    {
        var mi = AppServices.Media.Current;
        var parts = new System.Collections.Generic.List<string>();
        bool track = MusicOn && mi.Title.Length > 0;
        string trackText = mi.Artist.Length > 0 ? $"{mi.Artist} — {mi.Title}" : mi.Title;
        if (hover)
        {
            if (track) parts.Add(trackText);
            if (TimerOn) parts.Add(Fmt(AppServices.Timer.Display));
            if (S.EnableBattery && AppServices.Battery.HasBattery) parts.Add($"{AppServices.Battery.Percent}%");
            parts.Add(DateTime.Now.ToString("HH:mm"));
        }
        else
        {
            if (S.IdleShowTrack && track) parts.Add(trackText);
            if (S.IdleShowTimer && TimerOn) parts.Add(Fmt(AppServices.Timer.Display));
            if (S.IdleShowBattery && S.EnableBattery && AppServices.Battery.HasBattery) parts.Add($"{AppServices.Battery.Percent}%");
            if (S.IdleShowClock) parts.Add(DateTime.Now.ToString("HH:mm"));
        }
        return string.Join("  •  ", parts);
    }

    void RefreshIdle()
    {
        var mi = AppServices.Media.Current;
        bool music = MusicOn;
        if (music && S.IdleShowArt && mi.Art is System.Windows.Media.ImageSource art)
        {
            IdleArt.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            IdleArt.Visibility = Visibility.Visible;
        }
        else IdleArt.Visibility = Visibility.Collapsed;
        IdleFace.Visibility = S.FaceEnabled ? Visibility.Visible : Visibility.Collapsed;
        bool bars = music && S.IdleShowBars && !(S.FaceEnabled && S.FaceDance);
        IdleBars.Visibility = bars ? Visibility.Visible : Visibility.Collapsed;
        IdleBars.IsActive = bars && mi.Playing;
        MicDot.Visibility = S.EnableMic && AppServices.MicUsage.Active ? Visibility.Visible : Visibility.Collapsed;

        string text = BuildIdleText(_mode == Mode.Hover);
        IdleText.Text = text;
        IdleText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // отступы только между видимыми элементами, чтобы содержимое всегда было по центру
        bool first = true;
        foreach (FrameworkElement el in new FrameworkElement[] { MicDot, IdleArt, IdleFace, IdleBars, IdleText })
        {
            if (el.Visibility != Visibility.Visible) continue;
            el.Margin = new Thickness(first ? 0 : 8, 0, 0, 0);
            first = false;
        }
    }

    double IdleWidth()
    {
        IdleStack.Measure(new Size(double.PositiveInfinity, 40));
        return Math.Clamp(IdleStack.DesiredSize.Width + 36, 112, 420);
    }

    void UpdateIdle(bool force)
    {
        if (_mode != Mode.Idle && _mode != Mode.Hover) return;
        RefreshIdle();
        double w = IdleWidth();
        if (force || Math.Abs(w - _curW) > 2) AnimateSize(w, _mode == Mode.Hover ? S.IdleHeight + 4 : S.IdleHeight, false);
        ApplyIdleAutoHide();
    }

    // ------------------------------------------------------------------ toast queue
    void Post(Toast t)
    {
        if (_userHidden || _fsHidden) return;
        if (_mode == Mode.Expanded && !t.Urgent) return;

        if (_mode == Mode.Toast && _current != null && t.Key.Length > 0 && _current.Key == t.Key)
        {
            _current = t; ApplyToast(t, false); RestartToastTimer(t); return;
        }
        if (_mode != Mode.Toast || t.Urgent) { ShowToast(t); return; }
        if (t.Key.Length > 0) _queue.RemoveAll(q => q.Key == t.Key);
        _queue.Add(t);
        if (_queue.Count > 5) _queue.RemoveAt(0);
    }

    void ShowToast(Toast t)
    {
        _current = t;
        ApplyToast(t, _mode == Mode.Toast);
        if (_mode != Mode.Toast) SetMode(Mode.Toast);
        RestartToastTimer(t);
    }

    void RestartToastTimer(Toast t) { _toastTimer.Stop(); _toastTimer.Interval = TimeSpan.FromMilliseconds(t.DurationMs); _toastTimer.Start(); }

    void OnToastTimer()
    {
        _toastTimer.Stop();
        if (_mode != Mode.Toast) return;
        if (Capsule.IsMouseOver) { _toastTimer.Interval = TimeSpan.FromMilliseconds(1200); _toastTimer.Start(); return; }
        if (_queue.Count > 0) { var n = _queue[0]; _queue.RemoveAt(0); ShowToast(n); }
        else { _current = null; SetMode(Mode.Idle); }
    }

    void ApplyToast(Toast t, bool pulseSwap)
    {
        ToastGlyph.BeginAnimation(OpacityProperty, null); ToastGlyph.Opacity = 1;
        if (t.Image != null)
        {
            ToastImg.Source = t.Image; ToastImg.Visibility = Visibility.Visible; ToastGlyph.Visibility = Visibility.Collapsed;
            ToastFace.Visibility = Visibility.Collapsed;
            ToastIcon.Background = (Brush)FindResource("ChipBg");
        }
        else
        {
            bool face = S.FaceEnabled && S.FaceInToasts && t.Mood.HasValue;
            ToastImg.Visibility = Visibility.Collapsed; ToastGlyph.Visibility = face ? Visibility.Collapsed : Visibility.Visible;
            ToastGlyph.Text = t.Glyph; ToastGlyph.Foreground = new SolidColorBrush(t.Accent);
            ToastFace.Visibility = face ? Visibility.Visible : Visibility.Collapsed;
            if (face)
            {
                var eyes = ThemeManager.IsLight && t.Accent == Colors.White ? Color.FromRgb(0x11, 0x11, 0x11) : t.Accent;
                ToastFace.Foreground = new SolidColorBrush(eyes);
                ToastFace.SetMood(t.Mood!.Value, true);
                if (t.Mood is FaceMood.Excited or FaceMood.Surprised or FaceMood.Happy) ToastFace.Bounce();
            }
            ToastIcon.Background = new SolidColorBrush(Color.FromArgb(46, t.Accent.R, t.Accent.G, t.Accent.B));
        }
        ToastTitle.Text = t.Title;
        ToastSub.Text = t.Subtitle; ToastSub.Visibility = t.Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToastRight.Text = t.Right;
        if (t.Progress is double p)
        {
            ToastBar.Visibility = Visibility.Visible;
            ToastFill.Background = new SolidColorBrush(t.Accent);
            SetBar(ToastFillScale, p);
        }
        else ToastBar.Visibility = Visibility.Collapsed;

        if (t.Pulse && S.Animations)
            ToastGlyph.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        if (t.Shake) Shake();
        if (pulseSwap && S.Animations)
        {
            ToastLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, Dur(220)));
            if (ToastLayer.RenderTransform is TranslateTransform tt)
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, Dur(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    // ------------------------------------------------------------------ event sources → toasts
    void OnMedia(MediaInfo info, bool trackChanged)
    {
        ThemeManager.SetAccent(info.HasSession ? info.Accent : ThemeManager.DefaultAccent);
        if (_mode == Mode.Expanded) { UpdateMusicUi(); ResizeExpanded(); }
        else UpdateIdle(false);
        if (trackChanged && info.Title.Length > 0) _brain.React(FaceMood.Happy, 1800);
        if (trackChanged && S.EnableMusic && info.Title.Length > 0 && _mode != Mode.Expanded)
            Post(new Toast { Key = "track", Glyph = "\uE8D6", Image = info.Art, Title = info.Title, Subtitle = info.Artist, Accent = info.Accent, DurationMs = 3500 });
    }

    void OnVolume(float v, bool muted)
    {
        if (!S.EnableVolume) return;
        int pct = (int)Math.Round(v * 100);
        if (muted) _brain.React(FaceMood.Closed, 1200); else if (pct >= 80) _brain.React(FaceMood.Surprised, 900);
        string glyph = muted || pct == 0 ? "\uE74F" : pct < 34 ? "\uE992" : pct < 67 ? "\uE993" : "\uE994";
        Post(new Toast
        {
            Key = "volume", Glyph = glyph, Title = muted ? "Звук выключен" : "Громкость", Subtitle = "",
            Mood = muted ? FaceMood.Closed : pct >= 80 ? FaceMood.Surprised : (FaceMood?)null, Progress = muted ? 0 : v, Right = muted ? "" : pct + "%", Accent = Color.FromRgb(0x4C, 0xC2, 0xFF), DurationMs = 1600
        });
    }

    void OnMicMute(bool muted)
    {
        if (!S.EnableMic) return;
        _brain.React(muted ? FaceMood.Closed : FaceMood.Happy, 1500);
        Post(new Toast { Key = "micmute", Mood = muted ? FaceMood.Closed : FaceMood.Happy, Glyph = muted ? "\uF781" : "\uE720", Title = muted ? "Микрофон выключен" : "Микрофон включён", Accent = muted ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0x32, 0xD7, 0x4B), DurationMs = 1800 });
    }

    void OnMicUsage()
    {
        var m = AppServices.MicUsage;
        if (m.Active) Post(new Toast { Key = "mic", Mood = FaceMood.Alert, Glyph = "\uE720", Title = "Микрофон используется", Subtitle = m.App ?? "", Accent = Color.FromRgb(0xFF, 0x9F, 0x0A), DurationMs = 2600 });
        UpdateIdle(false);
    }

    void OnBattery(BatteryEventKind k)
    {
        if (!S.EnableBattery) return;
        var b = AppServices.Battery; double p = b.Percent / 100.0;
        if (k == BatteryEventKind.PluggedIn || k == BatteryEventKind.Full) _brain.React(FaceMood.Happy, 2500);
        var green = Color.FromRgb(0x32, 0xD7, 0x4B);
        switch (k)
        {
            case BatteryEventKind.PluggedIn: Post(new Toast { Key = "bat", Glyph = "\uE83E", Mood = FaceMood.Happy, Title = "Зарядка подключена", Subtitle = "Идёт зарядка", Progress = p, Right = b.Percent + "%", Accent = green, DurationMs = 3200, Pulse = true }); break;
            case BatteryEventKind.Unplugged: Post(new Toast { Key = "bat", Glyph = "\uE83F", Title = "Зарядка отключена", Subtitle = "Работа от батареи", Progress = p, Right = b.Percent + "%", Accent = Colors.White, DurationMs = 2800 }); break;
            case BatteryEventKind.Low: Post(new Toast { Key = "bat", Glyph = "\uE850", Mood = FaceMood.Tired, Title = "Низкий заряд батареи", Subtitle = "Подключите зарядное устройство", Progress = p, Right = b.Percent + "%", Accent = Color.FromRgb(0xFF, 0x45, 0x3A), DurationMs = 7000, Urgent = true, Shake = true }); break;
            case BatteryEventKind.Full: Post(new Toast { Key = "bat", Glyph = "\uE83E", Mood = FaceMood.Happy, Title = "Батарея заряжена", Progress = 1, Right = "100%", Accent = green, DurationMs = 3500 }); break;
        }
    }

    void OnNotification(NotificationItem n)
    {
        if (!S.EnableNotifications) return;
        _brain.React(FaceMood.Surprised, 1600);
        Post(new Toast
        {
            Key = "", Glyph = "\uEA8F", Image = n.Icon, Mood = FaceMood.Surprised,
            Title = n.Title.Length > 0 ? n.Title : n.App,
            Subtitle = n.Title.Length > 0 ? (n.Body.Length > 0 ? n.Body : n.App) : "",
            Accent = Colors.White, DurationMs = S.NotificationSeconds * 1000
        });
    }

    // ------------------------------------------------------------------ clipboard
    void ReadClipboardInitial()
    {
        if (!S.EnableClipboard) return;
        try { if (Clipboard.ContainsText()) AddClip(Clipboard.GetText()); } catch { }
    }

    void AddClip(string text)
    {
        text = text.Length > 2000 ? text[..2000] : text;
        if (string.IsNullOrWhiteSpace(text)) return;
        var ex = _clips.FirstOrDefault(c => c.Full == text);
        if (ex != null) _clips.Remove(ex);
        _clips.Insert(0, new ClipEntry { Full = text });
        while (_clips.Count > 3) _clips.RemoveAt(_clips.Count - 1);
    }

    void OnClipboard()
    {
        if (_suppressClip) { _suppressClip = false; return; }
        if (!S.EnableClipboard) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ReadClipboard));
    }

    void ReadClipboard()
    {
        try
        {
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return;   // менеджеры паролей
            string? text = null;
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    if (Clipboard.ContainsText()) text = Clipboard.GetText();
                    else if (Clipboard.ContainsImage()) { Post(ClipToast("Изображение", "Скопировано изображение")); return; }
                    else if (Clipboard.ContainsFileDropList()) { int n = Clipboard.GetFileDropList().Count; Post(ClipToast("Файлы", $"Скопировано файлов: {n}")); return; }
                    break;
                }
                catch (ExternalException) { Thread.Sleep(40); }
            }
            if (string.IsNullOrWhiteSpace(text)) return;
            AddClip(text);
            _brain.React(FaceMood.Focused, 900);
            string one = text.Replace("\r", " ").Replace("\n", " ").Trim();
            Post(ClipToast("Скопировано", one.Length > 70 ? one[..70] + "…" : one));
            if (_mode == Mode.Expanded) { UpdateClipUi(); ResizeExpanded(); }
        }
        catch (Exception ex) { SettingsService.Log("Clipboard: " + ex.Message); }
    }

    static Toast ClipToast(string title, string sub) => new() { Key = "clip", Glyph = "\uE8C8", Title = title, Subtitle = sub, Accent = Color.FromRgb(0xBF, 0x5A, 0xF2), DurationMs = 2400 };

    void ClipItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ClipEntry c) return;
        try { _suppressClip = true; Clipboard.SetText(c.Full); }
        catch { _suppressClip = false; }
    }

    // ------------------------------------------------------------------ timer
    void OnTimerStarted()
    {
        if (!S.EnableTimer) return;
        var t = AppServices.Timer;
        _brain.React(FaceMood.Focused, 1500);
        Post(new Toast { Key = "timer", Mood = FaceMood.Focused, Glyph = "\uE916", Title = t.Mode == TimerMode.Countdown ? "Таймер запущен" : "Секундомер запущен", Subtitle = t.Mode == TimerMode.Countdown ? Fmt(t.Total) : "", Accent = Color.FromRgb(0xFF, 0x9F, 0x0A), DurationMs = 1800 });
    }

    void OnTimerTick()
    {
        if (_mode == Mode.Expanded) UpdateTimerUi(); else UpdateIdle(false);
    }

    void OnTimerDone()
    {
        if (!S.EnableTimer) return;
        System.Media.SystemSounds.Exclamation.Play();
        _brain.React(FaceMood.Excited, 3500);
        Post(new Toast { Key = "timer", Glyph = "\uE916", Mood = FaceMood.Excited, Title = "Таймер завершён", Subtitle = Fmt(AppServices.Timer.Total), Accent = Color.FromRgb(0xFF, 0x9F, 0x0A), DurationMs = 8000, Urgent = true, Shake = true });
    }

    void BuildPresets()
    {
        var list = SettingsService.PresetList;
        string key = string.Join(",", list);
        if (key == _presetKey) return;
        _presetKey = key;
        PresetHost.Children.Clear();
        foreach (int m in list)
        {
            var b = new Button { Style = (Style)FindResource("ChipBtn"), Content = m >= 60 && m % 60 == 0 ? $"{m / 60}ч" : $"{m}м", Tag = m.ToString() };
            b.Click += Preset_Click;
            PresetHost.Children.Add(b);
        }
    }

    void ApplyPanelOrder()
    {
        var order = SettingsService.PanelOrderList;
        string key = string.Join(",", order);
        if (key != _orderKey)
        {
            _orderKey = key;
            var map = new System.Collections.Generic.Dictionary<string, UIElement>
            { ["Music"] = ExpMusicSection, ["Sys"] = ExpSysSection, ["Timer"] = ExpTimerSection, ["Clip"] = ExpClipSection };
            ExpStack.Children.Clear();
            foreach (var k in order) if (map.TryGetValue(k, out var el)) ExpStack.Children.Add(el);
        }
        FrameworkElement? last = null;
        foreach (FrameworkElement el in ExpStack.Children)
        {
            el.Margin = new Thickness(0, 0, 0, 12);
            if (el.Visibility == Visibility.Visible) last = el;
        }
        if (last != null) last.Margin = new Thickness(0);
    }

    /// Предпросмотр из окна настроек: разворачивает капсулу и не даёт ей свернуться.
    public void PreviewExpanded()
    {
        _userHidden = false; ApplyVisibility();
        Hold = true;
        if (_mode != Mode.Expanded) SetMode(Mode.Expanded);
    }

    /// Проверка настроения из настроек: капсула возвращается в покой и показывает реакцию.
    public void TestFaceMood(FaceMood m)
    {
        Hold = false;
        _userHidden = false; ApplyVisibility();
        if (_mode == Mode.Expanded || _mode == Mode.Toast) SetMode(Mode.Idle);
        _brain.Poke();
        _brain.React(m, 3500);
    }

    public void ReleaseHold()
    {
        Hold = false;
        if (_mode == Mode.Expanded && !Capsule.IsMouseOver) { _collapseTimer.Stop(); _collapseTimer.Start(); }
    }

    void Preset_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).Tag is string s && int.TryParse(s, out var m)) AppServices.Timer.Start(TimeSpan.FromMinutes(m)); }
    void Stopwatch_Click(object sender, RoutedEventArgs e) => AppServices.Timer.StartStopwatch();
    void TimerStop_Click(object sender, RoutedEventArgs e) => AppServices.Timer.Stop();

    // ------------------------------------------------------------------ expanded panel
    void RefreshExpanded()
    {
        ExpMusicSection.Visibility = S.EnableMusic ? Visibility.Visible : Visibility.Collapsed;
        ExpSysSection.Visibility = S.EnableSysInfo ? Visibility.Visible : Visibility.Collapsed;
        ExpTimerSection.Visibility = S.EnableTimer ? Visibility.Visible : Visibility.Collapsed;
        ExpClipSection.Visibility = S.EnableClipboard ? Visibility.Visible : Visibility.Collapsed;
        ApplyPanelOrder();
        UpdateMusicUi(); UpdateTimerUi(); UpdateClipUi();
    }

    void UpdateMusicUi()
    {
        var m = AppServices.Media.Current;
        ExpMusic.Visibility = m.HasSession ? Visibility.Visible : Visibility.Collapsed;
        ExpNoMedia.Visibility = m.HasSession ? Visibility.Collapsed : Visibility.Visible;
        ExpTitle.Text = m.Title.Length > 0 ? m.Title : "Без названия";
        ExpArtist.Text = m.Artist.Length > 0 ? m.Artist : m.Album;
        ExpArt.Source = m.Art;
        PlayBtn.Content = m.Playing ? "\uE769" : "\uE768";
        PrevBtn.Opacity = m.CanPrev ? 1 : 0.35; NextBtn.Opacity = m.CanNext ? 1 : 0.35;
        ExpBars.IsActive = m.Playing;
        UpdateProgress();
    }

    void UpdateProgress()
    {
        if (_mode != Mode.Expanded || !S.EnableMusic) return;
        var tl = AppServices.Media.GetTimeline();
        if (tl is null) { ExpProgScale.ScaleX = 0; ExpPos.Text = ""; ExpDur.Text = ""; return; }
        var (pos, len) = tl.Value;
        ExpProgScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ExpProgScale.ScaleX = pos.TotalSeconds / len.TotalSeconds;
        ExpPos.Text = Fmt(pos); ExpDur.Text = Fmt(len);
    }

    void UpdateTimerUi()
    {
        var t = AppServices.Timer;
        ExpTimerText.Text = Fmt(t.Display);
        ExpTimerLabel.Text = t.Mode switch { TimerMode.Countdown => "Обратный отсчёт", TimerMode.Stopwatch => "Секундомер", _ => "Таймер не запущен" };
    }

    void UpdateClipUi()
    {
        ClipEmpty.Visibility = _clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    bool _tempShown = true;

    /// Столбец температуры показывается, только если Windows отдаёт данные датчика.
    void ApplyTempColumn(bool has)
    {
        if (has == _tempShown) return;
        _tempShown = has;
        TempPanel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        SysSpacer2.Width = has ? new GridLength(12) : new GridLength(0);
        SysTempCol.Width = has ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    void SampleSys()
    {
        if (_mode != Mode.Expanded || !S.EnableSysInfo) return;
        var s = AppServices.SysInfo.Sample();
        CpuText.Text = $"{s.Cpu:0}%"; RamText.Text = $"{s.Ram:0}% · {s.UsedGb:0.0} ГБ";
        ApplyTempColumn(s.TempC.HasValue);
        if (s.TempC is double t) TempText.Text = $"{t:0} °C";
        SetBar(CpuScale, s.Cpu / 100); SetBar(RamScale, s.Ram / 100);
        SetBar(TempScale, s.TempC is double t2 ? (t2 - 30) / 70 : 0);
    }

    async void Play_Click(object sender, RoutedEventArgs e) => await AppServices.Media.PlayPauseAsync();
    async void Next_Click(object sender, RoutedEventArgs e) => await AppServices.Media.NextAsync();
    async void Prev_Click(object sender, RoutedEventArgs e) => await AppServices.Media.PrevAsync();

    public void ShowToastPublic(string title, string sub) =>
        Post(new Toast { Key = "info", Mood = FaceMood.Happy, Glyph = "\uE946", Title = title, Subtitle = sub, Accent = Color.FromRgb(0x4C, 0xC2, 0xFF), DurationMs = 3500 });
}
