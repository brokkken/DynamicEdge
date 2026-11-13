using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicEdge.Core;

namespace DynamicEdge.Views;

/// Питомцы: оригинальные векторные персонажи (пингвин «Пикс», ёжик «Колючка», медуза «Глоу»).
/// Глаза (Face) работают как раньше; питомец добавляет тело и аксессуары: наушники под музыку, конверт, «z», капельку пота.
public sealed class Pet : UserControl
{
    static AppSettings S => SettingsService.Current;

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(double), typeof(Pet), new PropertyMetadata(1.0, (d, _) => ((Pet)d).ApplyUnit()));
    public double Unit { get => (double)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public Face Eyes { get; } = new() { Unit = 0.42 };

    public static readonly string[] Kinds = { "Penguin", "Hedgehog", "Jelly" };
    public static string Title(string kind) => kind switch
    {
        "Penguin" => "Пингвин «Пикс»", "Hedgehog" => "Ёжик «Колючка»", "Jelly" => "Медуза «Глоу»", _ => "Глазки"
    };

    sealed record Spec(string Eye, double EyeY, double Hx1, double Hx2, double Hy, double Hr);
    static readonly Dictionary<string, Spec> Specs = new()
    {
        ["Penguin"] = new("#1B2233", 15.1, 8, 36, 13, 14),
        ["Hedgehog"] = new("#2A1D18", 20.0, 11, 33, 17, 11),
        ["Jelly"] = new("#2A1B55", 12.0, 8, 36, 12, 14)
    };

    public static Color EyeColor(string kind) =>
        (Color)ColorConverter.ConvertFromString(Specs.TryGetValue(kind, out var s) ? s.Eye : "#1B2233");

    readonly Canvas _body = new() { Width = 44, Height = 34 };
    readonly Canvas _acc = new() { Width = 44, Height = 34 };
    readonly ScaleTransform _unit = new(1, 1);
    readonly TranslateTransform _bob = new();
    readonly List<RotateTransform> _sway = new();
    readonly Canvas _hp = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock _n1, _n2, _z1, _z2, _alert;
    readonly TranslateTransform _n1T = new(), _n2T = new(), _z1T = new(), _z2T = new();
    readonly Border _mail;
    readonly Path _sweat;
    string _species = "";

    static SolidColorBrush B(string hex, double opacity = 1)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        if (opacity < 1) b.Opacity = opacity;
        return b;
    }

    static Ellipse E(double x, double y, double w, double h, Brush fill, double rot = 0)
    {
        var e = new Ellipse { Width = w, Height = h, Fill = fill };
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        if (rot != 0) { e.RenderTransformOrigin = new Point(0.5, 0.5); e.RenderTransform = new RotateTransform(rot); }
        return e;
    }

    static Path P(string data, Brush? fill, Brush? stroke = null, double th = 0)
    {
        var p = new Path { Data = Geometry.Parse(data), Fill = fill };
        if (stroke != null) { p.Stroke = stroke; p.StrokeThickness = th; p.StrokeStartLineCap = PenLineCap.Round; p.StrokeEndLineCap = PenLineCap.Round; }
        return p;
    }

    static TextBlock Txt(string s, double size, Brush b, double x, double y, TranslateTransform t)
    {
        var tb = new TextBlock { Text = s, FontSize = size, FontWeight = FontWeights.Bold, Foreground = b, Opacity = 0, FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"), RenderTransform = t };
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
        return tb;
    }

    public Pet()
    {
        IsHitTestVisible = false;
        Eyes.LookGain = 2.2;
        Eyes.SetZBrush(Brushes.Transparent);
        _body.RenderTransform = _bob;

        _n1 = Txt("♪", 9, B("#FFD166"), 1, 0, _n1T);
        _n2 = Txt("♫", 8, B("#FFD166"), 35, 1, _n2T);
        _z1 = Txt("z", 8, B("#CFE3FF"), 35, 2, _z1T);
        _z2 = Txt("Z", 10, B("#CFE3FF"), 38, -3, _z2T);
        _alert = Txt("!", 12, B("#FF453A"), 36, -3, new TranslateTransform());
        _alert.Opacity = 1; _alert.Visibility = Visibility.Collapsed;

        var env = new TextBlock { Text = "✉", FontSize = 9, Foreground = B("#222"), FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _mail = new Border { Width = 15, Height = 12, CornerRadius = new CornerRadius(4), Background = Brushes.White, Child = env, Visibility = Visibility.Collapsed };
        Canvas.SetLeft(_mail, 29); Canvas.SetTop(_mail, -4);
        _sweat = P("M 35,4 C 35,4 32,9 35,10.5 C 38,9 35,4 35,4 Z", B("#6EC6FF"));
        _sweat.Visibility = Visibility.Collapsed;

        _acc.Children.Add(_hp); _acc.Children.Add(_n1); _acc.Children.Add(_n2); _acc.Children.Add(_z1); _acc.Children.Add(_z2);
        _acc.Children.Add(_mail); _acc.Children.Add(_sweat); _acc.Children.Add(_alert);

        var root = new Canvas { Width = 44, Height = 34 };
        root.Children.Add(_body); root.Children.Add(_acc);
        var host = new Grid { Width = 44, Height = 34, LayoutTransform = _unit };
        host.Children.Add(root);
        Content = host;
        IsVisibleChanged += (_, _) => UpdateAnim();
        SetSpecies("Penguin");
    }

    void ApplyUnit() => _unit.ScaleX = _unit.ScaleY = Math.Max(0.3, Unit);

    public void SetSpecies(string kind)
    {
        if (!Specs.ContainsKey(kind)) kind = "Penguin";
        if (kind == _species) return;
        _species = kind;
        Build();
    }

    void Build()
    {
        var spec = Specs[_species];
        _body.Children.Clear();
        _sway.Clear();
        switch (_species)
        {
            case "Hedgehog":
                _body.Children.Add(P("M 4,26 L 7,15 L 10,19 L 11,8 L 15,14 L 18,3 L 22,11 L 26,3 L 29,14 L 33,8 L 34,19 L 37,15 L 40,26 Z", B("#5B3D2E")));
                _body.Children.Add(E(7, 10, 30, 22, B("#8F5E47")));
                _body.Children.Add(E(10, 12, 6, 6, B("#F4D6B8")));
                _body.Children.Add(E(28, 12, 6, 6, B("#F4D6B8")));
                _body.Children.Add(E(11, 15, 22, 17, B("#F4D6B8")));
                _body.Children.Add(E(20, 25, 4, 3, B("#3A2A24")));
                _body.Children.Add(E(12, 30, 8, 4, B("#E0B892")));
                _body.Children.Add(E(24, 30, 8, 4, B("#E0B892")));
                _body.Children.Add(E(12.5, 23, 4, 3, B("#FF9AA2", 0.6)));
                _body.Children.Add(E(27.5, 23, 4, 3, B("#FF9AA2", 0.6)));
                break;
            case "Jelly":
            {
                foreach (var (d, ph) in new[] { ("M 13,20 C 11,24 15,27 13,32", 0), ("M 19,21 C 17,25 21,28 19,33", 1), ("M 25,21 C 23,25 27,28 25,33", 2), ("M 31,20 C 29,24 33,27 31,32", 3) })
                {
                    var t = P(d, null, B("#B79CFF"), 2.4);
                    var rt = new RotateTransform(0, 22, 20);
                    t.RenderTransform = rt;
                    _sway.Add(rt);
                    _body.Children.Add(t);
                }
                var dome = E(7, 2, 30, 22, new LinearGradientBrush(Color.FromRgb(0xC8, 0xA8, 0xFF), Color.FromRgb(0x8A, 0x63, 0xFF), 90));
                dome.Opacity = 0.96;
                _body.Children.Add(dome);
                _body.Children.Add(E(12, 5, 8, 4, B("#FFFFFF", 0.35), -20));
                _body.Children.Add(E(11, 15, 5, 3, B("#FF9AD5", 0.55)));
                _body.Children.Add(E(28, 15, 5, 3, B("#FF9AD5", 0.55)));
                break;
            }
            default:
                _body.Children.Add(E(3, 14, 7, 15, B("#232F47"), -18));
                _body.Children.Add(E(34, 14, 7, 15, B("#232F47"), 18));
                _body.Children.Add(E(8, 2, 28, 32, B("#2B3A55")));
                _body.Children.Add(E(13, 10, 18, 22, B("#F6F2EA")));
                _body.Children.Add(E(11, 30, 10, 5, B("#FFA94D")));
                _body.Children.Add(E(23, 30, 10, 5, B("#FFA94D")));
                _body.Children.Add(P("M 19.5,19 L 24.5,19 L 22,23 Z", B("#FFA94D")));
                _body.Children.Add(E(13.5, 18, 4, 3, B("#FF9AA2", 0.6)));
                _body.Children.Add(E(26.5, 18, 4, 3, B("#FF9AA2", 0.6)));
                break;
        }
        Eyes.Foreground = new SolidColorBrush(EyeColor(_species));
        Canvas.SetLeft(Eyes, 22 - 44 * 0.42 / 2);
        Canvas.SetTop(Eyes, spec.EyeY - 22 * 0.42 / 2);
        _body.Children.Add(Eyes);

        // наушники под размер головы
        _hp.Children.Clear();
        var red = B("#FF5A6E");
        _hp.Children.Add(P(FormattableString.Invariant($"M {spec.Hx1},{spec.Hy} A {spec.Hr},{spec.Hr} 0 0 1 {spec.Hx2},{spec.Hy}"), null, red, 2.4));
        _hp.Children.Add(E(spec.Hx1 - 2.5, spec.Hy - 3, 5, 9, red));
        _hp.Children.Add(E(spec.Hx2 - 2.5, spec.Hy - 3, 5, 9, red));
        UpdateAnim();
    }

    void UpdateAnim()
    {
        foreach (var rt in _sway) rt.BeginAnimation(RotateTransform.AngleProperty, null);
        _bob.BeginAnimation(TranslateTransform.YProperty, null);
        _bob.Y = 0;
        if (!IsVisible || !S.Animations) return;
        bool jelly = _species == "Jelly";
        _bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(jelly ? -1.6 : -0.7, jelly ? 1.6 : 0.7, TimeSpan.FromMilliseconds(jelly ? 1100 : 1900))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        int i = 0;
        foreach (var rt in _sway)
            rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-6, 6, TimeSpan.FromMilliseconds(850 + 140 * i++))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    static void Loop(TextBlock t, TranslateTransform tt, bool on, int delay)
    {
        t.BeginAnimation(OpacityProperty, null);
        tt.BeginAnimation(TranslateTransform.YProperty, null);
        if (!on) { t.Opacity = 0; return; }
        if (!S.Animations) { t.Opacity = 0.8; return; }
        t.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(900))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(delay) });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(3, -7, TimeSpan.FromMilliseconds(1800))
        { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(delay) });
    }

    /// Аксессуары по состоянию: наушники и ноты (музыка), конверт (сообщение), «z» (сон), капля (усталость), «!» (тревога).
    public void ApplyState(FaceMood m, bool music)
    {
        _hp.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
        Loop(_n1, _n1T, music, 0);
        Loop(_n2, _n2T, music, 500);
        _mail.Visibility = m == FaceMood.Message ? Visibility.Visible : Visibility.Collapsed;
        _sweat.Visibility = m == FaceMood.Tired ? Visibility.Visible : Visibility.Collapsed;
        _alert.Visibility = m == FaceMood.Alert ? Visibility.Visible : Visibility.Collapsed;
        Loop(_z1, _z1T, m == FaceMood.Sleeping, 0);
        Loop(_z2, _z2T, m == FaceMood.Sleeping, 700);
    }

    /// Для уведомлений: настроение без FaceBrain.
    public void ApplyMood(FaceMood m, bool instant)
    {
        Eyes.SetMood(m, instant);
        ApplyState(m, false);
    }
}
