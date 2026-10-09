using BestestGame.Models;
using BestestGame.Services;

namespace BestestGame.Components;

// Display-only full-tournament standings.
public static class TournamentStandings
{
    public sealed record Entry(Game Game, int Rank);

    /// <summary>
    /// Entries level on points are ordered by the completed matchups among them. Those that
    /// stay level share a rank, and are listed alphabetically only for presentation.
    /// </summary>
    public static List<Entry> Rank(IEnumerable<Game> games, IEnumerable<Duel>? duels = null, int? year = null)
    {
        var results = new HeadToHead.Results(duels);
        var entries = new List<Entry>();
        foreach (var level in games.Where(game => year is null || ReleaseRankings.MatchesYear(game, year.Value))
            .GroupBy(game => game.Points).OrderByDescending(level => level.Key))
        {
            var byId = level.ToDictionary(game => game.Id);
            foreach (var tier in HeadToHead.Tiers(byId.Keys, results))
            {
                var rank = entries.Count + 1;
                entries.AddRange(tier.Select(id => byId[id])
                    .OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase).ThenBy(game => game.Id)
                    .Select(game => new Entry(game, rank)));
            }
        }
        return entries;
    }

    // Filter after ranking so hiding an entry never promotes the remaining rows.
    public static List<Entry> Search(IEnumerable<Entry> entries, string? query)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term)) return entries.ToList();
        return entries.Where(entry => entry.Game.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (entry.Game.IncludedTitles?.Any(title => title.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) ?? false))
            .ToList();
    }
}
