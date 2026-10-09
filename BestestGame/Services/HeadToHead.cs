using BestestGame.Models;

namespace BestestGame.Services;

/// <summary>
/// Separates entries level on points by the completed matchups among themselves.
/// Shared by the full standings and the yearly rankings; nothing here changes stored results.
/// </summary>
public static class HeadToHead
{
    /// <summary>The winner of every completed matchup, whichever way round its pair is asked for.</summary>
    public sealed class Results
    {
        private readonly Dictionary<(Guid, Guid), Guid> _winners = [];

        public Results(IEnumerable<Duel>? duels)
        {
            foreach (var duel in duels ?? [])
                if (duel.IsCompleted && duel.Game1Id != duel.Game2Id &&
                    (duel.WinnerId == duel.Game1Id || duel.WinnerId == duel.Game2Id))
                    _winners[Pair(duel.Game1Id, duel.Game2Id)] = duel.WinnerId!.Value;
        }

        public Guid? Winner(Guid first, Guid second)
            => _winners.TryGetValue(Pair(first, second), out var winner) ? winner : null;

        private static (Guid, Guid) Pair(Guid first, Guid second)
            => first.CompareTo(second) < 0 ? (first, second) : (second, first);
    }

    /// <summary>
    /// Tiers of entries, best first. Entries are ordered by their wins against the others in the
    /// group. Those still level are compared again among themselves only, until nothing separates
    /// them: two entries are settled by their own matchup, and a circle of wins shares its tier.
    /// </summary>
    public static List<Guid[]> Tiers(IReadOnlyCollection<Guid> tied, Results results)
    {
        if (tied.Count < 2)
            return tied.Count == 0 ? [] : [tied.ToArray()];

        var wins = tied.ToDictionary(id => id, _ => 0);
        var entries = tied.ToArray();
        for (var first = 0; first < entries.Length; first++)
            for (var second = first + 1; second < entries.Length; second++)
                if (results.Winner(entries[first], entries[second]) is { } winner)
                    wins[winner]++;

        var levels = entries.GroupBy(id => wins[id]).OrderByDescending(level => level.Key)
            .Select(level => level.ToArray()).ToList();
        return levels.Count == 1 ? levels : levels.SelectMany(level => Tiers(level, results)).ToList();
    }
}
