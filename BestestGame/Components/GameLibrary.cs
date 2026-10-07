using BestestGame.Models;

namespace BestestGame.Components;

// Presentation-only library reads. Included releases remain parent participants.
public static class GameLibrary
{
    public static List<Game> Search(IEnumerable<Game> games, string? query)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term)) return games.ToList();
        return games.Where(game => game.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (game.IncludedTitles?.Any(title => title.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) ?? false)).ToList();
    }

    public static Dictionary<Guid, int> PendingMatchups(Tournament tournament)
    {
        var counts = tournament.Games.ToDictionary(game => game.Id, _ => 0);
        foreach (var duel in tournament.Duels.Where(duel => !duel.IsCompleted))
        {
            if (counts.ContainsKey(duel.Game1Id)) counts[duel.Game1Id]++;
            if (counts.ContainsKey(duel.Game2Id)) counts[duel.Game2Id]++;
        }
        return counts;
    }
}
