using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DynamicEdge.Core;

public static class ThemeManager
{
    public static bool IsLight { get; private set; }
    public static readonly Color DefaultAccent = Color.FromRgb(0x4C, 0xC2, 0xFF);

    static bool SystemUsesLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(k?.GetValue("AppsUseLightTheme", 0) ?? 0) == 1;
        }
        catch { return false; }
    }

    static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    public static void Apply()
    {
        var s = SettingsService.Current;
        IsLight = s.Theme == "Light" || (s.Theme == "System" && SystemUsesLight());
        var r = Application.Current.Resources;
        byte a = (byte)Math.Round(s.Opacity * 255);
        if (!IsLight)
        {
            r["CapsuleBg"] = new SolidColorBrush(Color.FromArgb(a, 0, 0, 0));
            r["CapsuleBorder"] = B("#26FFFFFF");
            r["FgBrush"] = B("#FFFFFFFF"); r["Fg2Brush"] = B("#B3FFFFFF");
            r["ChipBg"] = B("#1FFFFFFF"); r["ChipHoverBrush"] = B("#33FFFFFF"); r["TrackBrush"] = B("#26FFFFFF");
        }
        else
        {
            r["CapsuleBg"] = new SolidColorBrush(Color.FromArgb(a, 0xF6, 0xF6, 0xF8));
            r["CapsuleBorder"] = B("#26000000");
            r["FgBrush"] = B("#FF111111"); r["Fg2Brush"] = B("#99000000");
            r["ChipBg"] = B("#14000000"); r["ChipHoverBrush"] = B("#26000000"); r["TrackBrush"] = B("#1F000000");
        }
    }

    public static void SetAccent(Color c) => Application.Current.Resources["AccentBrush"] = new SolidColorBrush(c);
}
