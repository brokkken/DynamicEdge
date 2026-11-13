using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DynamicEdge.Services;

public sealed class HistoryEntry
{
    public string Date { get; set; } = "";       // yyyy-MM-dd
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public double Seconds { get; set; }
}

public sealed class WeekSummary
{
    public int TotalMinutes;
    public int DaysWithMusic;
    public double[] DayMinutes = new double[7];
    public string[] DayLabels = new string[7];
    public List<(string Name, double Minutes)> Artists = new();
    public List<(string Name, double Minutes)> Tracks = new();
}

/// Локальная история прослушивания: хранится только на этом компьютере (history.json рядом с настройками).
public sealed class HistoryService
{
    static readonly string FilePath = Path.Combine(Path.GetDirectoryName(Core.SettingsService.FilePath)!, "history.json");
    readonly List<HistoryEntry> _items = new();
    bool _dirty, _loaded;
    DateTime _lastSave = DateTime.UtcNow;

    void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath));
                if (list != null)
                {
                    string cutoff = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    _items.AddRange(list.Where(i => string.CompareOrdinal(i.Date, cutoff) >= 0));
                }
            }
        }
        catch (Exception ex) { Core.SettingsService.Log("History load: " + ex.Message); }
    }

    public void Add(string artist, string title, double seconds)
    {
        EnsureLoaded();
        string d = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var e = _items.FirstOrDefault(i => i.Date == d && i.Artist == artist && i.Title == title);
        if (e == null) _items.Add(e = new HistoryEntry { Date = d, Artist = artist, Title = title });
        e.Seconds += seconds;
        _dirty = true;
    }

    public void FlushIfDue() { if (_dirty && (DateTime.UtcNow - _lastSave).TotalSeconds > 60) Flush(); }

    public void Flush()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
            _dirty = false; _lastSave = DateTime.UtcNow;
        }
        catch (Exception ex) { Core.SettingsService.Log("History save: " + ex.Message); }
    }

    public void Clear()
    {
        EnsureLoaded();
        _items.Clear();
        _dirty = true;
        Flush();
    }

    public WeekSummary Summary()
    {
        EnsureLoaded();
        var sum = new WeekSummary();
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6)).ToList();
        for (int i = 0; i < 7; i++)
        {
            string ds = days[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            sum.DayMinutes[i] = _items.Where(x => x.Date == ds).Sum(x => x.Seconds) / 60.0;
            sum.DayLabels[i] = ru.DateTimeFormat.GetAbbreviatedDayName(days[i].DayOfWeek);
        }
        sum.DaysWithMusic = sum.DayMinutes.Count(m => m >= 0.5);
        sum.TotalMinutes = (int)Math.Round(sum.DayMinutes.Sum());
        string first = days[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var week = _items.Where(x => string.CompareOrdinal(x.Date, first) >= 0).ToList();
        sum.Artists = week.GroupBy(x => x.Artist.Length > 0 ? x.Artist : "Неизвестный исполнитель")
            .Select(g => (g.Key, g.Sum(x => x.Seconds) / 60.0)).OrderByDescending(t => t.Item2).Take(5).ToList();
        sum.Tracks = week.GroupBy(x => x.Artist.Length > 0 ? x.Artist + " — " + x.Title : x.Title)
            .Select(g => (g.Key, g.Sum(x => x.Seconds) / 60.0)).OrderByDescending(t => t.Item2).Take(5).ToList();
        return sum;
    }
}
