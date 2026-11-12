using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicEdge.Core;

namespace DynamicEdge.Views;

public enum FaceMood { Neutral, Happy, Surprised, Sleeping, Tired, Alert, Excited, Focused, Closed }

/// Глазки: два овала, которые моргают, смотрят по сторонам, меняют форму под настроение и «танцуют» под уровень звука.
public partial class Face : UserControl
{
    static AppSettings S => SettingsService.Current;

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(double), typeof(Face), new PropertyMetadata(1.0, (d, _) => ((Face)d).ApplyUnit()));
    public double Unit { get => (double)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public FaceMood Mood { get; private set; } = FaceMood.Neutral;

    public Face() { InitializeComponent(); ApplyUnit(); }

    void ApplyUnit() { if (UnitScale != null) UnitScale.ScaleX = UnitScale.ScaleY = Math.Max(0.3, Unit); }

    static void A(DependencyObject target, DependencyProperty p, double to, int ms)
    {
        var an = (IAnimatable)target;
        if (!S.Animations || ms <= 0) { an.BeginAnimation(p, null); target.SetValue(p, to); return; }
        an.BeginAnimation(p, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms / Math.Max(0.3, S.AnimSpeed)))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
    }

    void Shape(double sx, double sy, double rot, bool arcs, int ms)
    {
        foreach (var st in new[] { ShapeL, ShapeR })
        {
            A(st, ScaleTransform.ScaleXProperty, sx, ms);
            A(st, ScaleTransform.ScaleYProperty, sy, ms);
        }
        A(RotL, RotateTransform.AngleProperty, rot, ms);
        A(RotR, RotateTransform.AngleProperty, -rot, ms);
        foreach (var e in new UIElement[] { EyeL, EyeR }) A(e, OpacityProperty, arcs ? 0 : 1, ms);
        foreach (var e in new UIElement[] { ArcL, ArcR }) A(e, OpacityProperty, arcs ? 1 : 0, ms);
    }

    public void SetMood(FaceMood m, bool instant = false)
    {
        Mood = m;
        int ms = instant ? 0 : 220;
        switch (m)
        {
            case FaceMood.Happy: Shape(1, 1, 0, true, ms); break;
            case FaceMood.Surprised: Shape(1.25, 1.3, 0, false, ms); break;
            case FaceMood.Sleeping:
            case FaceMood.Closed: Shape(1.15, 0.1, 0, false, ms); break;
            case FaceMood.Tired: Shape(1, 0.5, 14, false, ms); break;
            case FaceMood.Alert: Shape(1.15, 1.2, 0, false, ms); break;
            case FaceMood.Excited: Shape(1.1, 1.1, 0, false, ms); break;
            case FaceMood.Focused: Shape(1.05, 0.6, 0, false, ms); break;
            default: Shape(1, 1, 0, false, ms); break;
        }
        Zzz(m == FaceMood.Sleeping);
    }

    /// Куда смотрят глазки: x, y в диапазоне -1..1.
    public void Look(double x, double y)
    {
        LookL.X = LookR.X = x * 3.0;
        LookL.Y = LookR.Y = y * 2.2;
    }

    /// Уровень звука 0..1: глазки подпрыгивают и «дышат».
    public void SetLevel(double l)
    {
        l = Math.Clamp(l, 0, 1);
        DanceShift.Y = -l * 3.0;
        DanceScale.ScaleY = 1 + l * 0.22;
        DanceScale.ScaleX = 1 - l * 0.06;
    }

    public void Blink()
    {
        if (!S.Animations) return;
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))));
        BlinkL.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        BlinkR.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// Короткий подпрыг (радость, удивление, восторг).
    public void Bounce()
    {
        if (!S.Animations) return;
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        foreach (var (ms, v) in new[] { (0, 0.0), (110, -4.0), (220, 0.0), (330, -2.5), (440, 0.0) })
            a.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        DanceShift.BeginAnimation(TranslateTransform.YProperty, a);
    }

    void Zzz(bool on)
    {
        foreach (var (z, t, delay) in new[] { (Z1, Z1T, 0), (Z2, Z2T, 700) })
        {
            z.BeginAnimation(OpacityProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
            if (!on) { z.Opacity = 0; continue; }
            if (!S.Animations) { z.Opacity = 0.7; continue; }
            z.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.85, TimeSpan.FromMilliseconds(900))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(delay) });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(2, -7, TimeSpan.FromMilliseconds(1800))
            { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(delay) });
        }
    }
}
