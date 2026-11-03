using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicEdge.Core;

namespace DynamicEdge.Views;

public partial class EqBars : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(EqBars), new PropertyMetadata(false, (d, _) => ((EqBars)d).Update()));
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    static readonly double[] Ms = { 420, 560, 380, 500 };

    public EqBars()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => Update();
    }

    public void Update()
    {
        var bars = new[] { B1, B2, B3, B4 };
        bool run = IsActive && IsVisible && SettingsService.Current.Animations;
        for (int i = 0; i < bars.Length; i++)
        {
            var st = (ScaleTransform)bars[i].RenderTransform;
            if (run)
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(Ms[i]))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            else
            {
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                st.ScaleY = IsActive ? 0.6 : 0.3;
            }
        }
    }
}
