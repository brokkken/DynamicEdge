using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicEdge.Core;
using DynamicEdge.Services;
using WF = System.Windows.Forms;

namespace DynamicEdge.Views;

/// «Характер» глазок: решает, какое сейчас настроение, куда смотреть, когда моргать, засыпать и танцевать под музыку.
public sealed class FaceBrain
{
    static AppSettings S => SettingsService.Current;
    Face _face;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(70) };
    readonly DispatcherTimer _blink = new();
    readonly Random _rnd = new();
    FaceMood? _react;
    DateTime _reactUntil;
    FaceMood _shown = (FaceMood)(-1);
    Color _lastColor;
    bool _sleeping, _colorSet;
    double _lx, _ly, _level, _wx, _wy;
    DateTime _nextWander = DateTime.MinValue;

    bool _musicNow, _lastMusic;
    public bool Hover { get; set; }
    /// Фиксированный цвет глаз (питомцы), иначе цвет из настроек.
    public Color? FixedColor { get; set; }
    /// Уведомление о смене настроения/музыки (для аксессуаров питомца).
    public Action<FaceMood, bool>? StateChanged { get; set; }

    /// Переключает, каким глазам управляет «характер» (классические глазки или глаза питомца).
    public void SetTarget(Face f)
    {
        if (ReferenceEquals(f, _face)) return;
        _face.SetLevel(0); _face.Look(0, 0);
        _face = f;
        _shown = (FaceMood)(-1); _colorSet = false; _lastMusic = !_musicNow;
    }

    public FaceBrain(Face face)
    {
        _face = face;
        _tick.Tick += (_, _) => Tick();
        _blink.Tick += (_, _) => OnBlink();
    }

    public void Start()
    {
        if (_tick.IsEnabled) return;
        _shown = (FaceMood)(-1);
        _tick.Start();
        ScheduleBlink();
    }

    public void Stop()
    {
        _tick.Stop(); _blink.Stop();
        _face.SetLevel(0); _face.Look(0, 0);
    }

    /// Временная реакция на событие.
    public void React(FaceMood m, int ms)
    {
        if (!S.FaceEnabled) return;
        _react = m; _reactUntil = DateTime.UtcNow.AddMilliseconds(ms);
        if (m is FaceMood.Surprised or FaceMood.Message or FaceMood.Happy or FaceMood.Excited && _face.IsVisible) _face.Bounce();
        Resolve();
    }

    /// Пользователь взаимодействует с капсулой: просыпаемся.
    public void Poke()
    {
        if (!_sleeping) return;
        _sleeping = false;
        React(FaceMood.Surprised, 900);
    }

    static Color BaseColor() => S.FaceColor switch
    {
        "Accent" => (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? ThemeManager.DefaultAccent,
        "Cyan" => Color.FromRgb(0x5A, 0xC8, 0xFA),
        "Green" => Color.FromRgb(0x32, 0xD7, 0x4B),
        "Pink" => Color.FromRgb(0xFF, 0x6E, 0xC7),
        "Orange" => Color.FromRgb(0xFF, 0x9F, 0x0A),
        _ => ThemeManager.IsLight ? Color.FromRgb(0x11, 0x11, 0x11) : Colors.White
    };

    Color EyeColor(FaceMood m) => FixedColor ?? (m switch
    {
        FaceMood.Tired => Color.FromRgb(0xFF, 0x45, 0x3A),
        FaceMood.Alert => Color.FromRgb(0xFF, 0x9F, 0x0A),
        _ => BaseColor()
    });

    void Resolve()
    {
        var b = AppServices.Battery;
        FaceMood m;
        if (_react != null) m = _react.Value;
        else if (_sleeping) m = FaceMood.Sleeping;
        else if (S.EnableMic && AppServices.MicUsage.Active) m = FaceMood.Alert;
        else if (S.EnableBattery && b.HasBattery && !b.Charging && b.Percent <= S.LowBatteryPercent) m = FaceMood.Tired;
        else if (Hover) m = FaceMood.Happy;
        else m = FaceMood.Neutral;

        var color = EyeColor(m);
        if (!_colorSet || color != _lastColor) { _colorSet = true; _lastColor = color; _face.Foreground = new SolidColorBrush(color); }
        bool changed = m != _shown;
        if (changed) { _shown = m; _face.SetMood(m); }
        if (changed || _musicNow != _lastMusic) { _lastMusic = _musicNow; StateChanged?.Invoke(m, _musicNow); }
    }

    void Tick()
    {
        if (!S.FaceEnabled || !_face.IsVisible) return;
        try
        {
            var now = DateTime.UtcNow;
            if (_react != null && now > _reactUntil) _react = null;

            var media = AppServices.Media.Current;
            bool music = S.FaceDance && S.EnableMusic && media.HasSession && media.Playing;
            _musicNow = music;

            // сон: нет ввода пользователя заданное время и не играет музыка
            double idle = Native.IdleSeconds();
            int sm = S.FaceSleepMinutes;
            if (!_sleeping && sm > 0 && !music && idle > sm * 60.0) _sleeping = true;
            else if (_sleeping && (idle < 2 || music)) { _sleeping = false; _react = FaceMood.Surprised; _reactUntil = now.AddMilliseconds(900); _face.Bounce(); }

            // взгляд
            double tx = 0, ty = 0;
            if (!_sleeping && _shown != FaceMood.Closed)
            {
                if (S.FaceFollowCursor)
                {
                    var cp = WF.Cursor.Position;
                    var c = _face.PointToScreen(new Point(_face.ActualWidth / 2, _face.ActualHeight / 2));
                    tx = Math.Clamp((cp.X - c.X) / 250.0, -1, 1);
                    ty = Math.Clamp((cp.Y - c.Y) / 160.0, -1, 1);
                }
                else
                {
                    if (now > _nextWander)
                    {
                        _wx = _rnd.NextDouble() * 2 - 1; _wy = (_rnd.NextDouble() * 2 - 1) * 0.5;
                        _nextWander = now.AddMilliseconds(1500 + _rnd.Next(0, 2500));
                    }
                    tx = _wx; ty = _wy;
                }
            }
            _lx += (tx - _lx) * 0.4; _ly += (ty - _ly) * 0.4;
            _face.Look(_lx, _ly);

            // звук: уровень громкости устройства вывода, либо «бит» во время восторга
            double target = 0;
            if (music) target = Math.Clamp(Math.Pow(AppServices.Output.GetPeak(), 0.55) * 1.2, 0, 1);
            else if (_react == FaceMood.Excited) target = 0.5 + 0.5 * Math.Sin(now.Ticks / 1e7 * 9);
            _level = Math.Max(target, _level * 0.8);
            _face.SetLevel(S.Animations ? _level : 0);
            _tick.Interval = TimeSpan.FromMilliseconds(music || _react == FaceMood.Excited ? 40 : 70);

            Resolve();
        }
        catch (Exception ex) { SettingsService.Log("FaceBrain: " + ex.Message); }
    }

    void ScheduleBlink()
    {
        _blink.Interval = TimeSpan.FromMilliseconds(2200 + _rnd.Next(0, 3800));
        _blink.Start();
    }

    void OnBlink()
    {
        _blink.Stop();
        if (S.FaceEnabled && _face.IsVisible && _shown is not (FaceMood.Sleeping or FaceMood.Closed or FaceMood.Happy))
        {
            _face.Blink();
            if (_rnd.Next(100) < 18)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                t.Tick += (_, _) => { t.Stop(); _face.Blink(); };
                t.Start();
            }
        }
        ScheduleBlink();
    }
}
