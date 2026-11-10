using System.Collections.ObjectModel;
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

    /// <summary>Device name of the display the island sits on (e.g. \\.\DISPLAY2). Empty means the primary display.</summary>
    public string Monitor { get; set; } = "";

    /// <summary>Horizontal shift from the display's center, in device-independent pixels (negative = left).</summary>
    public int OffsetX { get; set; }

    /// <summary>Gap between the island and the top edge of the display, in device-independent pixels. 0 = flush (notch).</summary>
    public int OffsetY { get; set; }

    /// <summary>While dragging, snap to the display's edges and center when within a few pixels of them.</summary>
    public bool SnapToEdges { get; set; } = true;

    /// <summary>Blur whatever is behind the island instead of drawing it solid black.</summary>
    public bool FrostedGlass { get; set; }

    /// <summary>Show CPU, memory and network speed on the main island when it has nothing else to show.</summary>
    public bool ShowStatsWhenIdle { get; set; } = true;

    /// <summary>Show memory and network speed on the small islands while the main island is busy.</summary>
    public bool ShowStatsWhenBusy { get; set; } = true;

    /// <summary>Scheduled reminders that have not fired yet.</summary>
    public ObservableCollection<ReminderItem> Reminders { get; set; } = [];

    /// <summary>Where each <see cref="ActivityKind"/> is shown: ActivityKind name → <see cref="IslandSlot"/> name.</summary>
    public Dictionary<string, string> Placements { get; set; } = [];

    public IslandSlot SlotFor(ActivityKind kind)
    {
        if (kind == ActivityKind.SystemNotification) kind = ActivityKind.AppNotification; // one setting for both
        if (Placements.TryGetValue(kind.ToString(), out var name) && Enum.TryParse<IslandSlot>(name, out var slot))
            return slot;
        return kind switch
        {
            ActivityKind.Microphone or ActivityKind.MemoryStat => IslandSlot.Left,
            ActivityKind.Camera or ActivityKind.Location or ActivityKind.NetStat => IslandSlot.Right,
            _ => IslandSlot.Main,
        };
    }

    public void SetSlot(ActivityKind kind, IslandSlot slot) => Placements[kind.ToString()] = slot.ToString();

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
