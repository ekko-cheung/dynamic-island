using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.SystemLevels;

public enum LevelType { Volume, Brightness }

/// <summary>A transient system level readout (volume or brightness). One instance per type, updated in place.</summary>
public sealed partial class LevelActivity(LevelType type) : IslandActivity
{
    public LevelType Type { get; } = type;
    public override string Id { get; } = $"level-{type}";
    public override ActivityKind Kind => ActivityKind.Progress;

    // Above notifications (10): a level change is momentary feedback for something the user is doing right now.
    public override int Priority => 20;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Glyph), nameof(Percent))] private double _level; // 0..1
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Glyph), nameof(Percent))] private bool _isMuted;

    public int Percent => IsMuted ? 0 : (int)Math.Round(Level * 100);

    // Segoe MDL2 Assets: Volume, Mute, Brightness.
    public string Glyph => Type == LevelType.Brightness ? "" : IsMuted ? "" : "";

    public void Update(double level, bool muted)
    {
        Level = Math.Clamp(level, 0, 1);
        IsMuted = muted;
        Touch();
    }
}
