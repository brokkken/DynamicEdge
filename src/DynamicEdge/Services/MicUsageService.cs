using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicEdge.Services;

/// Определяет, что микрофон используется, по данным Windows Capability Access Manager (тот же источник, что и точка в трее).
public sealed class MicUsageService
{
    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    public bool Active { get; private set; }
    public string? App { get; private set; }
    public event Action? Changed;

    public MicUsageService() { _t.Tick += (_, _) => Poll(); }

    public bool Enabled
    {
        get => _t.IsEnabled;
        set { if (value && !_t.IsEnabled) { Poll(); _t.Start(); } else if (!value) { _t.Stop(); if (Active) { Active = false; App = null; Changed?.Invoke(); } } }
    }

    static (bool, string?) Query()
    {
        foreach (var root in new[] { Root, Root + @"\NonPackaged" })
        {
            using var k = Registry.CurrentUser.OpenSubKey(root);
            if (k == null) continue;
            foreach (var name in k.GetSubKeyNames())
            {
                if (name == "NonPackaged") continue;
                using var sk = k.OpenSubKey(name);
                if (sk == null) continue;
                long start = Convert.ToInt64(sk.GetValue("LastUsedTimeStart", 0L) ?? 0L);
                long stop = Convert.ToInt64(sk.GetValue("LastUsedTimeStop", 0L) ?? 0L);
                if (start > 0 && stop == 0)
                {
                    string app = root.EndsWith("NonPackaged")
                        ? Path.GetFileNameWithoutExtension(name.Replace('#', '\\'))
                        : name.Split('_')[0];
                    return (true, app);
                }
            }
        }
        return (false, null);
    }

    bool _busy;

    async void Poll()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var (a, app) = await Task.Run(Query);      // чтение реестра — вне потока UI
            if (a != Active || app != App) { Active = a; App = app; Changed?.Invoke(); }
        }
        catch { }
        finally { _busy = false; }
    }
}
