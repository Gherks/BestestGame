namespace BestestGame.Services;

/// <summary>
/// Keeps opponent groups in their initial standing order while voting changes scores.
/// Splitting and skipping only change presentation; they never imply a duel result.
/// </summary>
public sealed class AdaptiveDuelSession
{
    public const int InitialGroupSize = 10;
    private readonly List<Guid[]> _groups = [];

    public int RemainingCount => _groups.Sum(group => group.Length);
    public Guid[][] Snapshot() => _groups.Select(group => group.ToArray()).ToArray();

    public Guid[] Current(bool useGroups)
        => _groups.Count == 0 ? [] : useGroups ? _groups[0].ToArray() : [_groups[0][0]];

    public void Synchronize(IEnumerable<Guid> orderedPendingIds)
    {
        var pending = orderedPendingIds.Distinct().ToArray();
        var pendingSet = pending.ToHashSet();
        Restore(_groups.Select(group => group.Where(pendingSet.Contains).ToArray()).ToArray());
        var queued = _groups.SelectMany(group => group).ToHashSet();
        _groups.AddRange(pending.Where(id => !queued.Contains(id)).Chunk(InitialGroupSize));
    }

    public void Restore(IEnumerable<Guid[]> groups)
    {
        // Materialize before clearing: callers may pass the current queue itself.
        var seen = new HashSet<Guid>();
        var restored = groups.Select(group => group.Where(seen.Add).ToArray())
            .Where(group => group.Length > 0).ToArray();
        _groups.Clear();
        _groups.AddRange(restored);
    }

    public bool SplitCurrent()
    {
        var current = Current(useGroups: true);
        if (current.Length < 2)
            return false;

        var midpoint = (current.Length + 1) / 2;
        _groups.RemoveAt(0);
        _groups.Insert(0, current[midpoint..]);
        _groups.Insert(0, current[..midpoint]);
        return true;
    }

    /// <summary>Makes the group holding this duel current. The rest of the queue keeps its order.</summary>
    public bool BringToFront(Guid duelId)
    {
        var index = _groups.FindIndex(group => group.Contains(duelId));
        if (index <= 0)
            return false;

        var group = _groups[index];
        _groups.RemoveAt(index);
        _groups.Insert(0, group);
        return true;
    }

    public void Remove(IEnumerable<Guid> duelIds)
    {
        var removed = duelIds.ToHashSet();
        Restore(_groups.Select(group => group.Where(id => !removed.Contains(id)).ToArray()).ToArray());
    }

    public void SkipCurrent(bool useGroups)
    {
        var current = Current(useGroups);
        if (current.Length == 0)
            return;

        Remove(current);
        _groups.Add(current);
    }
}
