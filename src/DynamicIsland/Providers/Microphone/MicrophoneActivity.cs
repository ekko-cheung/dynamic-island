using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.Microphone;

/// <summary>Shown while at least one app is capturing from the microphone.</summary>
public sealed partial class MicrophoneActivity : IslandActivity
{
    public override string Id => "microphone";
    public override ActivityKind Kind => ActivityKind.Microphone;

    // Above media (0), below notifications (10) when they share a slot.
    public override int Priority => 5;

    [ObservableProperty] private string _appsText = "";

    public void Update(IReadOnlyCollection<string> apps)
    {
        AppsText = string.Join("\n", apps);
        Touch();
    }
}
