using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Providers.Notifications;

/// <summary>
/// Mirrors new Toast notifications from all apps (IM clients included) onto the island.
/// Requires package identity (MSIX) and the user's permission in Settings → Privacy → Notifications.
/// </summary>
public sealed class NotificationActivityProvider : IActivityProvider
{
    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(6);

    private readonly AppSettings _settings;
    private readonly HashSet<uint> _seen = [];

    public NotificationActivityProvider(AppSettings settings) => _settings = settings;

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public async Task StartAsync()
    {
        var listener = UserNotificationListener.Current;
        var access = await listener.RequestAccessAsync();
        if (access != UserNotificationListenerAccessStatus.Allowed)
            throw new InvalidOperationException($"没有通知读取权限({access})。请在 设置 → 隐私和安全性 → 通知 中允许本应用。");

        // Notifications already sitting in the action center at startup are not replayed.
        foreach (var n in await listener.GetNotificationsAsync(NotificationKinds.Toast))
        {
            _seen.Add(n.Id);
            Remember(n.AppInfo);
        }

        listener.NotificationChanged += (_, args) =>
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try { await OnNotificationAddedAsync(listener, args); }
                catch { /* notification may be dismissed before we read it */ }
            });
    }

    /// <summary>Records the app so it shows up in the tray's allow-list menu.</summary>
    private void Remember(Windows.ApplicationModel.AppInfo info)
    {
        var id = info.AppUserModelId;
        var name = info.DisplayInfo.DisplayName;
        if (_settings.KnownApps.TryGetValue(id, out var existing) && existing == name) return;
        _settings.KnownApps[id] = name;
        _settings.Save();
    }

    private async Task OnNotificationAddedAsync(UserNotificationListener listener, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added) return;
        if (!_seen.Add(args.UserNotificationId)) return;

        var n = listener.GetNotification(args.UserNotificationId);
        Remember(n.AppInfo);
        if (!_settings.IsAllowed(n.AppInfo.AppUserModelId)) return;

        var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
        var texts = binding?.GetTextElements().Select(t => t.Text).ToArray() ?? [];
        var title = texts.ElementAtOrDefault(0) ?? "";
        var body = string.Join(" ", texts.Skip(1));
        if (title.Length == 0 && body.Length == 0) return;

        var info = n.AppInfo.DisplayInfo;
        var icon = await StreamImage.LoadAsync(info.GetLogo(new Windows.Foundation.Size(64, 64)), 64);

        var id = $"notif-{args.UserNotificationId}";
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(id, new NotificationActivity(id, info.DisplayName, title, body, icon)));

        // Toasts are transient on the island; the full history stays in the action center.
        var timer = new DispatcherTimer { Interval = ShowFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(id, null));
        };
        timer.Start();
    }
}
