using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.Media.Control;

namespace DynamicIsland.Providers.Media;

/// <summary>
/// Bridges Windows SMTC (system media transport controls) to a single <see cref="MediaActivity"/>.
/// Session state is polled on the UI thread instead of subscribing to per-session events: players such as Apple Music
/// replace their session on every track change, and adding/removing handlers on a session that is being torn down
/// crashed the process inside Windows.Media.MediaControl.dll.
/// </summary>
public sealed class MediaActivityProvider : IActivityProvider
{
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaActivity? _activity;
    private bool _busy;
    private bool _announced;
    private string _sessionId = "";
    private string _trackKey = "";
    private ImageSource? _thumb;
    private bool? _playing;

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _poll.Tick += async (_, _) => await PollAsync();
        await PollAsync();
        _poll.Start();
    }

    private async Task PollAsync()
    {
        if (_busy || _manager is null) return;
        _busy = true;
        try
        {
            var session = _manager.GetCurrentSession();
            if (session is null)
            {
                Detach();
                return;
            }

            var id = session.SourceAppUserModelId;
            var isNewSession = !_announced || id != _sessionId;
            _session = session;
            _sessionId = id;

            _activity ??= new MediaActivity(
                async () => await (_session?.TryTogglePlayPauseAsync().AsTask() ?? Task.FromResult(false)),
                async () => await (_session?.TrySkipNextAsync().AsTask() ?? Task.FromResult(false)),
                async () => await (_session?.TrySkipPreviousAsync().AsTask() ?? Task.FromResult(false)));

            var props = await session.TryGetMediaPropertiesAsync();
            var key = $"{id}|{props.Title}|{props.Artist}|{props.AlbumTitle}";
            if (key != _trackKey)
            {
                _trackKey = key;
                _thumb = await StreamImage.LoadAsync(props.Thumbnail);
                _activity.UpdateTrack(props.Title, props.Artist, _thumb);
            }

            var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing != _playing)
            {
                _playing = playing;
                _activity.UpdatePlayback(playing);
            }

            var timeline = session.GetTimelineProperties();
            _activity.UpdateTimeline(timeline.Position - timeline.StartTime, timeline.EndTime - timeline.StartTime);

            if (isNewSession)
            {
                _announced = true;
                ActivityChanged?.Invoke(this, new ActivityChangedEventArgs("media", _activity));
            }
        }
        catch
        {
            // The session can vanish mid-read; the next poll picks up whatever is current.
        }
        finally
        {
            _busy = false;
        }
    }

    private void Detach()
    {
        if (_session is null) return;
        _session = null;
        _sessionId = "";
        _trackKey = "";
        _playing = null;
        _announced = false;
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs("media", null));
    }
}
