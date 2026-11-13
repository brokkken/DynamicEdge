using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DynamicEdge.Services;

public sealed class WeatherInfo
{
    public double Temp, Feels;
    public int Code;
    public string City = "";
}

/// Погода через Open-Meteo (open-meteo.com, без ключей). В сервис отправляется только название города, затем координаты.
public sealed class WeatherService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    readonly Dispatcher _d = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMinutes(20) };
    double? _lat, _lon;
    string _cityKey = "", _resolvedName = "";
    bool _busy;
    public WeatherInfo? Current { get; private set; }
    public event Action? Changed;

    public WeatherService() { _t.Tick += (_, _) => _ = RefreshAsync(); }

    public void Apply()
    {
        var s = Core.SettingsService.Current;
        if (!s.WeatherEnabled || string.IsNullOrWhiteSpace(s.WeatherCity))
        {
            _t.Stop();
            if (Current != null) { Current = null; Changed?.Invoke(); }
            return;
        }
        if (_cityKey != s.WeatherCity.Trim()) { _cityKey = s.WeatherCity.Trim(); _lat = null; _lon = null; }
        if (!_t.IsEnabled) _t.Start();
        _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_lat == null)
            {
                string g = await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(_cityKey)}&count=1&language=ru&format=json");
                using var gd = JsonDocument.Parse(g);
                if (!gd.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) { Core.SettingsService.Log("Weather: город не найден: " + _cityKey); return; }
                var r0 = res[0];
                _lat = r0.GetProperty("latitude").GetDouble(); _lon = r0.GetProperty("longitude").GetDouble();
                _resolvedName = r0.TryGetProperty("name", out var nm) ? nm.GetString() ?? _cityKey : _cityKey;
            }
            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={_lat}&longitude={_lon}&current=temperature_2m,apparent_temperature,weather_code&timezone=auto");
            using var fd = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = fd.RootElement.GetProperty("current");
            var info = new WeatherInfo
            {
                Temp = cur.GetProperty("temperature_2m").GetDouble(),
                Feels = cur.GetProperty("apparent_temperature").GetDouble(),
                Code = cur.GetProperty("weather_code").GetInt32(),
                City = _resolvedName
            };
            _d.BeginInvoke(() => { Current = info; Changed?.Invoke(); });
        }
        catch (Exception ex) { Core.SettingsService.Log("Weather: " + ex.Message); }
        finally { _busy = false; }
    }

    public static (string Emoji, string Text) Describe(int code) => code switch
    {
        0 => ("☀", "Ясно"),
        1 or 2 => ("⛅", "Малооблачно"),
        3 => ("☁", "Облачно"),
        45 or 48 => ("🌫", "Туман"),
        >= 51 and <= 57 => ("🌦", "Морось"),
        >= 61 and <= 67 or >= 80 and <= 82 => ("🌧", "Дождь"),
        >= 71 and <= 77 or 85 or 86 => ("❄", "Снег"),
        >= 95 => ("⛈", "Гроза"),
        _ => ("🌡", "Погода")
    };
}
