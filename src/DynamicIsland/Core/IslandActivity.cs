using CommunityToolkit.Mvvm.ComponentModel;

namespace DynamicIsland.Core;

/// <summary>Something the island can display. Concrete types are matched to views via DataTemplates.</summary>
public abstract class IslandActivity : ObservableObject
{
    public abstract string Id { get; }
    public abstract ActivityKind Kind { get; }

    /// <summary>Higher wins. Ties go to the most recently updated activity.</summary>
    public virtual int Priority => 0;

    /// <summary>
    /// When outranked in its own slot, may move to a free (or lower-priority) small island instead of staying hidden.
    /// </summary>
    public virtual bool CanOverflow => false;

    public DateTime Updated { get; private set; } = DateTime.UtcNow;

    protected void Touch() => Updated = DateTime.UtcNow;
}
