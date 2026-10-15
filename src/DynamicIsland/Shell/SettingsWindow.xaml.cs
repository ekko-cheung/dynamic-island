using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Settings;

namespace DynamicIsland.Shell;

/// <summary>The settings window. Every change is applied and saved immediately; there is no Apply button.</summary>
public partial class SettingsWindow : Window
{
    private sealed record AppRow(string Id, string Name, bool Allowed);

    // Glyphs are Segoe Fluent Icons / MDL2: music, notification, volume, microphone, camera.
    private static readonly (ActivityKind Kind, string Name, string Glyph)[] Kinds =
    [
        (ActivityKind.Media, "音乐", ""),
        (ActivityKind.AppNotification, "通知", ""),
        (ActivityKind.Progress, "音量 / 亮度", ""),
        (ActivityKind.Microphone, "麦克风", ""),
        (ActivityKind.Camera, "摄像头", ""),
    ];

    private static readonly (IslandSlot Slot, string Name)[] Slots =
    [
        (IslandSlot.Main, "主岛"), (IslandSlot.Left, "左小岛"), (IslandSlot.Right, "右小岛"),
    ];

    private readonly AppSettings _settings;
    private readonly ActivityHub _hub;
    private readonly Func<bool, Task<bool>> _setAutostart;

    public SettingsWindow(AppSettings settings, ActivityHub hub, bool autostartAvailable, bool autostart,
        Func<bool, Task<bool>> setAutostart)
    {
        _settings = settings;
        _hub = hub;
        _setAutostart = setAutostart;
        InitializeComponent();

        BuildPlacementRows();
        FilterBox.IsChecked = settings.NotificationFilterEnabled;
        PortBox.Text = settings.ApiPort.ToString();
        AutostartBox.IsChecked = autostart;
        AutostartBox.IsEnabled = autostartAvailable;
        AutostartHint.Visibility = autostartAvailable ? Visibility.Collapsed : Visibility.Visible;
        RefreshApps();
    }

    /// <summary>Re-reads the known-apps list, e.g. when the window is brought back to front.</summary>
    public void RefreshApps()
    {
        var rows = _settings.KnownApps
            .OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(a => new AppRow(a.Key, a.Value, _settings.AllowedApps.Contains(a.Key)))
            .ToList();
        AppsList.ItemsSource = rows;
        AppsEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildPlacementRows()
    {
        var segment = (Style)FindResource("Segment");
        var rowIcon = (Style)FindResource("RowIcon");

        foreach (var (kind, name, glyph) in Kinds)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock { Text = glyph, Style = rowIcon };
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var current = _settings.SlotFor(kind);
            foreach (var (slot, slotName) in Slots)
            {
                var radio = new RadioButton { Content = slotName, GroupName = kind.ToString(), Style = segment, IsChecked = slot == current };
                radio.Checked += (_, _) =>
                {
                    _settings.SetSlot(kind, slot);
                    _settings.Save();
                    _hub.Refresh();
                };
                buttons.Children.Add(radio);
            }
            var track = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(2),
                Child = buttons,
            };
            Grid.SetColumn(track, 2);

            row.Children.Add(icon);
            row.Children.Add(label);
            row.Children.Add(track);
            PlacementPanel.Children.Add(row);
        }
    }

    private void FilterBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.NotificationFilterEnabled = FilterBox.IsChecked == true;
        _settings.Save();
    }

    private void AppBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: AppRow row } box) return;
        if (box.IsChecked == true) _settings.AllowedApps.Add(row.Id);
        else _settings.AllowedApps.Remove(row.Id);
        _settings.Save();
    }

    private void PortBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PortBox.Text, out var port) && port is >= 0 and <= 65535)
        {
            _settings.ApiPort = port;
            _settings.Save();
        }
        else PortBox.Text = _settings.ApiPort.ToString(); // reject, keep the old value
    }

    private async void AutostartBox_Click(object sender, RoutedEventArgs e) =>
        AutostartBox.IsChecked = await _setAutostart(AutostartBox.IsChecked == true);
}
