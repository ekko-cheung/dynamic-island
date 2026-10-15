using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core;
using DynamicIsland.Providers.Media;
using DynamicIsland.Providers.Notifications;
using DynamicIsland.Providers.SystemLevels;

namespace DynamicIsland.Shell;

public sealed partial class IslandViewModel : ObservableObject
{
    private static readonly TimeSpan AutoCollapse = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HoverCollapseDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan NotificationShowFor = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _collapseTimer = new();
    private MediaActivity? _watched;
    private bool _hovering;

    public IslandViewModel(ActivityHub hub)
    {
        Hub = hub;
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); if (!_hovering) IsExpanded = false; };
        hub.SlotChanged += (_, slot) => OnSlotChanged(slot);
        OnMainChanged();
    }

    public ActivityHub Hub { get; }

    [ObservableProperty] private IslandActivity? _current;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSuppressed; // e.g. a fullscreen app is in front

    // The small islands beside the main one; compact only.
    [ObservableProperty] private IslandActivity? _left;
    [ObservableProperty] private IslandActivity? _right;

    public bool IsVisible => Current is not null && !IsSuppressed;
    public bool IsLeftVisible => Left is not null && !IsSuppressed;
    public bool IsRightVisible => Right is not null && !IsSuppressed;

    partial void OnCurrentChanged(IslandActivity? value) => OnPropertyChanged(nameof(IsVisible));
    partial void OnLeftChanged(IslandActivity? value) => OnPropertyChanged(nameof(IsLeftVisible));
    partial void OnRightChanged(IslandActivity? value) => OnPropertyChanged(nameof(IsRightVisible));
    partial void OnIsSuppressedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsLeftVisible));
        OnPropertyChanged(nameof(IsRightVisible));
    }

    private void OnSlotChanged(IslandSlot slot)
    {
        switch (slot)
        {
            case IslandSlot.Main: OnMainChanged(); break;
            case IslandSlot.Left: Left = Hub.Get(IslandSlot.Left); break;
            case IslandSlot.Right: Right = Hub.Get(IslandSlot.Right); break;
        }
    }

    private void OnMainChanged()
    {
        if (_watched is not null) _watched.TrackChanged -= OnTrackChanged;
        Current = Hub.Get(IslandSlot.Main);
        _watched = Current as MediaActivity;
        if (_watched is not null) _watched.TrackChanged += OnTrackChanged;

        if (Current is NotificationActivity && !IsSuppressed)
        {
            // New notifications pop open briefly, then the hub drops them and the pill falls back.
            IsExpanded = true;
            if (Current is NotificationActivity { IsSticky: true }) _collapseTimer.Stop();
            else RestartCollapseTimer(NotificationShowFor);
        }
        else if (Current is null or LevelActivity)
        {
            // A level bar is always the compact pill.
            IsExpanded = false;
            _collapseTimer.Stop();
        }
    }

    private void RestartCollapseTimer(TimeSpan delay)
    {
        _collapseTimer.Interval = delay;
        _collapseTimer.Stop();
        _collapseTimer.Start();
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
        if (Current is LevelActivity) return;
        _collapseTimer.Stop();
        IsExpanded = true;
    }

    public void PointerLeft()
    {
        _hovering = false;
        if (Current is NotificationActivity { IsSticky: true }) return; // keep the close button reachable
        _collapseTimer.Interval = HoverCollapseDelay;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }
}
