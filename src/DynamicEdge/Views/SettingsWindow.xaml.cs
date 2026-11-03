using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DynamicEdge.Core;
using DynamicEdge.Services;
using WF = System.Windows.Forms;

namespace DynamicEdge.Views;

public partial class SettingsWindow : Window
{
    bool _loading = true;
    bool _lastAutostart;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = SettingsService.Current;
        _lastAutostart = SettingsService.Current.Autostart;

        MonitorBox.Items.Add(new ComboBoxItem { Content = "Основной монитор", Tag = -1 });
        var screens = WF.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
            MonitorBox.Items.Add(new ComboBoxItem { Content = $"Монитор {i + 1} ({screens[i].Bounds.Width}×{screens[i].Bounds.Height}){(screens[i].Primary ? ", основной" : "")}", Tag = i });
        int sel = SettingsService.Current.Monitor;
        MonitorBox.SelectedIndex = sel >= 0 && sel < screens.Length ? sel + 1 : 0;

        NotifStatus.Text = "Статус: " + AppServices.Notifications.Status;
        PathsText.Text = $"Версия .NET: {Environment.Version}\nУстановка: {(Native.IsPackaged() ? "MSIX" : "обычная (без package identity)")}\nНастройки: {SettingsService.FilePath}\nЛог: {SettingsService.LogPath}";
        _loading = false;
    }

    void Setting_Changed(object? sender, DataTransferEventArgs e)
    {
        if (_loading) return;
        var s = SettingsService.Current;
        SettingsService.NotifyChanged();
        if (s.Autostart != _lastAutostart) { _lastAutostart = s.Autostart; _ = Autostart.SetAsync(s.Autostart); }
    }

    void Monitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MonitorBox.SelectedItem is not ComboBoxItem it) return;
        SettingsService.Current.Monitor = (int)it.Tag;
        SettingsService.NotifyChanged();
    }

    void Hotkey_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None) return;
        var m = Keyboard.Modifiers;
        if (m == ModifierKeys.None) { HotkeyHint.Text = "Добавьте модификатор: Ctrl, Alt, Shift или Win."; return; }
        string combo = "";
        if (m.HasFlag(ModifierKeys.Control)) combo += "Ctrl+";
        if (m.HasFlag(ModifierKeys.Alt)) combo += "Alt+";
        if (m.HasFlag(ModifierKeys.Shift)) combo += "Shift+";
        if (m.HasFlag(ModifierKeys.Windows)) combo += "Win+";
        combo += key.ToString();
        var tb = (TextBox)sender;
        var s = SettingsService.Current;
        switch ((string)tb.Tag) { case "Toggle": s.HotkeyToggle = combo; break; case "Hide": s.HotkeyHide = combo; break; case "Timer": s.HotkeyTimer = combo; break; }
        tb.Text = combo;
        SettingsService.NotifyChanged();
        HotkeyHint.Text = $"Сохранено: {combo}. Если сочетание занято другим приложением, оно не сработает (см. лог).";
    }

    void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsService.FilePath)!;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
