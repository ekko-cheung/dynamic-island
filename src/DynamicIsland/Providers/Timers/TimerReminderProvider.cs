using System.Collections.ObjectModel;
using System.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;

namespace DynamicIsland.Providers.Timers;

/// <summary>
/// Owns countdowns (in memory) and scheduled reminders (persisted in settings). A single 1 s tick drives both;
/// comparing against the wall clock each time keeps reminders right across sleep and clock changes.
/// All members are UI-thread only.
/// </summary>
public sealed class TimerReminderProvider(AppSettings settings) : IActivityProvider
{
    private static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(5);
    // A once-only reminder missed while the app was closed still fires; a daily one this late just waits for tomorrow.
    private static readonly TimeSpan DailyGrace = TimeSpan.FromHours(1);

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public ObservableCollection<TimerActivity> Countdowns { get; } = [];
    public ObservableCollection<ReminderItem> Reminders => settings.Reminders;

    public Task StartAsync()
    {
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();
        OnTick(); // fire anything that came due while we were not running
        return Task.CompletedTask;
    }

    public void StartCountdown(TimeSpan duration, string label)
    {
        if (duration <= TimeSpan.Zero) return;
        var countdown = new TimerActivity(label, duration, Cancel);
        Countdowns.Add(countdown);
        Publish(countdown);
    }

    public void AddReminder(string text, DateTime due, bool daily)
    {
        Reminders.Add(new ReminderItem { Text = text, DueAt = due, Daily = daily });
        settings.Save();
    }

    public void RemoveReminder(ReminderItem item)
    {
        Reminders.Remove(item);
        settings.Save();
    }

    private void Cancel(TimerActivity countdown)
    {
        Countdowns.Remove(countdown);
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(countdown.Id, null));
    }

    private void Publish(IslandActivity activity) =>
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(activity.Id, activity));

    private void OnTick()
    {
        foreach (var countdown in Countdowns.ToList())
            if (countdown.Tick())
            {
                Cancel(countdown);
                Alert("倒计时结束", string.IsNullOrEmpty(countdown.Label) ? FormatSpan(countdown.Total) : countdown.Label, snooze: null);
            }

        var now = DateTime.Now;
        var changed = false;
        foreach (var item in Reminders.Where(r => r.DueAt <= now).ToList())
        {
            if (item.Daily)
            {
                if (now - item.DueAt <= DailyGrace) Alert("提醒", item.Text, () => AddReminder(item.Text, DateTime.Now + SnoozeFor, daily: false));
                do item.DueAt = item.DueAt.AddDays(1); while (item.DueAt <= now);
            }
            else
            {
                Alert("提醒", item.Text, () => AddReminder(item.Text, DateTime.Now + SnoozeFor, daily: false));
                Reminders.Remove(item);
            }
            changed = true;
        }
        if (changed) settings.Save();
    }

    private void Alert(string title, string text, Action? snooze)
    {
        ReminderActivity? alert = null;
        alert = new ReminderActivity(title, text,
            dismiss: () => ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(alert!.Id, null)),
            snooze: snooze is null ? null : () =>
            {
                snooze();
                ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(alert!.Id, null));
            });
        Publish(alert);
        SystemSounds.Asterisk.Play();
    }

    private static string FormatSpan(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分钟"
        : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} 分钟"
        : $"{(int)span.TotalSeconds} 秒";
}
