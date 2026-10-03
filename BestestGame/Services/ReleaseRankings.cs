using BestestGame.Models;

namespace BestestGame.Services;

public static class ReleaseRankings
{
    public sealed record Entry(Guid GameId, string Title, int Year, int Points, string? GroupTitle)
    {
        public int HeadToHeadWins { get; init; }
        public bool SharesRankWith(Entry other)
            => Points == other.Points && HeadToHeadWins == other.HeadToHeadWins;
    }

    // Collections contribute their individual releases, never an extra collection nominee.
    public static List<Entry> GetEntries(IEnumerable<Game> games, IEnumerable<Duel>? duels = null)
    {
        var completedDuels = (duels ?? []).Where(d => d.IsCompleted &&
            (d.WinnerId == d.Game1Id || d.WinnerId == d.Game2Id)).ToList();
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
                return tied.Select(entry => entry with { HeadToHeadWins = wins.GetValueOrDefault(entry.GameId) });
            })
            .OrderByDescending(entry => entry.Points)
            .ThenByDescending(entry => entry.HeadToHeadWins)
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
