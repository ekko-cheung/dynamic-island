using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Providers.Timers;
using DynamicIsland.Settings;

namespace DynamicIsland.Shell;

/// <summary>Create and manage countdowns and reminders. The island itself is hidden while idle, so this is the way in.</summary>
public partial class TimersWindow : Window
{
    private static readonly int[] PresetMinutes = [1, 5, 10, 15, 25, 45, 60];

    private readonly TimerReminderProvider _provider;

    public TimersWindow(TimerReminderProvider provider)
    {
        _provider = provider;
        InitializeComponent();

        var ghost = (Style)FindResource("GhostButton");
        foreach (var minutes in PresetMinutes)
        {
            var button = new Button { Content = $"{minutes} 分钟", Style = ghost, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => StartCountdown(minutes);
            Presets.Children.Add(button);
        }

        CountdownList.ItemsSource = provider.Countdowns;
        ReminderList.ItemsSource = provider.Reminders;
        provider.Countdowns.CollectionChanged += (_, _) => RefreshEmptyStates();
        provider.Reminders.CollectionChanged += OnRemindersChanged;
        Closed += (_, _) => provider.Reminders.CollectionChanged -= OnRemindersChanged;
        RefreshEmptyStates();
    }

    private void OnRemindersChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshEmptyStates();

    private void RefreshEmptyStates()
    {
        CountdownsEmpty.Visibility = _provider.Countdowns.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RemindersEmpty.Visibility = _provider.Reminders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartCountdown(double minutes)
    {
        _provider.StartCountdown(TimeSpan.FromMinutes(minutes), CountdownLabel.Text.Trim());
        CountdownError.Visibility = Visibility.Collapsed;
    }

    private void StartCustom_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(CustomMinutes.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var minutes) && minutes is > 0 and <= 24 * 60)
        {
            StartCountdown(minutes);
            CustomMinutes.Clear();
            return;
        }
        CountdownError.Text = "请输入 0 到 1440 之间的分钟数。";
        CountdownError.Visibility = Visibility.Visible;
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent before the rest of the tree exists.
        if (ReminderWhen is null) return;
        var atTime = AtTimeMode.IsChecked == true;
        ReminderWhen.Text = atTime ? "09:00" : "30";
        ReminderWhenHint.Text = atTime ? "24 小时制,如 18:30;已过的时间算明天" : "分钟数,如 30";
        DailyRow.Visibility = atTime ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddReminder_Click(object sender, RoutedEventArgs e)
    {
        var text = ReminderText.Text.Trim();
        if (text.Length == 0) { ShowReminderError("请填写提醒内容。"); return; }

        DateTime due;
        var daily = false;
        if (AtTimeMode.IsChecked == true)
        {
            if (!TimeSpan.TryParseExact(ReminderWhen.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var timeOfDay) || timeOfDay >= TimeSpan.FromDays(1))
            { ShowReminderError("时间格式不对,请用 24 小时制,如 18:30。"); return; }

            due = DateTime.Today + timeOfDay;
            if (due <= DateTime.Now) due = due.AddDays(1);
            daily = DailyBox.IsChecked == true;
        }
        else
        {
            if (!double.TryParse(ReminderWhen.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var minutes) || minutes is <= 0 or > 366 * 24 * 60)
            { ShowReminderError("请输入大于 0 的分钟数。"); return; }
            due = DateTime.Now.AddMinutes(minutes);
        }

        _provider.AddReminder(text, due, daily);
        ReminderText.Clear();
        ReminderError.Visibility = Visibility.Collapsed;
    }

    private void ShowReminderError(string message)
    {
        ReminderError.Text = message;
        ReminderError.Visibility = Visibility.Visible;
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ReminderItem item }) _provider.RemoveReminder(item);
    }
}
