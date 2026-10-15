using System.Management;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Providers.SystemLevels;

/// <summary>
/// Shows the volume / brightness bar when either changes: volume via the default render endpoint,
/// brightness via WMI (only exposed by internal panels, e.g. laptops; external monitors never raise it).
/// </summary>
public sealed class SystemLevelProvider : IActivityProvider
{
    private static readonly TimeSpan ShowFor = TimeSpan.FromMilliseconds(1800);

    private readonly LevelActivity _volume = new(LevelType.Volume);
    private readonly LevelActivity _brightness = new(LevelType.Brightness);
    private readonly Dictionary<LevelType, DispatcherTimer> _timers = [];
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifications;
    private MMDevice? _device;
    private ManagementEventWatcher? _brightnessWatcher;

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public Task StartAsync()
    {
        _enumerator = new MMDeviceEnumerator();
        // Rebind when the default output switches (headphones plugged in, etc.). Captures the UI sync context.
        _notifications = _enumerator.CreateNotificationClient();
        _notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia) BindDefaultDevice();
        };
        BindDefaultDevice();
        StartBrightnessWatcher();
        return Task.CompletedTask;
    }

    private void BindDefaultDevice()
    {
        if (_device is not null) _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
        try
        {
            _device = _enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
        }
        catch { _device = null; } // no output device right now
    }

    // Raised on a COM thread.
    private void OnVolume(AudioVolumeNotificationData data) =>
        Application.Current?.Dispatcher.InvokeAsync(() => Show(_volume, data.MasterVolume, data.Muted));

    private void StartBrightnessWatcher()
    {
        try
        {
            _brightnessWatcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"),
                new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _brightnessWatcher.EventArrived += (_, e) =>
            {
                var percent = Convert.ToInt32(e.NewEvent["Brightness"]);
                Application.Current?.Dispatcher.InvokeAsync(() => Show(_brightness, percent / 100.0, false));
            };
            _brightnessWatcher.Start();
        }
        catch { _brightnessWatcher = null; } // WMI brightness not available on this machine
    }

    // UI thread.
    private void Show(LevelActivity activity, double level, bool muted)
    {
        activity.Update(level, muted);
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(activity.Id, activity));

        if (!_timers.TryGetValue(activity.Type, out var timer))
        {
            timer = new DispatcherTimer { Interval = ShowFor };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(activity.Id, null));
            };
            _timers[activity.Type] = timer;
        }
        timer.Stop();
        timer.Start();
    }
}
