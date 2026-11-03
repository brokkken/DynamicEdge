using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DynamicEdge.Services;

public sealed class MediaInfo
{
    public bool HasSession, Playing, CanNext = true, CanPrev = true;
    public string Title = "", Artist = "", Album = "", App = "";
    public ImageSource? Art;
    public Color Accent = Core.ThemeManager.DefaultAccent;
}

/// Интеграция с системными медиасессиями Windows (SMTC): Spotify, браузеры, плееры.
public sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _s;
    readonly Dispatcher _d = Dispatcher.CurrentDispatcher;
    int _ver;
    public MediaInfo Current { get; private set; } = new();
    public event Action<MediaInfo, bool>? Changed;   // (info, trackChanged)

    public async Task InitAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => Attach();
            Attach();
        }
        catch (Exception ex) { Core.SettingsService.Log("SMTC init: " + ex.Message); }
    }

    void Attach()
    {
        if (_s != null)
        {
            _s.MediaPropertiesChanged -= OnProps;
            _s.PlaybackInfoChanged -= OnPlayback;
        }
        _s = _mgr?.GetCurrentSession();
        if (_s != null)
        {
            _s.MediaPropertiesChanged += OnProps;
            _s.PlaybackInfoChanged += OnPlayback;
        }
        _ = RefreshAsync(true);
    }

    void OnProps(GlobalSystemMediaTransportControlsSession s, object a) => _ = RefreshAsync(true);
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, object a) => _ = RefreshAsync(false);

    async Task RefreshAsync(bool reload)
    {
        var s = _s;
        int v = ++_ver;
        if (s == null) { Post(new MediaInfo(), false, v); return; }
        var info = new MediaInfo { HasSession = true, App = s.SourceAppUserModelId ?? "" };
        try
        {
            var pb = s.GetPlaybackInfo();
            info.Playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            info.CanNext = pb.Controls.IsNextEnabled;
            info.CanPrev = pb.Controls.IsPreviousEnabled;
        }
        catch { }

        if (reload)
        {
            try
            {
                var p = await s.TryGetMediaPropertiesAsync();
                info.Title = p.Title ?? ""; info.Artist = p.Artist ?? ""; info.Album = p.AlbumTitle ?? "";
                if (p.Thumbnail != null)
                {
                    using var rs = await p.Thumbnail.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await rs.AsStreamForRead().CopyToAsync(ms);
                    var bi = ImageUtil.FromBytes(ms.ToArray());
                    if (bi != null) { info.Art = bi; info.Accent = ImageUtil.Dominant(bi); }
                }
            }
            catch (Exception ex) { Core.SettingsService.Log("SMTC props: " + ex.Message); }
        }
        else
        {
            var c = Current;
            info.Title = c.Title; info.Artist = c.Artist; info.Album = c.Album; info.Art = c.Art; info.Accent = c.Accent;
        }
        bool trackChanged = reload && info.Title.Length > 0 && (info.Title != Current.Title || info.Artist != Current.Artist);
        Post(info, trackChanged, v);
    }

    void Post(MediaInfo info, bool tc, int ver) => _d.BeginInvoke(() =>
    {
        if (ver != _ver) return;      // устарело — пришёл более новый запрос
        Current = info;
        Changed?.Invoke(info, tc);
    });

    public async Task PlayPauseAsync() { try { if (_s != null) await _s.TryTogglePlayPauseAsync(); } catch { } }
    public async Task NextAsync() { try { if (_s != null) await _s.TrySkipNextAsync(); } catch { } }
    public async Task PrevAsync() { try { if (_s != null) await _s.TrySkipPreviousAsync(); } catch { } }

    public (TimeSpan pos, TimeSpan len)? GetTimeline()
    {
        try
        {
            if (_s == null) return null;
            var tl = _s.GetTimelineProperties();
            var len = tl.EndTime - tl.StartTime;
            if (len <= TimeSpan.Zero) return null;
            var pos = tl.Position - tl.StartTime;
            if (Current.Playing) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > len) pos = len;
            return (pos, len);
        }
        catch { return null; }
    }
}
