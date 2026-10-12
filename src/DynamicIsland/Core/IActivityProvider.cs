namespace DynamicIsland.Core;

public sealed class ActivityChangedEventArgs(string id, IslandActivity? activity) : EventArgs
{
    public string Id { get; } = id;

    /// <summary>The activity to show/update, or null to remove the activity with <see cref="Id"/>.</summary>
    public IslandActivity? Activity { get; } = activity;
}

public interface IActivityProvider
{
    event EventHandler<ActivityChangedEventArgs> ActivityChanged;
    Task StartAsync();
}
