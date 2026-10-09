using BestestGame.Models;

namespace BestestGame.Services;

public static class ReleaseRankings
{
    public sealed record Entry(Guid GameId, string Title, int Year, int Points, string? GroupTitle)
    {
        public int HeadToHeadWins { get; init; }
        /// <summary>Place among the releases level on points this year, best first; see <see cref="HeadToHead.Tiers"/>.</summary>
        public int Tier { get; init; }
        public bool SharesRankWith(Entry other)
            => Points == other.Points && Tier == other.Tier;
    }

    /// <summary>A nominee with matchups left that can still reach the leader's points.</summary>
    public sealed record Contender(Guid GameId, string Title, int Points, int Pending)
    {
        public int Reach => Points + Pending;
    }

    /// <summary>Who leads a year, who can still catch them, and whether first place can still change hands.</summary>
    public sealed record Lead(IReadOnlyList<Entry> Leaders, IReadOnlyList<Contender> Contenders, bool Settled);

    /// <summary>
    /// Reads the lead from one year's nominees in ranked order. Points only ever rise, so nothing is estimated:
    /// first place is settled once no other nominee can reach the leader's points, and leaders sharing it have
    /// no matchups left that could part them.
    /// </summary>
    public static Lead GetLead(IReadOnlyList<Entry> nominees, IReadOnlyDictionary<Guid, int> pending)
    {
        if (nominees.Count == 0)
            return new([], [], true);

        var leaders = nominees.Where(entry => entry.SharesRankWith(nominees[0])).ToList();
        var leading = leaders.Select(entry => entry.GameId).ToHashSet();
        var contenders = nominees.Where(entry => !leading.Contains(entry.GameId)).GroupBy(entry => entry.GameId)
            .Select(releases => new Contender(releases.Key, releases.First().GroupTitle ?? releases.First().Title,
                releases.First().Points, pending.GetValueOrDefault(releases.Key)))
            .Where(contender => contender.Pending > 0 && contender.Reach >= nominees[0].Points)
            .OrderByDescending(contender => contender.Reach).ThenByDescending(contender => contender.Points).ToList();
        var sharedOpen = leading.Count > 1 && leading.Any(id => pending.GetValueOrDefault(id) > 0);
        return new(leaders, contenders, contenders.Count == 0 && !sharedOpen);
    }

    // Collections contribute their individual releases, never an extra collection nominee.
    public static List<Entry> GetEntries(IEnumerable<Game> games, IEnumerable<Duel>? duels = null)
    {
        var completedDuels = (duels ?? []).Where(d => d.IsCompleted &&
            (d.WinnerId == d.Game1Id || d.WinnerId == d.Game2Id)).ToList();
        var results = new HeadToHead.Results(completedDuels);
        var releases = games.SelectMany(game => game.IncludedTitles is { Count: > 0 }
                ? game.IncludedTitles.Where(title => title.ReleaseYear.HasValue)
                    .Select(title => new Entry(game.Id, title.Title, title.ReleaseYear!.Value, game.Points, game.Title))
                : game.ReleaseYear is { } year
                    ? new[] { new Entry(game.Id, game.Title, year, game.Points, null) }
                    : Enumerable.Empty<Entry>()).ToList();

        return releases.GroupBy(entry => (entry.Year, entry.Points))
            .SelectMany(tied =>
            {
                // Count each parent participant once, even if several of its releases
                // qualify this year. A shared entry cannot play against itself.
                var participants = tied.Select(entry => entry.GameId).ToHashSet();
                var wins = completedDuels
                    .Where(d => participants.Contains(d.Game1Id) && participants.Contains(d.Game2Id) && d.Game1Id != d.Game2Id)
                    .GroupBy(d => d.WinnerId!.Value).ToDictionary(group => group.Key, group => group.Count());
                var tiers = HeadToHead.Tiers(participants, results);
                var tierOf = tiers.SelectMany((tier, index) => tier.Select(id => (id, index))).ToDictionary(item => item.id, item => item.index);
                return tied.Select(entry => entry with { HeadToHeadWins = wins.GetValueOrDefault(entry.GameId), Tier = tierOf[entry.GameId] });
            })
            .OrderByDescending(entry => entry.Points)
            .ThenBy(entry => entry.Tier)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int CountUnassigned(IEnumerable<Game> games)
        => games.Sum(game => game.IncludedTitles is { Count: > 0 }
            ? game.IncludedTitles.Count(title => title.ReleaseYear is null)
            : game.ReleaseYear is null ? 1 : 0);

    public static bool MatchesYear(Game game, int year)
        => game.ReleaseYear == year || (game.IncludedTitles?.Any(title => title.ReleaseYear == year) ?? false);
}
