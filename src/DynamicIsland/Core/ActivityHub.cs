namespace DynamicIsland.Core;

/// <summary>Where an activity is shown: the main island or one of the small islands beside it.</summary>
public enum IslandSlot { Main, Left, Right }

/// <summary>Aggregates providers and picks the single activity each slot should show.</summary>
public sealed class ActivityHub(Func<IslandActivity, IslandSlot> route)
{
    private readonly Dictionary<string, IslandActivity> _activities = [];
    private readonly Dictionary<IslandSlot, IslandActivity?> _current = [];

    public IslandActivity? Get(IslandSlot slot) => _current.GetValueOrDefault(slot);
    public event EventHandler<IslandSlot>? SlotChanged;

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
        Refresh();
    }

    /// <summary>Recomputes every slot, e.g. after the user changes where an activity kind is shown.</summary>
    public void Refresh()
    {
        var routed = _activities.Values.ToLookup(route);
        var winners = Enum.GetValues<IslandSlot>().ToDictionary(s => s, s => Best(routed[s]));

        // An outranked activity that allows it moves to a small island that is free or holds something less important.
        var losers = _activities.Values
            .Where(a => a.CanOverflow && !winners.ContainsValue(a))
            .OrderByDescending(a => a.Priority).ThenByDescending(a => a.Updated);
        foreach (var loser in losers)
        {
            var own = route(loser);
            foreach (var slot in OverflowSlots.Where(s => s != own))
            {
                if (winners[slot] is { } holder && holder.Priority >= loser.Priority) continue;
                winners[slot] = loser;
                break;
            }
        }

        foreach (var (slot, best) in winners)
        {
            if (ReferenceEquals(best, Get(slot))) continue;
            _current[slot] = best;
            SlotChanged?.Invoke(this, slot);
        }
    }

    private static readonly IslandSlot[] OverflowSlots = [IslandSlot.Left, IslandSlot.Right];

    private static IslandActivity? Best(IEnumerable<IslandActivity> candidates) => candidates
        .OrderByDescending(a => a.Priority)
        .ThenByDescending(a => a.Updated)
        .FirstOrDefault();

    private void OnActivityChanged(object? sender, ActivityChangedEventArgs e) => Apply(e.Id, e.Activity);
}
