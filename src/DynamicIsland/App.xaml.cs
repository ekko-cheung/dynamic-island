using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using DynamicIsland.Providers.Media;
using DynamicIsland.Shell;
using H.NotifyIcon;
using Microsoft.Win32;

namespace DynamicIsland;

public partial class App : Application
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DynamicIsland";

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

        // Providers are registered here; add notification / progress providers next to this line.
        try { await hub.AddProviderAsync(new MediaActivityProvider()); }
        catch (Exception ex) { MessageBox.Show($"Failed to start media provider:\n{ex.Message}", "Dynamic Island"); }
    }

    private void CreateTray()
    {
        var autostart = new MenuItem { Header = "开机自启", IsCheckable = true, IsChecked = IsAutostartEnabled() };
        autostart.Click += (_, _) => SetAutostart(autostart.IsChecked);
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Shutdown();

        _tray = new TaskbarIcon
        {
            ToolTipText = "Dynamic Island",
            Icon = LoadTrayIcon(),
            ContextMenu = new ContextMenu { Items = { autostart, new Separator(), exit } },
        };
        _tray.ForceCreate();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var info = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        return new System.Drawing.Icon(info.Stream);
    }

    private static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    private static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;
        if (enabled) key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
