using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicEdge.Core;
using DynamicEdge.Services;
using WF = System.Windows.Forms;

namespace DynamicEdge.Views;

/// Расширенные возможности капсулы: вкладки, мониторинг, тексты песен, итоги, полка, перетаскивание.
public partial class IslandWindow
{
    enum Page { Home, Stats, Shelf }
    Page _page = Page.Home;

    bool _gameMon, _monBusy;
    string _layout = "", _weatherKey = "";
    LrcLine[]? _lyrics;
    string _lyricKey = "";
    int _lyricIdx = -1;

    // положение окна (для перетаскивания)
    int _winX, _winY, _winW;
    double _winSc = 1;
    System.Drawing.Point? _dragStart;
    int _dragStartOffX, _dragStartOffY;
    bool _dragged;

    Point _artDown;
    bool _artTracking;

    readonly DispatcherTimer _monTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly DispatcherTimer _histTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer _fitTimer = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    readonly DispatcherTimer _layoutTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    sealed class MonCard { public Border Root = null!; public TextBlock Value = null!; public ScaleTransform Bar = null!; }
    readonly Dictionary<string, MonCard> _cards = new();
    TextBlock[] _miniText = Array.Empty<TextBlock>();
    ScaleTransform[] _miniBar = Array.Empty<ScaleTransform>();

    void InitFeatures()
    {
        BuildMonitorCards();
        BuildMiniMon();
        _fitTimer.Tick += (_, _) => { _fitTimer.Stop(); SetWindowHeight(WindowHeightFor(_curH)); };
        _monTimer.Tick += (_, _) => SampleSys();
        _histTimer.Tick += (_, _) =>
        {
            var m = AppServices.Media.Current;
            if (S.HistoryEnabled && m.HasSession && m.Playing && m.Title.Length > 0) AppServices.History.Add(m.Artist, m.Title, 5);
            AppServices.History.FlushIfDue();
        };
        _histTimer.Start();
        _layoutTimer.Tick += (_, _) =>
        {
            var l = KeyboardLayout.Current();
            if (l.Length > 0 && l != _layout) { _layout = l; if (S.IdleShowLayout) UpdateIdle(false); }
        };
        AppServices.Weather.Changed += OnWeather;
    }

    /// Вызывается из OnSettingsChanged.
    void ApplyFeatureSettings()
    {
        try { FontFamily = new FontFamily(S.FontName); } catch { }
        UpdatePinUi();
        if (S.IdleShowLayout && !_layoutTimer.IsEnabled) _layoutTimer.Start();
        else if (!S.IdleShowLayout && _layoutTimer.IsEnabled) _layoutTimer.Stop();

        string wk = $"{S.WeatherEnabled}|{S.WeatherCity}";
        if (wk != _weatherKey) { _weatherKey = wk; AppServices.Weather.Apply(); }
        UpdateWeatherUi();

        if (!S.LyricsEnabled) { _lyrics = null; _lyricKey = ""; ExpLyric.Visibility = Visibility.Collapsed; }
        else if (AppServices.Media.Current.HasSession) LoadLyrics(AppServices.Media.Current);
    }

