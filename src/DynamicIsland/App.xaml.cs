using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using DynamicIsland.Providers.Api;
using DynamicIsland.Providers.Claude;
using DynamicIsland.Providers.Media;
using DynamicIsland.Providers.Privacy;
using DynamicIsland.Providers.Notifications;
using DynamicIsland.Providers.SystemLevels;
using DynamicIsland.Providers.SystemStats;
using DynamicIsland.Providers.Timers;
using DynamicIsland.Settings;
using DynamicIsland.Shell;
using H.NotifyIcon;
using Windows.ApplicationModel;

namespace DynamicIsland;

public partial class App : Application
{
    private const string StartupTaskId = "DynamicIslandStartup";

    private readonly AppSettings _settings = AppSettings.Load();
    private Mutex? _singleInstance;
    private TaskbarIcon? _tray;
    private IslandWindow? _window;
    private ActivityHub? _hub;
    private SettingsWindow? _settingsWindow;
    private TimersWindow? _timersWindow;
    private TimerReminderProvider? _timers;
    private GlobalHotKey? _hotKey;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "DynamicIsland.SingleInstance", out var isFirst);
        if (!isFirst) { Shutdown(); return; }

        // Sticky notifications carry a close button, which only the main island has.
        var hub = _hub = new ActivityHub(a =>
            a is NotificationActivity { IsSticky: true } ? IslandSlot.Main : _settings.SlotFor(a.Kind));
        _window = new IslandWindow(new IslandViewModel(hub), _settings);
        _window.Show();
        CreateTray();
        if (e.Args.Contains("--settings")) ShowSettings();

        // Providers are registered here; add progress providers next to these lines.
        await TryAddProviderAsync(new MediaActivityProvider(), "媒体");
        // Reading system notifications needs package identity; without it, skip the provider quietly.
        if (PackageIdentity.IsPackaged)
            await TryAddProviderAsync(new NotificationActivityProvider(_settings), "通知");
        await TryAddProviderAsync(new SystemLevelProvider(), "音量/亮度");
        await TryAddProviderAsync(new CapabilityUsageProvider("microphone", new MicrophoneActivity()), "麦克风");
        await TryAddProviderAsync(new CapabilityUsageProvider("webcam", new CameraActivity()), "摄像头");
        await TryAddProviderAsync(new CapabilityUsageProvider("location", new LocationActivity()), "定位");
        await TryAddProviderAsync(new SystemStatsProvider(_settings, hub), "系统状态");
        var timers = _timers = new TimerReminderProvider(_settings);
        await TryAddProviderAsync(timers, "计时与提醒");
        // Ctrl+Alt+T: the island is hidden when idle, so this is the way in. Silently skipped if another app owns it.
        _hotKey = GlobalHotKey.TryRegister(GlobalHotKey.Ctrl | GlobalHotKey.Alt, 'T', ShowTimers);

        // Claude Code status arrives as hook events over the local API.
        var claude = new ClaudeActivityProvider();
        await TryAddProviderAsync(claude, "Claude 状态");
        if (_settings.ApiPort > 0)
            await TryAddProviderAsync(new LocalApiProvider(_settings, claude, timers), "本地接口");

        async Task TryAddProviderAsync(IActivityProvider provider, string name)
        {
            try { await hub.AddProviderAsync(provider); }
            catch (Exception ex) { MessageBox.Show($"{name}模块启动失败:\n{ex.Message}", "Dynamic Island"); }
        }
    }

    private void CreateTray()
    {
        var timers = new MenuItem { Header = "计时与提醒", InputGestureText = "Ctrl+Alt+T" };
        timers.Click += (_, _) => ShowTimers();
        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => ShowSettings();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Shutdown();

        _tray = new TaskbarIcon
        {
            ToolTipText = "Dynamic Island",
            Icon = LoadTrayIcon(),
            ContextMenu = new ContextMenu { Items = { timers, settings, new Separator(), exit } },
        };
        _tray.TrayMouseDoubleClick += (_, _) => ShowSettings();
        _tray.ForceCreate();
    }

    private void ShowTimers()
    {
        if (_timers is null) return;
        if (_timersWindow is { IsLoaded: true })
        {
            _timersWindow.Activate();
            return;
        }

        _timersWindow = new TimersWindow(_timers);
        _timersWindow.Show();
        _timersWindow.Activate();
    }

    private async void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.RefreshApps();
            _settingsWindow.Activate();
            return;
        }

        var packaged = PackageIdentity.IsPackaged;
        _settingsWindow = new SettingsWindow(_settings, _hub!, packaged, packaged && await GetAutostartAsync(), SetAutostartAsync,
            () => _window?.PositionAtTop());
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var info = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        return new System.Drawing.Icon(info.Stream);
    }

    private static async Task<bool> GetAutostartAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch { return false; } // not running as a packaged app
    }

    private static async Task<bool> SetAutostartAsync(bool enable)
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (!enable)
            {
                task.Disable();
                return false;
            }
            // Returns DisabledByUser if the user turned it off in Settings → Apps → Startup.
            return await task.RequestEnableAsync() is StartupTaskState.Enabled;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置开机自启失败:\n{ex.Message}", "Dynamic Island");
            return await GetAutostartAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
