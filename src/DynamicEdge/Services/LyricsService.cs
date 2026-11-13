using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DynamicEdge.Services;

public readonly record struct LrcLine(TimeSpan Time, string Text);

/// Тексты песен с синхронизацией по строкам (сервис LRCLIB, lrclib.net, без ключей).
/// Отправляются только исполнитель, название и длительность трека.
public sealed class LyricsService
{
    static readonly HttpClient Http = CreateClient();
    static readonly Regex LrcRx = new(@"^\[(\d+):(\d+(?:[.,]\d+)?)\]\s*(.*)$", RegexOptions.Compiled);
    readonly Dictionary<string, LrcLine[]?> _cache = new();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicEdge/1.3 (https://github.com/brokkken/DynamicEdge)");
        return c;
    }

    public async Task<LrcLine[]?> GetAsync(string artist, string title, int durationSec)
    {
        string key = (artist + "|" + title).ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;
        LrcLine[]? res = null;
        try
        {
            string url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}"
                         + (durationSec > 0 ? $"&duration={durationSec}" : "");
            using (var r = await Http.GetAsync(url))
                if (r.IsSuccessStatusCode) res = FromJson(await r.Content.ReadAsStringAsync());
            if (res == null)
            {
                using var r2 = await Http.GetAsync($"https://lrclib.net/api/search?q={Uri.EscapeDataString((artist + " " + title).Trim())}");
                if (r2.IsSuccessStatusCode) res = FromJson(await r2.Content.ReadAsStringAsync());
            }
        }
        catch { }
        if (_cache.Count > 30) _cache.Clear();
        _cache[key] = res;
        return res;
    }

    static LrcLine[]? FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    var p = FromObject(el);
                    if (p != null) return p;
                }
                return null;
            }
            return FromObject(root);
        }
        catch { return null; }
    }

    static LrcLine[]? FromObject(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty("syncedLyrics", out var s) || s.ValueKind != JsonValueKind.String) return null;
        return ParseLrc(s.GetString() ?? "");
    }

    public static LrcLine[]? ParseLrc(string text)
    {
        var list = new List<LrcLine>();
        foreach (var raw in text.Split('\n'))
        {
            var m = LrcRx.Match(raw.Trim());
            if (!m.Success) continue;
            double min = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double sec = double.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            string line = m.Groups[3].Value.Trim();
            list.Add(new LrcLine(TimeSpan.FromSeconds(min * 60 + sec), line.Length == 0 ? "♪" : line));
        }
        if (list.Count == 0) return null;
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return list.ToArray();
    }

    /// Индекс текущей строки для позиции трека (или -1 до первой строки).
    public static int CurrentIndex(LrcLine[] lines, TimeSpan pos)
    {
        pos += TimeSpan.FromMilliseconds(250);
        int lo = 0, hi = lines.Length - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= pos) { ans = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return ans;
    }
}
