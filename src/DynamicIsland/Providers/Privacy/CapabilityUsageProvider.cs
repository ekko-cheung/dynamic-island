using System.IO;
using System.Windows.Threading;
using DynamicIsland.Core;
using Microsoft.Win32;

namespace DynamicIsland.Providers.Privacy;

/// <summary>
/// Detects apps using the microphone, camera or location from the same registry data Windows uses for its own tray indicator:
/// each app under the ConsentStore has LastUsedTimeStart / LastUsedTimeStop, and Stop == 0 means "in use now".
/// </summary>
/// <param name="capability">ConsentStore subkey: "microphone", "webcam" or "location".</param>
public sealed class CapabilityUsageProvider(string capability, UsageActivity activity) : IActivityProvider
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _shown;
    private string _lastApps = "";

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public Task StartAsync()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
        return Task.CompletedTask;
    }

    private void Poll()
    {
        var apps = ReadActiveApps();
        if (apps.Count == 0)
        {
            if (!_shown) return;
            _shown = false;
            _lastApps = "";
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(activity.Id, null));
            return;
        }

        var text = string.Join("\n", apps);
        if (_shown && text == _lastApps) return;
        _lastApps = text;
        activity.Update(apps);
        _shown = true;
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(activity.Id, activity));
    }

    private List<string> ReadActiveApps()
    {
        var apps = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(ConsentStore + capability);
            if (root is null) return [];
            Collect(root, apps, nonPackaged: false);
            using var nonPackaged = root.OpenSubKey("NonPackaged");
            if (nonPackaged is not null) Collect(nonPackaged, apps, nonPackaged: true);
        }
        catch { /* registry unreadable: report nothing */ }
        return [.. apps];
    }

    private static void Collect(RegistryKey parent, SortedSet<string> apps, bool nonPackaged)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (name == "NonPackaged") continue;
            using var app = parent.OpenSubKey(name);
            if (app?.GetValue("LastUsedTimeStart") is long start && start > 0 &&
                app.GetValue("LastUsedTimeStop") is long stop && stop == 0)
            {
                var display = DisplayName(name);
                // An app killed mid-use never writes its stop time; drop entries whose process is gone.
                if (nonPackaged && System.Diagnostics.Process.GetProcessesByName(display).Length == 0) continue;
                apps.Add(display);
            }
        }
    }

    // NonPackaged keys are exe paths with '\' replaced by '#'; packaged ones are "Name_publisherId".
    private static string DisplayName(string key)
    {
        var hash = key.LastIndexOf('#');
        if (hash >= 0) return Path.GetFileNameWithoutExtension(key[(hash + 1)..]);
        var underscore = key.IndexOf('_');
        return underscore > 0 ? key[..underscore] : key;
    }
}
