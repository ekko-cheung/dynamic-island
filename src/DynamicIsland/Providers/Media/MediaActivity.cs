using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.Media;

public sealed partial class MediaActivity : IslandActivity
{
    private readonly Func<Task> _togglePlayPause;
    private readonly Func<Task> _next;
    private readonly Func<Task> _previous;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private TimeSpan _basePosition;
    private DateTime _baseTime = DateTime.UtcNow;

    public MediaActivity(Func<Task> togglePlayPause, Func<Task> next, Func<Task> previous)
    {
        _togglePlayPause = togglePlayPause;
        _next = next;
        _previous = previous;
        _ticker.Tick += (_, _) => Extrapolate();
    }

    public override string Id => "media";
    public override ActivityKind Kind => ActivityKind.Media;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _progress; // 0..1
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";

    public TimeSpan Duration { get; private set; }

    /// <summary>Raised when the track (title/artist) changes, so the shell can briefly expand.</summary>
    public event EventHandler? TrackChanged;

    public void UpdateTrack(string title, string artist, ImageSource? thumbnail)
    {
        var changed = title != Title || artist != Artist;
        Title = title;
        Artist = artist;
        Thumbnail = thumbnail;
        Touch();
        if (changed) TrackChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdatePlayback(bool isPlaying)
    {
        // Re-anchor extrapolation so a pause freezes at the right spot.
        if (IsPlaying && !isPlaying) _basePosition += DateTime.UtcNow - _baseTime;
        _baseTime = DateTime.UtcNow;
        IsPlaying = isPlaying;
        if (isPlaying) _ticker.Start(); else _ticker.Stop();
        Touch();
        Extrapolate();
    }

    public void UpdateTimeline(TimeSpan position, TimeSpan duration)
    {
        _basePosition = position;
        _baseTime = DateTime.UtcNow;
        Duration = duration;
        DurationText = Format(duration);
        Extrapolate();
    }

    private void Extrapolate()
    {
        var pos = IsPlaying ? _basePosition + (DateTime.UtcNow - _baseTime) : _basePosition;
        if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        Progress = Duration > TimeSpan.Zero ? pos / Duration : 0;
        PositionText = Format(pos);
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    [RelayCommand] private Task TogglePlayPause() => _togglePlayPause();
    [RelayCommand] private Task Next() => _next();
    [RelayCommand] private Task Previous() => _previous();
}
