using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core;
using DynamicIsland.Providers.Media;

namespace DynamicIsland.Shell;

public sealed partial class IslandViewModel : ObservableObject
{
    private static readonly TimeSpan AutoCollapse = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HoverCollapseDelay = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherTimer _collapseTimer = new();
    private MediaActivity? _watched;
    private bool _hovering;

    public IslandViewModel(ActivityHub hub)
    {
        Hub = hub;
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); if (!_hovering) IsExpanded = false; };
        hub.CurrentChanged += (_, _) => OnCurrentChanged();
        OnCurrentChanged();
    }

    public ActivityHub Hub { get; }

    [ObservableProperty] private IslandActivity? _current;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSuppressed; // e.g. a fullscreen app is in front

    public bool IsVisible => Current is not null && !IsSuppressed;

    partial void OnCurrentChanged(IslandActivity? value) => OnPropertyChanged(nameof(IsVisible));
    partial void OnIsSuppressedChanged(bool value) => OnPropertyChanged(nameof(IsVisible));

    private void OnCurrentChanged()
    {
        if (_watched is not null) _watched.TrackChanged -= OnTrackChanged;
        Current = Hub.Current;
        _watched = Current as MediaActivity;
        if (_watched is not null) _watched.TrackChanged += OnTrackChanged;
        if (Current is null) IsExpanded = false;
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        if (IsSuppressed) return;
        IsExpanded = true;
        _collapseTimer.Interval = AutoCollapse;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    public void PointerEntered()
    {
        _hovering = true;
        _collapseTimer.Stop();
        IsExpanded = true;
    }

    public void PointerLeft()
    {
        _hovering = false;
        _collapseTimer.Interval = HoverCollapseDelay;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }
}
