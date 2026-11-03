using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace DynamicEdge.Core;

public sealed class AppSettings
{
    public bool FirstRunDone { get; set; }

    public bool EnableMusic { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool EnableBattery { get; set; } = true;
    public bool EnableVolume { get; set; } = true;
    public bool EnableMic { get; set; } = true;
    public bool EnableClipboard { get; set; } = true;
    public bool EnableTimer { get; set; } = true;
    public bool EnableSysInfo { get; set; } = true;

    public int Monitor { get; set; } = -1;          // -1 = основной
    public int OffsetX { get; set; } = 0;
    public int OffsetY { get; set; } = 8;
    public double Scale { get; set; } = 1.0;        // 0.8 .. 1.3
    public double Opacity { get; set; } = 0.96;     // 0.4 .. 1.0 (прозрачность фона капсулы)
    public string Theme { get; set; } = "Dark";     // Dark | Light | System
    public bool Animations { get; set; } = true;
    public bool Autostart { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    public bool AutoHideIdle { get; set; } = false;

    public int NotificationSeconds { get; set; } = 5;
    public int LowBatteryPercent { get; set; } = 20;
    public int DefaultTimerMinutes { get; set; } = 5;

    public string HotkeyToggle { get; set; } = "Ctrl+Alt+D";
    public string HotkeyHide { get; set; } = "Ctrl+Alt+H";
    public string HotkeyTimer { get; set; } = "Ctrl+Alt+T";
}

public static class SettingsService
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicEdge");
    public static string FilePath => Path.Combine(Dir, "settings.json");
    public static string LogPath => Path.Combine(Dir, "log.txt");
    public static AppSettings Current { get; private set; } = new();
    public static event Action? Changed;

    static DispatcherTimer? _saveTimer;
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) { Log("Settings load failed: " + ex.Message); Current = new AppSettings(); }
        Sanitize();
    }

    static void Sanitize()
    {
        var s = Current;
        s.Scale = Math.Clamp(s.Scale, 0.8, 1.3);
        s.Opacity = Math.Clamp(s.Opacity, 0.4, 1.0);
        s.NotificationSeconds = Math.Clamp(s.NotificationSeconds, 2, 30);
        s.LowBatteryPercent = Math.Clamp(s.LowBatteryPercent, 5, 50);
        s.DefaultTimerMinutes = Math.Clamp(s.DefaultTimerMinutes, 1, 600);
        s.OffsetY = Math.Clamp(s.OffsetY, -6, 400);
        s.OffsetX = Math.Clamp(s.OffsetX, -2000, 2000);
        if (s.Theme != "Light" && s.Theme != "System") s.Theme = "Dark";
    }

    /// <summary>Применяет изменения сразу, сохраняет на диск с задержкой.</summary>
    public static void NotifyChanged()
    {
        Sanitize();
        Changed?.Invoke();
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick -= OnSaveTick;
        _saveTimer.Tick += OnSaveTick;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    static void OnSaveTick(object? s, EventArgs e) { _saveTimer?.Stop(); Save(); }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Opts));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex) { Log("Settings save failed: " + ex.Message); }
    }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
            File.AppendAllText(LogPath, $"{DateTime.Now:s} {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
