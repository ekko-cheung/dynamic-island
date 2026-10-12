namespace DynamicIsland.Core;

/// <summary>Aggregates providers and picks the single activity the island should show.</summary>
public sealed class ActivityHub
{
    private readonly Dictionary<string, IslandActivity> _activities = [];

    public IslandActivity? Current { get; private set; }
    public event EventHandler? CurrentChanged;

    public async Task AddProviderAsync(IActivityProvider provider)
    {
        provider.ActivityChanged += OnActivityChanged;
        await provider.StartAsync();
    }

    /// <summary>Must be called on the UI thread.</summary>
    public void Apply(string id, IslandActivity? activity)
    {
        if (activity is null) _activities.Remove(id);
        else _activities[id] = activity;

        var best = _activities.Values
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.Updated)
            .FirstOrDefault();

        if (!ReferenceEquals(best, Current))
        {
            Current = best;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnActivityChanged(object? sender, ActivityChangedEventArgs e) => Apply(e.Id, e.Activity);
}
