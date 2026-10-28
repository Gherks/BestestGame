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

    /// <summary>The highest and lowest rank an entry can still finish on. Settled once they are the same.</summary>
    public readonly record struct Outlook(int Best, int Worst)
    {
        public bool Settled => Best == Worst;
    }

    /// <summary>
    /// Where each ranked entry can still finish once every matchup is played. Points only ever rise, so an entry
    /// ends somewhere between its points now and those plus its pending matchups. Nothing is estimated: an entry
    /// finishes behind at least everyone already past the most it can reach, and behind at most everyone who can
    /// reach its points now. A finished entry that no unfinished entry can draw level with keeps the rank it
    /// has, since everything that orders it among the entries level with it has been played.
    /// </summary>
    /// <param name="ranked">Standings from <see cref="Rank"/>, for the full tournament or one year.</param>
    /// <param name="pending">Each entry's matchups still to play in the full tournament.</param>
    public static Dictionary<Guid, Outlook> Outlooks(IReadOnlyList<Entry> ranked, IReadOnlyDictionary<Guid, int> pending)
    {
        var ranges = ranked.Select(entry => (entry.Game.Id, entry.Rank, Low: entry.Game.Points,
            High: entry.Game.Points + pending.GetValueOrDefault(entry.Game.Id))).ToList();
        var outlooks = new Dictionary<Guid, Outlook>();
        foreach (var entry in ranges)
        {
            var others = ranges.Where(other => other.Id != entry.Id).ToList();
            var finished = entry.High == entry.Low;
            if (finished && !others.Any(other => other.High > other.Low && other.Low <= entry.Low && entry.Low <= other.High))
            {
                outlooks[entry.Id] = new(entry.Rank, entry.Rank);
                continue;
            }
            outlooks[entry.Id] = new(1 + others.Count(other => other.Low > entry.High), 1 + others.Count(other => other.High >= entry.Low));
        }
        return outlooks;
    }

    /// <summary>1st, 2nd, 3rd, 4th … 11th, 12th, 13th … 21st.</summary>
    public static string Ordinal(int number) => number + ((number % 100) is 11 or 12 or 13 ? "th" : (number % 10) switch
    {
        1 => "st", 2 => "nd", 3 => "rd", _ => "th"
    });

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
