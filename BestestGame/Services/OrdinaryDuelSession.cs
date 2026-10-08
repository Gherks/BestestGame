using BestestGame.Models;

namespace BestestGame.Services;

// Presentation and undo state for one page; all results use the guarded service API.
// The page keeps the undo history in the browser tab so it survives a reload.
public sealed class OrdinaryDuelSession
{
    public enum VoteOutcome { Ignored, Changed, Saved }
    public enum UndoOutcome { Ignored, Changed, Undone }

    public Guid Presentation { get; private set; } = Guid.NewGuid();
    public Duel? Current { get; private set; }
    public const int UndoCapacity = 50;
    private readonly List<GameService.DuelResult> _history = [];
    /// <summary>Saved votes that can still be undone, oldest first.</summary>
    public IReadOnlyList<GameService.DuelResult> History => _history;
    public GameService.DuelResult? LastVote => _history.Count > 0 ? _history[^1] : null;
    public bool CanUndo => _history.Count > 0;
    private readonly Random _random;

    public OrdinaryDuelSession(Random? random = null) => _random = random ?? new();

    public void Reset()
    {
        Current = null;
        _history.Clear();
        Presentation = Guid.NewGuid();
    }

    /// <param name="points">Entry points to ask about the closest matchups first; null keeps the order random.</param>
    public void Select(IEnumerable<Duel> pending, IReadOnlySet<Guid> excluded,
        Guid? preferred = null, Guid? avoid = null, IReadOnlyDictionary<Guid, int>? points = null)
    {
        var available = pending.Where(duel => !duel.IsCompleted &&
            !excluded.Contains(duel.Game1Id) && !excluded.Contains(duel.Game2Id)).ToList();
        if (avoid is not null && available.Count > 1)
            available.RemoveAll(duel => duel.Id == avoid);
        Current = available.FirstOrDefault(duel => duel.Id == preferred) ?? Pick(available, points);
        Presentation = Guid.NewGuid();
    }

    // Entries level on points come before lopsided pairs. Equally close matchups stay random.
    private Duel? Pick(List<Duel> available, IReadOnlyDictionary<Guid, int>? points)
    {
        if (available.Count == 0) return null;
        if (points is not null)
        {
            int Gap(Duel duel) => Math.Abs(points.GetValueOrDefault(duel.Game1Id) - points.GetValueOrDefault(duel.Game2Id));
            var closest = available.Min(Gap);
            available = available.Where(duel => Gap(duel) == closest).ToList();
        }
        return available[_random.Next(available.Count)];
    }

    public VoteOutcome Vote(GameService service, Guid tournamentId, Guid presentation, Guid duelId, Guid winnerId)
    {
        if (presentation != Presentation || Current?.Id != duelId ||
            (Current.Game1Id != winnerId && Current.Game2Id != winnerId)) return VoteOutcome.Ignored;
        var result = new GameService.DuelResult(duelId, winnerId);
        if (service.RecordWinners(tournamentId, [result]).Count == 0) return VoteOutcome.Changed;
        _history.Add(result);
        if (_history.Count > UndoCapacity) _history.RemoveAt(0);
        return VoteOutcome.Saved;
    }

    public UndoOutcome Undo(GameService service, Guid tournamentId, Guid presentation)
    {
        if (presentation != Presentation || LastVote is not { } result) return UndoOutcome.Ignored;
        // A vote that has since been corrected can never be undone, so it leaves the history too.
        _history.RemoveAt(_history.Count - 1);
        return service.UndoWinners(tournamentId, [result]) ? UndoOutcome.Undone : UndoOutcome.Changed;
    }

    /// <summary>Takes over a history kept elsewhere, such as the one saved in the browser tab.</summary>
    public void RestoreHistory(IEnumerable<GameService.DuelResult?> saved, IEnumerable<Duel> duels)
    {
        var restored = StillSaved(saved, duels);
        _history.Clear();
        _history.AddRange(restored);
    }

    /// <summary>
    /// Keeps the votes that are still stored exactly as they were cast, newest last and at most
    /// <see cref="UndoCapacity"/> of them. Corrected, undone and unknown results are dropped.
    /// </summary>
    public static List<GameService.DuelResult> StillSaved(IEnumerable<GameService.DuelResult?> saved, IEnumerable<Duel> duels)
    {
        var stored = duels.Where(duel => duel.IsCompleted).ToDictionary(duel => duel.Id, duel => duel.WinnerId);
        var seen = new HashSet<Guid>();
        return saved.Reverse()
            .Where(result => result is not null && stored.GetValueOrDefault(result.DuelId) == result.WinnerId && seen.Add(result.DuelId))
            .Select(result => result!).Take(UndoCapacity).Reverse().ToList();
    }
}
