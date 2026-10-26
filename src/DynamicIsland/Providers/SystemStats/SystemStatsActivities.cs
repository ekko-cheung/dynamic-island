using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.SystemStats;

// Below media (0): these only fill space nothing else wants.
public abstract class StatsActivity(string id, ActivityKind kind) : IslandActivity
{
    public override string Id { get; } = id;
    public override ActivityKind Kind { get; } = kind;
    public override int Priority => -1;
}

/// <summary>Everything at once, for the main island when it has nothing else to show.</summary>
public sealed partial class SystemStatsActivity() : StatsActivity("stats", ActivityKind.SystemStats)
{
    [ObservableProperty] private double _cpuValue;
    [ObservableProperty] private double _memoryValue;
    [ObservableProperty] private string _cpuText = "–";
    [ObservableProperty] private string _memoryText = "–";
    [ObservableProperty] private string _uploadText = "–";
    [ObservableProperty] private string _downloadText = "–";
}

/// <summary>Memory only, for a small island while the main island is busy.</summary>
public sealed partial class MemoryStatActivity() : StatsActivity("stats-memory", ActivityKind.MemoryStat)
{
    [ObservableProperty] private double _memoryValue;
    [ObservableProperty] private string _memoryText = "–";
}

/// <summary>Network speed only, for a small island while the main island is busy.</summary>
public sealed partial class NetStatActivity() : StatsActivity("stats-net", ActivityKind.NetStat)
{
    [ObservableProperty] private string _uploadText = "–";
    [ObservableProperty] private string _downloadText = "–";
}
