using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace DynamicEdge.Services;

public enum TimerMode { None, Countdown, Stopwatch }

public sealed class TimerService
{
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Stopwatch _sw = new();
    TimeSpan _total;
    int _lastSec = -1;

    public TimerMode Mode { get; private set; }
    public bool Running => Mode != TimerMode.None;
    public TimeSpan Total => _total;
    public event Action? Tick;           // раз в секунду
    public event Action? Started;
    public event Action? Completed;

    public TimerService() { _t.Tick += OnTick; }

    public TimeSpan Display => Mode switch
    {
        TimerMode.Countdown => TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, (_total - _sw.Elapsed).TotalSeconds))),
        TimerMode.Stopwatch => TimeSpan.FromSeconds(Math.Floor(_sw.Elapsed.TotalSeconds)),
        _ => TimeSpan.Zero
    };

    public void Start(TimeSpan total) { _total = total; Mode = TimerMode.Countdown; Begin(); }
    public void StartStopwatch() { Mode = TimerMode.Stopwatch; Begin(); }

    void Begin() { _sw.Restart(); _lastSec = -1; _t.Start(); Started?.Invoke(); Tick?.Invoke(); }

    public void Stop() { _t.Stop(); _sw.Reset(); Mode = TimerMode.None; Tick?.Invoke(); }

    void OnTick(object? s, EventArgs e)
    {
        if (Mode == TimerMode.Countdown && _sw.Elapsed >= _total)
        {
            _t.Stop(); _sw.Reset(); Mode = TimerMode.None;
            Tick?.Invoke();
            Completed?.Invoke();
            return;
        }
        int sec = (int)_sw.Elapsed.TotalSeconds;
        if (sec != _lastSec) { _lastSec = sec; Tick?.Invoke(); }
    }
}
