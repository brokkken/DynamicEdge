using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using DynamicEdge.Core;

namespace DynamicEdge.Services;

/// «Полка»: быстрые ссылки (перетащите ссылку из браузера на капсулу или вставьте из буфера обмена).
public static class ShelfService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
    static readonly Dictionary<string, ImageSource?> Icons = new();
    static readonly string IconDir = Path.Combine(Path.GetDirectoryName(SettingsService.FilePath)!, "icons");

    public static string? NormalizeUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string first = text.Trim().Split('\n', '\r')[0].Trim();
        if (!first.Contains("://") && first.Contains('.') && !first.Contains(' ')) first = "https://" + first;
        if (Uri.TryCreate(first, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            return u.AbsoluteUri;
        return null;
    }

    public static string DisplayName(string url)
    {
        try { var h = new Uri(url).Host; return h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..] : h; }
        catch { return url; }
    }

    public static bool Add(string url)
    {
        var list = SettingsService.Current.ShelfLinks;
        if (list.Contains(url)) return false;
        list.Insert(0, url);
        while (list.Count > 12) list.RemoveAt(list.Count - 1);
        SettingsService.NotifyChanged();
        return true;
    }

    public static void Remove(string url) { SettingsService.Current.ShelfLinks.Remove(url); SettingsService.NotifyChanged(); }
    public static void Clear() { SettingsService.Current.ShelfLinks.Clear(); SettingsService.NotifyChanged(); }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { SettingsService.Log("Shelf open: " + ex.Message); }
    }

    public static async Task<ImageSource?> GetIconAsync(string url)
    {
        string host;
        try { host = new Uri(url).Host; } catch { return null; }
        if (Icons.TryGetValue(host, out var c)) return c;
        ImageSource? img = null;
        try
        {
            string file = Path.Combine(IconDir, host + ".bin");
            byte[]? bytes = File.Exists(file) ? await File.ReadAllBytesAsync(file) : null;
            if (bytes == null)
            {
                bytes = await Http.GetByteArrayAsync($"https://{host}/favicon.ico");
                if (bytes.Length > 100 && bytes.Length < 300_000)
                {
                    Directory.CreateDirectory(IconDir);
                    await File.WriteAllBytesAsync(file, bytes);
                }
            }
            img = ImageUtil.FromBytes(bytes);
        }
        catch { }
        Icons[host] = img;
        return img;
    }
}
