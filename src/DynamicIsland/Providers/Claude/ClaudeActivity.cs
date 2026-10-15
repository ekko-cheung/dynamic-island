using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.Claude;

public enum ClaudeState { Running, WaitingInput, Done }

/// <summary>One Claude Code session and what it is doing right now.</summary>
public sealed partial class ClaudeActivity(string sessionId, string project) : IslandActivity
{
    private static readonly Brush RunningBrush = Frozen("#FFD97757");
    private static readonly Brush WaitingBrush = Frozen("#FFFFD60A");
    private static readonly Brush DoneBrush = Frozen("#FF30D158");

    public override string Id { get; } = $"claude-{sessionId}";
    public override ActivityKind Kind => ActivityKind.Claude;

    // Waiting for the user outranks the island's ambient items; running/done sit just above media.
    public override int Priority => State == ClaudeState.WaitingInput ? 8 : 3;

    public string Project { get; } = project;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StateText), nameof(AccentBrush))] private ClaudeState _state;
    [ObservableProperty] private string _message = "";

    public event EventHandler? StateChanged;

    public string StateText => State switch
    {
        ClaudeState.Running => "运行中",
        ClaudeState.WaitingInput => "等待输入",
        _ => "已完成",
    };

    public Brush AccentBrush => State switch
    {
        ClaudeState.Running => RunningBrush,
        ClaudeState.WaitingInput => WaitingBrush,
        _ => DoneBrush,
    };

    public void Set(ClaudeState state, string message)
    {
        var changed = State != state;
        State = state;
        Message = message;
        Touch();
        if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Brush Frozen(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