    // ------------------------------------------------------------------ helpers
    TextBlock T(string text, double size, bool bold = false, string brush = "FgBrush", bool wrap = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    static string FormatMinutes(int m) => m < 60 ? $"{m} мин" : $"{m / 60} ч {m % 60:00} мин";
    static string Rate(double mbps) => mbps >= 10 ? $"{mbps:0} Мбит/с" : $"{mbps:0.0} Мбит/с";

    // ------------------------------------------------------------------ вкладки панели
    void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string t) ShowPage(t == "Stats" ? Page.Stats : t == "Shelf" ? Page.Shelf : Page.Home);
    }

    void ShowPage(Page p, bool resize = true)
    {
        _page = p;
        HomePage.Visibility = p == Page.Home ? Visibility.Visible : Visibility.Collapsed;
        StatsPage.Visibility = p == Page.Stats ? Visibility.Visible : Visibility.Collapsed;
        ShelfPage.Visibility = p == Page.Shelf ? Visibility.Visible : Visibility.Collapsed;
        TabHome.Opacity = p == Page.Home ? 1 : 0.55;
        TabStats.Opacity = p == Page.Stats ? 1 : 0.55;
        TabShelf.Opacity = p == Page.Shelf ? 1 : 0.55;
        if (p == Page.Stats) BuildStats(); else if (p == Page.Shelf) BuildShelf();
        ExpScroll.ScrollToTop();
        UpdatePinUi();
        if (resize && _mode == Mode.Expanded) { ResizeExpanded(); if (p == Page.Home) SampleSys(); }
    }

    // ------------------------------------------------------------------ питомцы
    FaceMood _petMood = FaceMood.Neutral;
    bool _petMusic, _belowShown;

    static bool PetsOn => S.PetKind != "Eyes";
    static bool BelowMode => S.FaceEnabled && PetsOn && S.PetPlacement == "Below";

    /// Выбирает, кто «живёт» в капсуле: классические глазки или питомец (внутри капсулы или выглядывает снизу).
    void ApplyPetSettings()
    {
        bool pets = PetsOn, below = BelowMode;
        double unit = S.FaceSize * S.IdleHeight / 34.0;
        IdleFace.Unit = unit;
        IdlePet.Unit = unit * 0.78;
        BelowPet.Unit = unit * S.Scale;
        IdlePet.SetSpecies(S.PetKind); BelowPet.SetSpecies(S.PetKind); ToastPet.SetSpecies(S.PetKind);

        Pet? active = !pets ? null : below ? BelowPet : IdlePet;
        BelowPet.Visibility = below ? Visibility.Visible : Visibility.Collapsed;
        if (!below) { BelowPet.BeginAnimation(OpacityProperty, null); BelowPet.Opacity = 0; _belowShown = false; }

        _brain.SetTarget(active != null ? active.Eyes : IdleFace);
        _brain.FixedColor = pets ? Pet.EyeColor(S.PetKind) : null;
        _brain.StateChanged = active == null ? null : (m, music) =>
        {
            active.ApplyState(m, music);
            _petMood = m; _petMusic = music;
            UpdateBelowPet();
        };
        if (S.FaceEnabled) _brain.Start(); else _brain.Stop();
        PositionBelowPet();
        UpdateBelowPet();
    }

    /// Питомец под капсулой: справа, верхняя часть прячется за капсулой.
    void PositionBelowPet()
    {
        if (!BelowMode) return;
        double capH = Capsule.ActualHeight > 0 ? Capsule.ActualHeight : S.IdleHeight;
        double capW = Capsule.ActualWidth > 0 ? Capsule.ActualWidth : 120;
        double dx = capW * S.Scale / 2 - 44 * BelowPet.Unit / 2 - 12 * S.Scale;
        BelowPet.Margin = new Thickness(2 * dx, 6 + capH * S.Scale - 9 * S.Scale, 0, 0);
    }

    /// Выглядывает, когда в капсуле есть чем заняться (музыка, событие, наведение, сон, низкий заряд), и прячется обратно.
    void UpdateBelowPet()
    {
        if (!BelowMode) return;
        bool notable = _petMusic || _petMood != FaceMood.Neutral;
        bool show = notable && (_mode == Mode.Idle || _mode == Mode.Hover) && !_userHidden && !_gameMon;
        if (show == _belowShown) return;
        _belowShown = show;
        double h = 34 * BelowPet.Unit;
        if (!S.Animations)
        {
            BelowShift.BeginAnimation(TranslateTransform.YProperty, null); BelowShift.Y = show ? 0 : -h;
            BelowPet.BeginAnimation(OpacityProperty, null); BelowPet.Opacity = show ? 1 : 0;
            return;
        }
        BelowPet.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, Dur(show ? 200 : 160)));
        BelowShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 0 : -h, Dur(show ? 380 : 240))
        { EasingFunction = show ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } : new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    // ------------------------------------------------------------------ высота окна и прокрутка
    /// Высота окна под капсулу заданной высоты: запас сверху, на тень и на «перелёт» пружины.
    double WindowHeightFor(double capsuleH) =>
        Math.Max(120, 6 + capsuleH * S.Scale * 1.08 + 46 + (BelowMode ? 36 * S.FaceSize * S.IdleHeight / 34.0 * S.Scale : 0));

    void SetWindowHeight(double need)
    {
        if (Math.Abs(Height - need) < 1) return;
        Height = need;
        PositionWindow();
    }

    /// Окно растёт сразу (до начала анимации), а сжимается позже, когда анимация закончилась.
    void GrowWindowIfNeeded(double capsuleH)
    {
        double need = WindowHeightFor(capsuleH);
        if (need > Height + 0.5) SetWindowHeight(need);
        _fitTimer.Stop(); _fitTimer.Start();
    }

    /// Максимальная высота капсулы (в DIP до масштаба) так, чтобы она не выходила за рабочую область монитора.
    double MaxCapsuleHeight()
    {
        var screens = WF.Screen.AllScreens;
        var scr = S.Monitor >= 0 && S.Monitor < screens.Length ? screens[S.Monitor] : WF.Screen.PrimaryScreen ?? screens[0];
        double sc = _winSc > 0 ? _winSc : 1;
        double availPx = scr.WorkingArea.Bottom - (_winY + 6 * sc) - 16 * sc;
        double cap = availPx / (sc * S.Scale * 1.08);
        return Math.Clamp(cap, 260, 1400);
    }

    /// Колёсико: над блоком музыки или когда прокручивать нечего — громкость, иначе прокрутка содержимого.
    void ExpScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool overMusic = _page == Page.Home && ExpMusicSection.Visibility == Visibility.Visible && ExpMusicSection.IsMouseOver;
        if (overMusic || ExpScroll.ScrollableHeight <= 0.5)
        {
            AppServices.Output.ChangeVolume(e.Delta > 0 ? 0.02f : -0.02f);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ закрепление и перетаскивание
    void UpdatePinUi()
    {
        PinBtn.Content = S.Pinned ? "\uE718" : "\uE77A";
        PinBtn.ToolTip = S.Pinned ? "Закреплено. Нажмите, чтобы разрешить перетаскивание" : "Откреплено: перетащите капсулу мышью. Нажмите, чтобы закрепить";
        PinBtn.Opacity = S.Pinned ? 0.6 : 1;
        Capsule.Cursor = S.Pinned ? Cursors.Arrow : Cursors.SizeAll;
    }

    void Pin_Click(object sender, RoutedEventArgs e)
    {
        S.Pinned = !S.Pinned;
        UpdatePinUi();
        SettingsService.NotifyChanged();
    }

    void Capsule_Down(object sender, MouseButtonEventArgs e)
    {
        if (S.Pinned || e.ChangedButton != MouseButton.Left) return;
        _dragStart = WF.Cursor.Position;
        _dragStartOffX = S.OffsetX; _dragStartOffY = S.OffsetY;
        _dragged = false;
        Capsule.CaptureMouse();
    }

    void Capsule_Move(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } st || e.LeftButton != MouseButtonState.Pressed) return;
        var cp = WF.Cursor.Position;
        int dx = cp.X - st.X, dy = cp.Y - st.Y;
        if (!_dragged && Math.Abs(dx) + Math.Abs(dy) < 6) return;
        _dragged = true;
        S.OffsetX = _dragStartOffX + (int)Math.Round(dx / _winSc);
        S.OffsetY = _dragStartOffY + (int)Math.Round(dy / _winSc);
        PositionWindow();
    }

    /// После перетаскивания определяем, на каком мониторе оказалась капсула, и пересчитываем смещения относительно него.
    void FinishDrag()
    {
        var cp = WF.Cursor.Position;
        var screens = WF.Screen.AllScreens;
        int idx = Array.FindIndex(screens, sc => sc.Bounds.Contains(cp));
        if (idx < 0) idx = 0;
        var b = screens[idx].Bounds;
        double sc2 = Native.MonitorScale(b.X + b.Width / 2, b.Y + 10);
        double centerX = _winX + _winW / 2.0;
        S.Monitor = idx;
        S.OffsetX = (int)Math.Round((centerX - (b.X + b.Width / 2.0)) / sc2);
        S.OffsetY = (int)Math.Round((_winY - b.Y) / sc2) + 6;
        PositionWindow();
        SettingsService.NotifyChanged();
    }

    /// Колёсико над капсулой меняет громкость.
    void Capsule_Wheel(object sender, MouseWheelEventArgs e)
    {
        AppServices.Output.ChangeVolume(e.Delta > 0 ? 0.02f : -0.02f);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ мониторинг
    void BuildMonitorCards()
    {
        var defs = new (string Key, string Label, Color C)[]
        {
            ("CPU", "CPU", Color.FromRgb(0x4C, 0xC2, 0xFF)), ("RAM", "RAM", Color.FromRgb(0xFF, 0xC8, 0x3D)),
            ("GPU", "GPU", Color.FromRgb(0xFF, 0x5A, 0x5F)), ("DISK", "ДИСК", Color.FromRgb(0x5B, 0x8D, 0xEF)),
            ("UP", "ОТПРАВКА", Color.FromRgb(0x32, 0xD7, 0x4B)), ("DOWN", "ПРИЁМ", Color.FromRgb(0xBF, 0x5A, 0xF2)),
            ("TEMP", "ТЕМП.", Color.FromRgb(0xFF, 0x9F, 0x0A))
        };
        foreach (var d in defs)
        {
            var label = T(d.Label, 10, false, "Fg2Brush");
            var value = T("—", 14, true);
            var scale = new ScaleTransform(0, 1);
            var fill = new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(d.C), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = scale };
            var track = new Border { CornerRadius = new CornerRadius(1.5) };
            track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            var bar = new Grid { Height = 3, Margin = new Thickness(0, 5, 0, 0) };
            bar.Children.Add(track); bar.Children.Add(fill);
            var sp = new StackPanel();
            sp.Children.Add(label); sp.Children.Add(value); sp.Children.Add(bar);
            var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 10, 8), Margin = new Thickness(3), Child = sp };
            card.SetResourceReference(Border.BackgroundProperty, "ChipBg");
            _cards[d.Key] = new MonCard { Root = card, Value = value, Bar = scale };
            MonGrid.Children.Add(card);
        }
    }

    bool SetCard(string key, bool avail, string text, double frac)
    {
        var c = _cards[key];
        var vis = avail ? Visibility.Visible : Visibility.Collapsed;
        bool changed = c.Root.Visibility != vis;
        c.Root.Visibility = vis;
        if (avail) { c.Value.Text = text; SetBar(c.Bar, frac); }
        return changed;
    }

    void UpdateMonitorCards(MonitorSample s)
    {
        bool ch = false;
        ch |= SetCard("CPU", true, $"{s.Cpu:0}%", s.Cpu / 100);
        ch |= SetCard("RAM", true, $"{s.UsedGb:0.0} / {s.TotalGb:0.0} ГБ", s.Ram / 100);
        ch |= SetCard("GPU", s.Gpu.HasValue, $"{(s.Gpu ?? 0):0}%", (s.Gpu ?? 0) / 100);
        ch |= SetCard("DISK", s.Disk.HasValue, $"{(s.Disk ?? 0):0}%", (s.Disk ?? 0) / 100);
        ch |= SetCard("UP", s.UpMbps.HasValue, Rate(s.UpMbps ?? 0), (s.UpMbps ?? 0) / 100);
        ch |= SetCard("DOWN", s.DownMbps.HasValue, Rate(s.DownMbps ?? 0), (s.DownMbps ?? 0) / 100);
        ch |= SetCard("TEMP", s.TempC.HasValue, $"{(s.TempC ?? 0):0} °C", ((s.TempC ?? 30) - 30) / 70);
        if (ch && _mode == Mode.Expanded) ResizeExpanded();
    }

    void BuildMiniMon()
    {
        string[] names = { "CPU", "RAM", "GPU", "ДИСК" };
        Color[] cols = { Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0xFF, 0xC8, 0x3D), Color.FromRgb(0xFF, 0x5A, 0x5F), Color.FromRgb(0x5B, 0x8D, 0xEF) };
        _miniText = new TextBlock[4]; _miniBar = new ScaleTransform[4];
        for (int i = 0; i < 4; i++)
        {
            var t = T(names[i] + " —", 10, true);
            t.MinWidth = 46;
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            var sc = new ScaleTransform(0, 1);
            var fill = new Border { CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(cols[i]), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = sc };
            var track = new Border { CornerRadius = new CornerRadius(1) };
            track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            var bar = new Grid { Height = 2, Margin = new Thickness(0, 2, 0, 0) };
            bar.Children.Add(track); bar.Children.Add(fill);
            var sp = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(t); sp.Children.Add(bar);
            _miniText[i] = t; _miniBar[i] = sc;
            IdleMon.Children.Add(sp);
        }
    }

    void UpdateIdleMon(MonitorSample s)
    {
        _miniText[0].Text = $"CPU {s.Cpu:0}%"; SetBar(_miniBar[0], s.Cpu / 100);
        _miniText[1].Text = $"RAM {s.Ram:0}%"; SetBar(_miniBar[1], s.Ram / 100);
        _miniText[2].Text = s.Gpu is double g ? $"GPU {g:0}%" : "GPU —"; SetBar(_miniBar[2], (s.Gpu ?? 0) / 100);
        _miniText[3].Text = s.Disk is double d ? $"ДИСК {d:0}%" : "ДИСК —"; SetBar(_miniBar[3], (s.Disk ?? 0) / 100);
    }

    bool IdleMonWanted => S.IdleShowMonitor || _gameMon;

    void ApplyIdleMon()
    {
        bool on = IdleMonWanted;
        IdleMon.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (_gameMon)
        {
            MicDot.Visibility = Visibility.Collapsed; IdleArt.Visibility = Visibility.Collapsed; IdleFace.Visibility = Visibility.Collapsed; IdlePet.Visibility = Visibility.Collapsed;
            IdleBars.Visibility = Visibility.Collapsed; IdleText.Visibility = Visibility.Collapsed;
        }
        if (on && !_monTimer.IsEnabled) { _monTimer.Start(); SampleSys(); }
        else if (!on && _monTimer.IsEnabled) _monTimer.Stop();
    }

    async void SampleSys()
    {
        bool exp = _mode == Mode.Expanded && S.EnableSysInfo && _page == Page.Home;
        bool idle = IdleMonWanted && (_mode == Mode.Idle || _mode == Mode.Hover);
        if ((!exp && !idle) || _monBusy) return;
        _monBusy = true;
        try
        {
            var s = await AppServices.Monitor.SampleAsync();
            if (exp && _mode == Mode.Expanded) UpdateMonitorCards(s);
            if (idle && (_mode == Mode.Idle || _mode == Mode.Hover)) UpdateIdleMon(s);
        }
        catch (Exception ex) { SettingsService.Log("Monitor: " + ex.Message); }
        finally { _monBusy = false; }
    }

    // ------------------------------------------------------------------ режим «в игре»
    /// Возвращает true, если нужно скрыть капсулу целиком.
    bool UpdateGameState(bool fullscreen)
    {
        bool hide = fullscreen && S.GameMode == "Hide";
        bool mon = fullscreen && S.GameMode == "Monitor";
        if (mon != _gameMon)
        {
            _gameMon = mon;
            if (mon && _mode != Mode.Idle) SetMode(Mode.Idle);
            UpdateIdle(true);
        }
        return hide;
    }

    // ------------------------------------------------------------------ погода
    void OnWeather()
    {
        UpdateWeatherUi();
        if (_mode == Mode.Expanded) { ApplyPanelOrder(); ResizeExpanded(); }
        UpdateIdle(false);
    }

    void UpdateWeatherUi()
    {
        var w = AppServices.Weather.Current;
        bool on = S.WeatherEnabled && w != null;
        ExpWeatherSection.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on || w == null) return;
        var (emoji, text) = WeatherService.Describe(w.Code);
        WeatherEmoji.Text = emoji;
        WeatherTemp.Text = $"{w.Temp:0}°  ·  ощущается как {w.Feels:0}°";
        WeatherDesc.Text = $"{text}, {w.City}";
    }

    // ------------------------------------------------------------------ тексты песен, информация о треке, «поделиться»
    async void LoadLyrics(MediaInfo info)
    {
        if (!S.LyricsEnabled || info.Title.Length == 0) { _lyrics = null; _lyricKey = ""; ExpLyric.Visibility = Visibility.Collapsed; return; }
        string key = info.Artist + "|" + info.Title;
        if (key == _lyricKey) return;
        _lyricKey = key; _lyrics = null; _lyricIdx = -1;
        ExpLyric.Visibility = Visibility.Collapsed;
        int dur = AppServices.Media.GetTimeline() is { } tl ? (int)tl.len.TotalSeconds : 0;
        var res = await AppServices.Lyrics.GetAsync(info.Artist, info.Title, dur);
        if (key != _lyricKey) return;
        _lyrics = res;
        ExpLyric.Visibility = res != null ? Visibility.Visible : Visibility.Collapsed;
        ExpLyric.Text = "";
        if (_mode == Mode.Expanded) ResizeExpanded();
    }

    void UpdateLyric(TimeSpan pos)
    {
        if (_lyrics == null || !S.LyricsEnabled) return;
        int i = LyricsService.CurrentIndex(_lyrics, pos);
        if (i == _lyricIdx) return;
        _lyricIdx = i;
        ExpLyric.Text = i >= 0 ? _lyrics[i].Text : "";
    }

    void UpdateMusicExtras(MediaInfo m)
    {
        ExpLyric.Visibility = S.LyricsEnabled && _lyrics != null && m.HasSession ? Visibility.Visible : Visibility.Collapsed;
        if (ExpInfo.Visibility == Visibility.Visible) FillInfo(m);
    }

    static string CleanApp(string id)
    {
        if (string.IsNullOrEmpty(id)) return "неизвестен";
        string s = id.Split('!')[0];
        int u = s.IndexOf('_');
        if (u > 0) s = s[..u];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s;
    }

    void FillInfo(MediaInfo m)
    {
        InfoAlbum.Text = m.Album.Length > 0 ? "Альбом: " + m.Album : "Альбом: не указан";
        InfoDuration.Text = AppServices.Media.GetTimeline() is { } t ? "Длительность: " + Fmt(t.len) : "Длительность: неизвестна";
        InfoSource.Text = "Источник: " + CleanApp(m.App);
    }

    void Info_Click(object sender, RoutedEventArgs e)
    {
        bool show = ExpInfo.Visibility != Visibility.Visible;
        ExpInfo.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) FillInfo(AppServices.Media.Current);
        if (_mode == Mode.Expanded) ResizeExpanded();
    }

    void CopyTitle_Click(object sender, RoutedEventArgs e)
    {
        var m = AppServices.Media.Current;
        string s = m.Artist.Length > 0 ? $"{m.Artist} — {m.Title}" : m.Title;
        try { _suppressClip = true; Clipboard.SetText(s); }
        catch { _suppressClip = false; }
    }

    void Share_Click(object sender, RoutedEventArgs e)
    {
        var m = AppServices.Media.Current;
        if (!m.HasSession) return;
        try
        {
            const int W = 560, H = 260;
            var accent = m.Accent;
            var bg = new LinearGradientBrush(Color.FromRgb(0x14, 0x14, 0x1E),
                Color.FromRgb((byte)(accent.R / 3), (byte)(accent.G / 3), (byte)(accent.B / 3)), 35);
            var grid = new Grid { Width = W, Height = H, Background = bg };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(H) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (m.Art != null)
            {
                var img = new Image { Source = m.Art, Margin = new Thickness(30), Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0, 0, 200, 200), 22, 22) };
                grid.Children.Add(img);
            }
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
            sp.Children.Add(new TextBlock { Text = m.Title, FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, FontFamily = FontFamily });
            sp.Children.Add(new TextBlock { Text = m.Artist, FontSize = 18, Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), Margin = new Thickness(0, 6, 0, 0), FontFamily = FontFamily });
            sp.Children.Add(new TextBlock { Text = "Dynamic Edge", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), Margin = new Thickness(0, 24, 0, 0), FontFamily = FontFamily });
            Grid.SetColumn(sp, 1); grid.Children.Add(sp);
            grid.Measure(new Size(W, H)); grid.Arrange(new Rect(0, 0, W, H)); grid.UpdateLayout();
            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(grid);
            _suppressClip = true;
            Clipboard.SetImage(rtb);
            ShowToastPublic("Карточка скопирована", "Вставьте её в чат или документ");
        }
        catch (Exception ex) { _suppressClip = false; SettingsService.Log("Share: " + ex.Message); }
    }

    // ------------------------------------------------------------------ жесты на обложке
    void Art_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2) { _artTracking = false; _ = AppServices.Media.PlayPauseAsync(); return; }
        _artDown = e.GetPosition(this);
        _artTracking = true;
        ExpArtBox.CaptureMouse();
    }

    void Art_Up(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_artTracking) return;
        _artTracking = false;
        ExpArtBox.ReleaseMouseCapture();
        double d = e.GetPosition(this).X - _artDown.X;
        if (d < -40) _ = AppServices.Media.NextAsync();
        else if (d > 40) _ = AppServices.Media.PrevAsync();
    }

    // ------------------------------------------------------------------ итоги недели
    void BuildStats()
    {
        StatsHost.Children.Clear();
        if (!S.HistoryEnabled)
        {
            StatsHost.Children.Add(T("История прослушивания отключена: включите её в настройках, вкладка «Сервисы».", 12, false, "Fg2Brush", true));
            return;
        }
        var sum = AppServices.History.Summary();
        StatsHost.Children.Add(T("ЗА НЕДЕЛЮ", 11, false, "Fg2Brush"));
        StatsHost.Children.Add(T(FormatMinutes(sum.TotalMinutes), 28, true));
        StatsHost.Children.Add(T(sum.DaysWithMusic == 0 ? "Пока нет данных: включите музыку" : $"музыка в {sum.DaysWithMusic} из 7 дней", 12, false, "Fg2Brush"));

        var chart = new Grid { Margin = new Thickness(0, 12, 0, 12), Height = 78 };
        double max = Math.Max(1, sum.DayMinutes.Max());
        for (int i = 0; i < 7; i++) chart.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 7; i++)
        {
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
            var bar = new Border { Width = 14, Height = Math.Max(3, sum.DayMinutes[i] / max * 52), CornerRadius = new CornerRadius(7), Opacity = sum.DayMinutes[i] > 0.5 ? 1 : 0.25 };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var lbl = T(sum.DayLabels[i], 10, false, "Fg2Brush");
            lbl.HorizontalAlignment = HorizontalAlignment.Center; lbl.Margin = new Thickness(0, 4, 0, 0);
            col.Children.Add(bar); col.Children.Add(lbl);
            Grid.SetColumn(col, i);
            chart.Children.Add(col);
        }
        StatsHost.Children.Add(chart);
        AddTop("ИСПОЛНИТЕЛИ", sum.Artists);
        AddTop("ТРЕКИ", sum.Tracks);
    }

    void AddTop(string title, List<(string Name, double Minutes)> rows)
    {
        if (rows.Count == 0) return;
        var h = T(title, 11, false, "Fg2Brush"); h.Margin = new Thickness(0, 4, 0, 4);
        StatsHost.Children.Add(h);
        double max = Math.Max(0.1, rows.Max(r => r.Minutes));
        foreach (var (name, minutes) in rows)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = T(name, 12); n.TextTrimming = TextTrimming.CharacterEllipsis;
            var m = T(minutes < 1 ? "<1 мин" : $"{minutes:0} мин", 11, false, "Fg2Brush"); m.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(m, 1);
            var sc = new ScaleTransform(Math.Clamp(minutes / max, 0.03, 1), 1);
            var bar = new Border { Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = sc, Margin = new Thickness(0, 0, 0, -4) };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            Grid.SetColumnSpan(bar, 2);
            g.Children.Add(n); g.Children.Add(m); g.Children.Add(bar);
            StatsHost.Children.Add(g);
        }
    }

    // ------------------------------------------------------------------ полка со ссылками
    void BuildShelf()
    {
        ShelfHost.Children.Clear();
        var links = S.ShelfLinks.ToList();
        ShelfEmpty.Text = "Перетащите ссылку из браузера на капсулу или нажмите «+ Из буфера»";
        ShelfEmpty.Visibility = links.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var url in links) ShelfHost.Children.Add(MakeTile(url));
    }

    FrameworkElement MakeTile(string url)
    {
        string name = ShelfService.DisplayName(url);
        var btn = new Button { Style = (Style)FindResource("ChipBtn"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(6, 8, 6, 6), Width = 72, ToolTip = url };
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var host = new Grid { Width = 32, Height = 32, Margin = new Thickness(0, 0, 0, 4) };
        var letter = T(name.Length > 0 ? name[..1].ToUpperInvariant() : "?", 18, true);
        letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
        var img = new Image { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        host.Children.Add(letter); host.Children.Add(img);
        var label = T(name, 10, false, "Fg2Brush");
        label.TextTrimming = TextTrimming.CharacterEllipsis; label.TextAlignment = TextAlignment.Center; label.MaxWidth = 60;
        sp.Children.Add(host); sp.Children.Add(label);
        btn.Content = sp;
        btn.Click += (_, _) => ShelfService.Open(url);
        btn.MouseRightButtonUp += (_, ev) => { ev.Handled = true; ShelfService.Remove(url); BuildShelf(); if (_mode == Mode.Expanded) ResizeExpanded(); };
        _ = LoadTileIcon(url, img, letter);
        return btn;
    }

    async Task LoadTileIcon(string url, Image img, TextBlock letter)
    {
        var ic = await ShelfService.GetIconAsync(url);
        if (ic == null) return;
        img.Source = ic; img.Visibility = Visibility.Visible; letter.Visibility = Visibility.Collapsed;
    }

    void AddShelf(string url)
    {
        ShelfService.Add(url);
        BuildShelf();
        if (_mode == Mode.Expanded) ResizeExpanded();
    }

    void ShelfAddClip_Click(object sender, RoutedEventArgs e)
    {
        string? text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
        var url = ShelfService.NormalizeUrl(text);
        if (url != null) { AddShelf(url); return; }
        ShelfEmpty.Text = "В буфере обмена нет ссылки";
        ShelfEmpty.Visibility = Visibility.Visible;
        if (_mode == Mode.Expanded) ResizeExpanded();
    }

    void ShelfClear_Click(object sender, RoutedEventArgs e)
    {
        ShelfService.Clear();
        BuildShelf();
        if (_mode == Mode.Expanded) ResizeExpanded();
    }

    static string? DragUrl(IDataObject d)
    {
        try
        {
            foreach (var f in new[] { DataFormats.UnicodeText, DataFormats.Text })
                if (d.GetDataPresent(f) && d.GetData(f) is string s)
                {
                    var u = ShelfService.NormalizeUrl(s);
                    if (u != null) return u;
                }
        }
        catch { }
        return null;
    }

    void Capsule_DragEnter(object sender, DragEventArgs e)
    {
        var url = DragUrl(e.Data);
        e.Handled = true;
        if (url == null) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        if (_userHidden || _gameMon) return;
        if (_mode != Mode.Expanded) { Hold = false; SetMode(Mode.Expanded); }
        if (_page != Page.Shelf) ShowPage(Page.Shelf);
    }

    void Capsule_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var url = DragUrl(e.Data);
        if (url != null) AddShelf(url);
    }
}
