using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.Timers;

/// <summary>A running countdown. The provider ticks it once a second.</summary>
public sealed partial class TimerActivity : IslandActivity
{
    private readonly Action<TimerActivity> _cancel;
    private DateTime _endsAt;

    public TimerActivity(string label, TimeSpan total, Action<TimerActivity> cancel)
    {
        Label = label;
        Total = total;
        Remaining = total;
        _endsAt = DateTime.UtcNow + total;
        _cancel = cancel;
        Id = $"timer-{Guid.NewGuid():N}";
        TogglePauseCommand = new RelayCommand(TogglePause);
        AddMinuteCommand = new RelayCommand(() => AddTime(TimeSpan.FromMinutes(1)));
        CancelCommand = new RelayCommand(() => _cancel(this));
    }

    public override string Id { get; }
    public override ActivityKind Kind => ActivityKind.Timer;

    // A countdown the user started on purpose outranks ambient media and a running Claude session.
    public override int Priority => 4;

    public string Label { get; }
    public TimeSpan Total { get; private set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RemainingText), nameof(Progress))] private TimeSpan _remaining;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PauseGlyph))] private bool _isPaused;

    public string RemainingText => Remaining.TotalHours >= 1
        ? $"{(int)Remaining.TotalHours}:{Remaining.Minutes:00}:{Remaining.Seconds:00}"
        : $"{Remaining.Minutes:00}:{Remaining.Seconds:00}";

    /// <summary>Fraction of the time still left, 1 → 0.</summary>
    public double Progress => Total.TotalSeconds <= 0 ? 0 : Math.Clamp(Remaining / Total, 0, 1);

    // Segoe MDL2 Assets: Pause / Play.
    public string PauseGlyph => IsPaused ? "" : "";

    public ICommand TogglePauseCommand { get; }
    public ICommand AddMinuteCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>Returns true once the countdown has run out.</summary>
    public bool Tick()
    {
        if (IsPaused) return false;
        Remaining = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, (_endsAt - DateTime.UtcNow).TotalSeconds)));
        return _endsAt <= DateTime.UtcNow;
    }

    private void TogglePause()
    {
        if (IsPaused) _endsAt = DateTime.UtcNow + Remaining;
        IsPaused = !IsPaused;
        Touch();
    }

    private void AddTime(TimeSpan extra)
    {
        _endsAt += extra;
        Total += extra;
        Remaining += extra;
        OnPropertyChanged(nameof(Progress));
        Touch();
    }
}

/// <summary>A reminder (or finished countdown) that is due now. Stays until the user answers it.</summary>
public sealed class ReminderActivity : IslandActivity
{
    public ReminderActivity(string title, string text, Action dismiss, Action? snooze)
    {
        Title = title;
        Text = text;
        Id = $"reminder-{Guid.NewGuid():N}";
        DismissCommand = new RelayCommand(dismiss);
        SnoozeCommand = new RelayCommand(() => snooze?.Invoke());
        CanSnooze = snooze is not null;
    }

    public override string Id { get; }
    public override ActivityKind Kind => ActivityKind.Reminder;

    // Below notifications (10), so a fresh notification keeps the main island and the reminder moves aside.
    public override int Priority => 9;

    // ...to a small island, rather than waiting invisibly.
    public override bool CanOverflow => true;

    public string Title { get; }
    public string Text { get; }
    public bool CanSnooze { get; }

    public ICommand DismissCommand { get; }
    public ICommand SnoozeCommand { get; }
    public void Dismiss() => DismissCommand.Execute(null);
}
