using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.Claude;

/// <summary>The JSON Claude Code passes to a hook on stdin (only the fields we use).</summary>
public sealed record ClaudeHookEvent(
    [property: JsonPropertyName("hook_event_name")] string? EventName,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("cwd")] string? Cwd,
    [property: JsonPropertyName("tool_name")] string? ToolName,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("notification_type")] string? NotificationType);

/// <summary>
/// Turns Claude Code hook events (posted to the local API by a hook command) into per-session activities:
/// prompt/tool events mean running, a permission prompt means waiting for input, Stop means done.
/// </summary>
public sealed class ClaudeActivityProvider : IActivityProvider
{
    private static readonly TimeSpan DoneShowFor = TimeSpan.FromSeconds(10);
    // Esc-interrupts and crashes never send Stop, so unfinished sessions expire instead of lingering forever.
    private static readonly TimeSpan RunningExpiry = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WaitingExpiry = TimeSpan.FromMinutes(30);

    private sealed record Session(ClaudeActivity Activity, DispatcherTimer Timer);

    private readonly Dictionary<string, Session> _sessions = [];

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public Task StartAsync() => Task.CompletedTask;

    /// <summary>UI thread only.</summary>
    public void Report(ClaudeHookEvent e)
    {
        var id = string.IsNullOrEmpty(e.SessionId) ? "default" : e.SessionId;
        switch (e.EventName)
        {
            case "UserPromptSubmit":
                Set(id, e.Cwd, ClaudeState.Running, "");
                break;
            case "PreToolUse" or "PostToolUse":
                Set(id, e.Cwd, ClaudeState.Running, string.IsNullOrEmpty(e.ToolName) ? "" : $"正在使用 {e.ToolName}");
                break;
            case "Notification":
                // "Claude is waiting for your input" is the idle reminder after Stop; Done already covered it.
                if (e.NotificationType is "idle_prompt" or "auth_success" ||
                    e.Message?.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase) == true) break;
                Set(id, e.Cwd, ClaudeState.WaitingInput, e.Message ?? "");
                break;
            case "Stop":
                Set(id, e.Cwd, ClaudeState.Done, "");
                break;
            case "SessionEnd":
                Remove(id);
                break;
        }
    }

    private void Set(string id, string? cwd, ClaudeState state, string message)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            var activity = new ClaudeActivity(id, ProjectName(cwd));
            var timer = new DispatcherTimer();
            timer.Tick += (_, _) => Remove(id);
            session = new Session(activity, timer);
            _sessions[id] = session;
        }

        session.Activity.Set(state, message);
        session.Timer.Stop();
        session.Timer.Interval = state switch
        {
            ClaudeState.Done => DoneShowFor,
            ClaudeState.WaitingInput => WaitingExpiry,
            _ => RunningExpiry,
        };
        session.Timer.Start();
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(session.Activity.Id, session.Activity));
    }

    private void Remove(string id)
    {
        if (!_sessions.Remove(id, out var session)) return;
        session.Timer.Stop();
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(session.Activity.Id, null));
    }

    private static string ProjectName(string? cwd)
    {
        var name = Path.GetFileName(cwd?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "");
        return name.Length > 0 ? name : "Claude";
    }
}
