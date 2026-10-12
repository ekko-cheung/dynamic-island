using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using DynamicIsland.Providers.Media;
using DynamicIsland.Providers.Notifications;
using DynamicIsland.Shell;
using H.NotifyIcon;
using Windows.ApplicationModel;

namespace DynamicIsland;

public partial class App : Application
{
    private const string StartupTaskId = "DynamicIslandStartup";

    private Mutex? _singleInstance;
    private TaskbarIcon? _tray;
    private IslandWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "DynamicIsland.SingleInstance", out var isFirst);
        if (!isFirst) { Shutdown(); return; }

        var hub = new ActivityHub();
        _window = new IslandWindow(new IslandViewModel(hub));
        _window.Show();
        CreateTray();

        // Providers are registered here; add progress providers next to these lines.
        await TryAddProviderAsync(new MediaActivityProvider(), "媒体");
        await TryAddProviderAsync(new NotificationActivityProvider(), "通知");

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

        _tray = new TaskbarIcon
        {
            ToolTipText = "Dynamic Island",
            Icon = LoadTrayIcon(),
            ContextMenu = new ContextMenu { Items = { autostart, new Separator(), exit } },
        };
        _tray.ForceCreate();

        // Reflect the current state once the menu item exists.
        _ = Dispatcher.InvokeAsync(async () => autostart.IsChecked = await GetAutostartAsync());
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
