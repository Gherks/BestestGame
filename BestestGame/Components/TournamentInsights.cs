using BestestGame.Models;

namespace BestestGame.Components;

// Display-only readings of the saved results. Nothing here changes points, duels or their order.
public static class TournamentInsights
{
    /// <summary>A result that went against the standings: the winner has fewer points than the loser.</summary>
    public sealed record Upset(Game Winner, Game Loser, int Gap);

    /// <summary>First beat Second, Second beat Third and Third beat First. First stands highest of the three.</summary>
    public sealed record Loop(Game First, Game Second, Game Third, int Spread);

    /// <summary>Neighbours in the standings with a stored matchup. Winner is null until it has a result.</summary>
    public sealed record Rivalry(Game Higher, Game Lower, int Gap, Game? Winner);

    /// <summary>The counts cover every upset and loop; the lists hold only the most notable.</summary>
    public sealed record Summary(IReadOnlyList<Upset> Upsets, int UpsetCount,
        IReadOnlyList<Loop> Loops, int LoopCount, IReadOnlyList<Rivalry> Rivalries);

    public static Summary Build(IEnumerable<Game> games, IEnumerable<Duel> duels, int limit = 5)
    {
        // Positions follow the standings, so a lower position always means at least as many points.
        var standings = TournamentStandings.Rank(games).Select(entry => entry.Game).ToArray();
        var position = standings.Select((game, index) => (game.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        var count = standings.Length;
        var paired = new bool[count, count];
        var beats = new bool[count, count];
        foreach (var duel in duels)
        {
            if (!position.TryGetValue(duel.Game1Id, out var first) || !position.TryGetValue(duel.Game2Id, out var second) || first == second)
                continue;
            paired[first, second] = paired[second, first] = true;
            if (duel.IsCompleted && (duel.WinnerId == duel.Game1Id || duel.WinnerId == duel.Game2Id))
                beats[duel.WinnerId == duel.Game1Id ? first : second, duel.WinnerId == duel.Game1Id ? second : first] = true;
        }

        var upsets = new List<Upset>();
        for (var loser = 0; loser < count; loser++)
            for (var winner = loser + 1; winner < count; winner++)
                if (beats[winner, loser] && standings[winner].Points < standings[loser].Points)
                    upsets.Add(new(standings[winner], standings[loser], standings[loser].Points - standings[winner].Points));

        // Every loop of three is found once, starting from its highest entry, in either direction.
        var loops = new List<Loop>();
        for (var top = 0; top < count; top++)
            for (var middle = top + 1; middle < count; middle++)
                for (var bottom = middle + 1; bottom < count; bottom++)
                {
                    var spread = standings[top].Points - standings[bottom].Points;
                    if (beats[top, middle] && beats[middle, bottom] && beats[bottom, top])
                        loops.Add(new(standings[top], standings[middle], standings[bottom], spread));
                    else if (beats[top, bottom] && beats[bottom, middle] && beats[middle, top])
                        loops.Add(new(standings[top], standings[bottom], standings[middle], spread));
                }

        var rivalries = new List<Rivalry>();
        for (var higher = 0; higher + 1 < count; higher++)
        {
            var lower = higher + 1;
            if (!paired[higher, lower]) continue;
            rivalries.Add(new(standings[higher], standings[lower], standings[higher].Points - standings[lower].Points,
                beats[higher, lower] ? standings[higher] : beats[lower, higher] ? standings[lower] : null));
        }

        // Stable sorts keep standings order among equals: the higher up the table, the earlier.
        return new(
            upsets.OrderByDescending(upset => upset.Gap).Take(limit).ToList(), upsets.Count,
            loops.OrderByDescending(loop => loop.Spread).Take(limit).ToList(), loops.Count,
            rivalries.OrderBy(rivalry => rivalry.Gap).ThenBy(rivalry => rivalry.Winner is not null).Take(limit).ToList());
    }
}
