using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core;
using Windows.Media.Control;

namespace DynamicIsland.Providers.Media;

/// <summary>Bridges Windows SMTC (system media transport controls) to a single <see cref="MediaActivity"/>.</summary>
public sealed class MediaActivityProvider : IActivityProvider
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaActivity? _activity;
    private string _lastThumbKey = "";
    private ImageSource? _lastThumb;

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => UI(AttachSessionAsync);
        await AttachSessionAsync();
    }

    // WinRT events arrive on thread-pool threads; hop to the UI thread before touching bound state.
    private static void UI(Func<Task> work) =>
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try { await work(); } catch { /* session may vanish mid-read */ }
        });

    private async Task AttachSessionAsync()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaProps;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }

        _session = _manager?.GetCurrentSession();
        if (_session is null)
        {
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs("media", null));
            return;
        }

        _session.MediaPropertiesChanged += OnMediaProps;
        _session.PlaybackInfoChanged += OnPlayback;
        _session.TimelinePropertiesChanged += OnTimeline;

        _activity ??= new MediaActivity(
            async () => await (_session?.TryTogglePlayPauseAsync().AsTask() ?? Task.FromResult(false)),
            async () => await (_session?.TrySkipNextAsync().AsTask() ?? Task.FromResult(false)),
            async () => await (_session?.TrySkipPreviousAsync().AsTask() ?? Task.FromResult(false)));

        await RefreshMediaAsync();
        RefreshPlayback();
        RefreshTimeline();
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs("media", _activity));
    }

    private void OnMediaProps(GlobalSystemMediaTransportControlsSession s, object a) => UI(RefreshMediaAsync);
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, object a) => UI(() => { RefreshPlayback(); return Task.CompletedTask; });
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, object a) => UI(() => { RefreshTimeline(); return Task.CompletedTask; });

    private async Task RefreshMediaAsync()
    {
        if (_session is null || _activity is null) return;
        var props = await _session.TryGetMediaPropertiesAsync();
        var key = $"{props.Title}|{props.Artist}|{props.AlbumTitle}";
        if (key != _lastThumbKey)
        {
            _lastThumbKey = key;
            _lastThumb = await LoadThumbnailAsync(props.Thumbnail);
        }
        _activity.UpdateTrack(props.Title, props.Artist, _lastThumb);
    }

    private void RefreshPlayback()
    {
        if (_session is null || _activity is null) return;
        var status = _session.GetPlaybackInfo().PlaybackStatus;
        _activity.UpdatePlayback(status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
    }

    private void RefreshTimeline()
    {
        if (_session is null || _activity is null) return;
        var t = _session.GetTimelineProperties();
        _activity.UpdateTimeline(t.Position - t.StartTime, t.EndTime - t.StartTime);
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var winStream = await reference.OpenReadAsync();
            using var ms = new MemoryStream();
            await winStream.AsStreamForRead().CopyToAsync(ms);
            if (ms.Length == 0) return null;
            ms.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
