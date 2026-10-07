using BestestGame.Models;
using BestestGame.Services;

namespace BestestGame.Components;

// Display-only full-tournament standings. GOTY keeps its own head-to-head rules.
public static class TournamentStandings
{
    public sealed record Entry(Game Game, int Rank);

    public static List<Entry> Rank(IEnumerable<Game> games, int? year = null)
    {
        var ordered = games.Where(game => year is null || ReleaseRankings.MatchesYear(game, year.Value))
            .OrderByDescending(game => game.Points)
            .ThenBy(game => game.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.Id).ToList();
        var entries = new List<Entry>(ordered.Count);
        var rank = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i == 0 || ordered[i].Points != ordered[i - 1].Points) rank = i + 1;
            entries.Add(new(ordered[i], rank));
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
