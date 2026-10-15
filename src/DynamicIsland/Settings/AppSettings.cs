using System.IO;
using System.Text.Json;
using DynamicIsland.Core;

namespace DynamicIsland.Settings;

/// <summary>User settings persisted as JSON under %AppData%. Access from the UI thread only.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Port of the local HTTP endpoint (127.0.0.1). 0 disables it.</summary>
    public int ApiPort { get; set; } = 17890;

    /// <summary>When true, only notifications from <see cref="AllowedApps"/> are shown.</summary>
    public bool NotificationFilterEnabled { get; set; }

    /// <summary>AppUserModelIds of apps whose notifications may be shown.</summary>
    public HashSet<string> AllowedApps { get; set; } = [];

    /// <summary>Apps we have seen a notification from: AppUserModelId → display name. Feeds the tray menu.</summary>
    public Dictionary<string, string> KnownApps { get; set; } = [];

    /// <summary>Where each <see cref="ActivityKind"/> is shown: ActivityKind name → <see cref="IslandSlot"/> name.</summary>
    public Dictionary<string, string> Placements { get; set; } = [];

    public IslandSlot SlotFor(ActivityKind kind)
    {
        if (kind == ActivityKind.SystemNotification) kind = ActivityKind.AppNotification; // one setting for both
        if (Placements.TryGetValue(kind.ToString(), out var name) && Enum.TryParse<IslandSlot>(name, out var slot))
            return slot;
        return kind switch
        {
            ActivityKind.Microphone => IslandSlot.Left,
            ActivityKind.Camera or ActivityKind.Location => IslandSlot.Right,
            _ => IslandSlot.Main,
        };
    }

    public void SetSlot(ActivityKind kind, IslandSlot slot) => Placements[kind.ToString()] = slot.ToString();

    public bool IsAllowed(string appId) => !NotificationFilterEnabled || AllowedApps.Contains(appId);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* corrupt or unreadable file: start fresh */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}
