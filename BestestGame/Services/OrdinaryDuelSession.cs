using BestestGame.Models;

namespace BestestGame.Services;

// Visit-only presentation and undo state; all results use the guarded service API.
public sealed class OrdinaryDuelSession
{
    public enum VoteOutcome { Ignored, Changed, Saved }
    public enum UndoOutcome { Ignored, Changed, Undone }

    public Guid Presentation { get; private set; } = Guid.NewGuid();
    public Duel? Current { get; private set; }
    public GameService.DuelResult? LastVote { get; private set; }
    public bool CanUndo => LastVote is not null;
    private readonly Random _random;

    public OrdinaryDuelSession(Random? random = null) => _random = random ?? new();

    public void Reset()
    {
        Current = null;
        LastVote = null;
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
        LastVote = result;
        return VoteOutcome.Saved;
    }

    public UndoOutcome Undo(GameService service, Guid tournamentId, Guid presentation)
    {
        if (presentation != Presentation || LastVote is not { } result) return UndoOutcome.Ignored;
        LastVote = null;
        return service.UndoWinners(tournamentId, [result]) ? UndoOutcome.Undone : UndoOutcome.Changed;
    }
}
