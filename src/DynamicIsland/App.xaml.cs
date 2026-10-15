using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using DynamicIsland.Providers.Api;
using DynamicIsland.Providers.Media;
using DynamicIsland.Providers.Microphone;
using DynamicIsland.Providers.Notifications;
using DynamicIsland.Providers.SystemLevels;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "DynamicIsland.SingleInstance", out var isFirst);
        if (!isFirst) { Shutdown(); return; }

        // Sticky notifications carry a close button, which only the main island has.
        var hub = _hub = new ActivityHub(a =>
            a is NotificationActivity { IsSticky: true } ? IslandSlot.Main : _settings.SlotFor(a.Kind));
        _window = new IslandWindow(new IslandViewModel(hub));
        _window.Show();
        CreateTray();

        // Providers are registered here; add progress providers next to these lines.
        await TryAddProviderAsync(new MediaActivityProvider(), "媒体");
        // Reading system notifications needs package identity; without it, skip the provider quietly.
        if (PackageIdentity.IsPackaged)
            await TryAddProviderAsync(new NotificationActivityProvider(_settings), "通知");
        await TryAddProviderAsync(new SystemLevelProvider(), "音量/亮度");
        await TryAddProviderAsync(new MicrophoneActivityProvider(), "麦克风");
        if (_settings.ApiPort > 0)
            await TryAddProviderAsync(new LocalApiProvider(_settings), "本地接口");

        async Task TryAddProviderAsync(IActivityProvider provider, string name)
        {
            try { await hub.AddProviderAsync(provider); }
            catch (Exception ex) { MessageBox.Show($"{name}模块启动失败:\n{ex.Message}", "Dynamic Island"); }
        }
    }

    private void CreateTray()
    {
        var autostart = new MenuItem { Header = "开机自启", IsCheckable = true };
        autostart.Click += async (_, _) => autostart.IsChecked = await SetAutostartAsync(autostart.IsChecked);
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Shutdown();

        var filter = new MenuItem { Header = "仅显示选中应用的通知", IsCheckable = true, IsChecked = _settings.NotificationFilterEnabled };
        filter.Click += (_, _) =>
        {
            _settings.NotificationFilterEnabled = filter.IsChecked;
            _settings.Save();
        };
        var apps = new MenuItem { Header = "通知应用(勾选 = 允许)" };
        apps.SubmenuOpened += (_, _) => RebuildAppsMenu(apps);
        RebuildAppsMenu(apps);

        _tray = new TaskbarIcon
        {
            ToolTipText = "Dynamic Island",
            Icon = LoadTrayIcon(),
            ContextMenu = new ContextMenu { Items = { filter, apps, BuildPlacementMenu(), new Separator(), autostart, new Separator(), exit } },
        };
        _tray.ForceCreate();

        // Reflect the current state once the menu item exists.
        _ = Dispatcher.InvokeAsync(async () => autostart.IsChecked = await GetAutostartAsync());
    }

    // Per activity kind: which island shows it. Radio-style: exactly one slot is checked.
    private MenuItem BuildPlacementMenu()
    {
        var root = new MenuItem { Header = "显示位置" };
        var kinds = new (ActivityKind Kind, string Name)[]
        {
            (ActivityKind.Media, "音乐"),
            (ActivityKind.AppNotification, "通知"),
            (ActivityKind.Progress, "音量/亮度"),
            (ActivityKind.Microphone, "麦克风"),
        };
        var slots = new (IslandSlot Slot, string Name)[]
        {
            (IslandSlot.Main, "主灵动岛"), (IslandSlot.Left, "左侧小岛"), (IslandSlot.Right, "右侧小岛"),
        };

        foreach (var (kind, kindName) in kinds)
        {
            var group = new MenuItem { Header = kindName };
            var items = new List<(MenuItem Item, IslandSlot Slot)>();
            foreach (var (slot, slotName) in slots)
            {
                var item = new MenuItem { Header = slotName, IsCheckable = true, IsChecked = _settings.SlotFor(kind) == slot };
                item.Click += (_, _) =>
                {
                    _settings.SetSlot(kind, slot);
                    _settings.Save();
                    foreach (var (other, otherSlot) in items) other.IsChecked = otherSlot == slot;
                    _hub?.Refresh();
                };
                items.Add((item, slot));
                group.Items.Add(item);
            }
            root.Items.Add(group);
        }
        return root;
    }

    // Apps appear here once they have sent a notification while the island was running.
    private void RebuildAppsMenu(MenuItem apps)
    {
        apps.Items.Clear();
        if (_settings.KnownApps.Count == 0)
        {
            apps.Items.Add(new MenuItem { Header = "(收到通知后会出现在这里)", IsEnabled = false });
            return;
        }

        foreach (var (id, name) in _settings.KnownApps.OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = _settings.AllowedApps.Contains(id) };
            item.Click += (_, _) =>
            {
                if (item.IsChecked) _settings.AllowedApps.Add(id);
                else _settings.AllowedApps.Remove(id);
                _settings.Save();
            };
            apps.Items.Add(item);
        }
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
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
