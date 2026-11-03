using System;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace DynamicEdge.Services;

public enum BatteryEventKind { PluggedIn, Unplugged, Low, Full }

public sealed class BatteryService
{
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(5) };
    bool _first = true, _lowShown, _fullShown;
    public bool HasBattery { get; private set; }
    public int Percent { get; private set; }
    public bool Charging { get; private set; }
    public event Action<BatteryEventKind>? Event;

    public BatteryService() { _t.Tick += (_, _) => Poll(); }

    public bool Enabled
    {
        get => _t.IsEnabled;
        set { if (value && !_t.IsEnabled) { _first = true; Poll(); _t.Start(); } else if (!value) _t.Stop(); }
    }

    void Poll()
    {
        var p = WF.SystemInformation.PowerStatus;
        HasBattery = (p.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) == 0
                     && p.BatteryChargeStatus != WF.BatteryChargeStatus.Unknown;
        if (!HasBattery || p.BatteryLifePercent > 1.0f) return;
        int pct = (int)Math.Round(p.BatteryLifePercent * 100);
        bool charging = p.PowerLineStatus == WF.PowerLineStatus.Online;
        bool wasCharging = Charging;
        Percent = pct; Charging = charging;
        if (_first) { _first = false; _lowShown = !charging && pct <= Core.SettingsService.Current.LowBatteryPercent; return; }

        if (charging != wasCharging) Event?.Invoke(charging ? BatteryEventKind.PluggedIn : BatteryEventKind.Unplugged);
        int low = Core.SettingsService.Current.LowBatteryPercent;
        if (!charging && pct <= low && !_lowShown) { _lowShown = true; Event?.Invoke(BatteryEventKind.Low); }
        if (charging || pct > low + 5) _lowShown = false;
        if (charging && pct >= 100 && !_fullShown) { _fullShown = true; Event?.Invoke(BatteryEventKind.Full); }
        if (pct < 95) _fullShown = false;
    }
}
