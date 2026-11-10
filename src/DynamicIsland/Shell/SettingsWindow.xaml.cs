using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Providers.Claude;
using DynamicIsland.Settings;

namespace DynamicIsland.Shell;

/// <summary>The settings window. Every change is applied and saved immediately; there is no Apply button.</summary>
public partial class SettingsWindow : Window
{

    // Glyphs are Segoe Fluent Icons / MDL2: music, notification, volume, microphone, camera, location, command prompt.
    private static readonly (ActivityKind Kind, string Name, string Glyph)[] Kinds =
    [
        (ActivityKind.Media, "音乐", ""),
        (ActivityKind.AppNotification, "通知", ""),
        (ActivityKind.Progress, "音量 / 亮度", ""),
        (ActivityKind.Microphone, "麦克风", ""),
        (ActivityKind.Camera, "摄像头", ""),
        (ActivityKind.Location, "定位", ""),
        (ActivityKind.Claude, "Claude 状态", ""),
        (ActivityKind.Timer, "倒计时", ""),
        (ActivityKind.Reminder, "定时提醒", ""),
    ];

    private static readonly (IslandSlot Slot, string Name)[] Slots =
    [
        (IslandSlot.Main, "主岛"), (IslandSlot.Left, "左小岛"), (IslandSlot.Right, "右小岛"),
    ];

    private readonly AppSettings _settings;
    private readonly ActivityHub _hub;
    private readonly Func<bool, Task<bool>> _setAutostart;
    private readonly Action _repositionIsland;

    public SettingsWindow(AppSettings settings, ActivityHub hub, bool autostartAvailable, bool autostart,
        Func<bool, Task<bool>> setAutostart, Action repositionIsland)
    {
        _repositionIsland = repositionIsland;
        _settings = settings;
        _hub = hub;
        _setAutostart = setAutostart;
        InitializeComponent();

        BuildPlacementRows();
        BuildMonitorRows();
        Activated += (_, _) => BuildMonitorRows(); // displays may have been plugged in meanwhile
        StatsIdleBox.IsChecked = settings.ShowStatsWhenIdle;
        StatsBusyBox.IsChecked = settings.ShowStatsWhenBusy;
        SnapBox.IsChecked = settings.SnapToEdges;
        SyncOffsets();
        Activated += (_, _) => SyncOffsets(); // the island may have been dragged meanwhile
        PortBox.Text = settings.ApiPort.ToString();
        AutostartBox.IsChecked = autostart;
        AutostartBox.IsEnabled = autostartAvailable;
        AutostartHint.Visibility = autostartAvailable ? Visibility.Collapsed : Visibility.Visible;
        RefreshClaudeHooks();
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

    private void StatsBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowStatsWhenIdle = StatsIdleBox.IsChecked == true;
        _settings.ShowStatsWhenBusy = StatsBusyBox.IsChecked == true;
        _settings.Save();
    }

    private void SyncOffsets()
    {
        OffsetXSlider.Value = _settings.OffsetX;
        OffsetYSlider.Value = _settings.OffsetY;
        OffsetXText.Text = _settings.OffsetX.ToString();
        OffsetYText.Text = _settings.OffsetY.ToString();
    }

    private void SnapBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.SnapToEdges = SnapBox.IsChecked == true;
        _settings.Save();
    }

    private void Offset_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return; // initial values are set from the settings in the constructor
        if ((int)OffsetXSlider.Value == _settings.OffsetX && (int)OffsetYSlider.Value == _settings.OffsetY) return;
        _settings.OffsetX = (int)OffsetXSlider.Value;
        _settings.OffsetY = (int)OffsetYSlider.Value;
        OffsetXText.Text = _settings.OffsetX.ToString();
        OffsetYText.Text = _settings.OffsetY.ToString();
        _settings.Save();
        _repositionIsland();
    }

    private void OffsetReset_Click(object sender, RoutedEventArgs e)
    {
        OffsetXSlider.Value = 0;
        OffsetYSlider.Value = 0;
    }

    private void BuildMonitorRows()
    {
        MonitorPanel.Children.Clear();
        var segment = (Style)FindResource("Segment");
        var monitors = Monitors.All();
        // A saved display that is not connected right now behaves as the primary one, so show it that way.
        var selected = monitors.Any(m => m.DeviceName == _settings.Monitor && !m.IsPrimary) ? _settings.Monitor : "";

        foreach (var monitor in monitors)
        {
            var key = monitor.IsPrimary ? "" : monitor.DeviceName;
            var radio = new RadioButton
            {
                Content = monitor.Label, GroupName = "Monitor", Style = segment, IsChecked = key == selected,
            };
            radio.Checked += (_, _) =>
            {
                if (_settings.Monitor == key) return;
                _settings.Monitor = key;
                _settings.Save();
                _repositionIsland();
            };
            MonitorPanel.Children.Add(radio);
        }
    }

    private ClaudeHooksState? _claudeState;

    private void RefreshClaudeHooks(string? message = null, bool isError = false)
    {
        try
        {
            _claudeState = ClaudeHooksInstaller.GetState();
            ClaudeStatus.Text = _claudeState switch
            {
                ClaudeHooksState.Installed => "已添加",
                ClaudeHooksState.Partial => "部分已添加(点击补全)",
                _ => "未添加",
            };
            ClaudeButton.Content = _claudeState == ClaudeHooksState.Installed ? "移除配置" : "添加配置";
            // The hooks post to the local API, so it has to be on.
            ClaudeButton.IsEnabled = _settings.ApiPort > 0 || _claudeState != ClaudeHooksState.NotInstalled;
            if (_settings.ApiPort == 0 && _claudeState == ClaudeHooksState.NotInstalled)
                message ??= "本地接口已关闭(端口为 0),开启后才能添加。";
        }
        catch (Exception ex)
        {
            _claudeState = null;
            ClaudeStatus.Text = "无法读取配置";
            ClaudeButton.IsEnabled = false;
            message = ex.Message;
            isError = true;
        }

        ClaudeMessage.Text = message;
        ClaudeMessage.Foreground = isError ? Brushes.Firebrick : (Brush)FindResource("Muted");
        ClaudeMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClaudeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_claudeState == ClaudeHooksState.Installed)
            {
                ClaudeHooksInstaller.Uninstall();
                RefreshClaudeHooks("已移除。");
            }
            else
            {
                ClaudeHooksInstaller.Install(_settings.ApiPort);
                RefreshClaudeHooks("已添加,新开的 Claude Code 会话生效。");
            }
        }
        catch (Exception ex)
        {
            RefreshClaudeHooks("操作失败:" + ex.Message, isError: true);
        }
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
