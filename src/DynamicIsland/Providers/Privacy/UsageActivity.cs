using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.Privacy;

/// <summary>Shown while at least one app is using a privacy-sensitive device (microphone, camera).</summary>
public abstract partial class UsageActivity(string id, ActivityKind kind) : IslandActivity
{
    public override string Id { get; } = id;
    public override ActivityKind Kind { get; } = kind;

    // Above media (0), below notifications (10) when they share a slot.
    public override int Priority => 5;

    [ObservableProperty] private string _appsText = "";

    public void Update(IReadOnlyCollection<string> apps)
    {
        AppsText = string.Join("\n", apps);
        Touch();
    }
}

public sealed class MicrophoneActivity() : UsageActivity("microphone", ActivityKind.Microphone);

public sealed class CameraActivity() : UsageActivity("camera", ActivityKind.Camera);
