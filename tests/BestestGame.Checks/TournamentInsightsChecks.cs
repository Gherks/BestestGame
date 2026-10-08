using System.Text.Json;
using BestestGame.Components;
using BestestGame.Models;

static class TournamentInsightsChecks
{
    public static void Run()
    {
        // Ash, Birch and Cedar beat each other in a circle; Dune loses to the first two and beats Cedar.
        var ash = new Game { Title = "Ash" };
        var birch = new Game { Title = "Birch" };
        var cedar = new Game { Title = "Cedar" };
        var dune = new Game { Title = "Dune" };
        var elm = new Game { Title = "Elm" };
        List<Game> games = [dune, cedar, birch, ash];
        List<Duel> duels = [];
        void Result(Game winner, Game loser)
        {
            duels.Add(new() { Game1Id = loser.Id, Game2Id = winner.Id, IsCompleted = true, WinnerId = winner.Id });
            winner.Points++;
        }
        Result(ash, birch);
        Result(birch, cedar);
        Result(cedar, ash);
        Result(ash, dune);
        Result(birch, dune);
        Result(dune, cedar);
        var before = JsonSerializer.Serialize((games, duels));
        var insights = TournamentInsights.Build(games, duels);
        Check(JsonSerializer.Serialize((games, duels)) == before, "Insights never change entries, points or results");
        Check(insights.UpsetCount == 1 && insights.Upsets.Single() is { Gap: 1 } upset && upset.Winner == cedar && upset.Loser == ash,
            "An upset is a win by the entry with fewer points; level entries cannot upset each other");
        Check(insights.LoopCount == 2 && insights.Loops.All(loop => loop.First == ash && loop.Spread == 1) &&
            insights.Loops.Any(loop => loop.Second == birch && loop.Third == cedar) &&
            insights.Loops.Any(loop => loop.Second == dune && loop.Third == cedar),
            "Each loop of three is found once, in winning order from its highest entry");
        Check(insights.Rivalries.Select(rivalry => (rivalry.Higher, rivalry.Lower, rivalry.Gap, rivalry.Winner))
            .SequenceEqual([(ash, birch, 0, ash), (cedar, dune, 0, dune), (birch, cedar, 1, birch)]),
            "Rivalries are neighbours in the standings, closest first, each with the winner of their own matchup");

        // A newcomer level with the last two entries: one stored matchup is pending, one was never stored.
        games.Add(elm);
        elm.Points = 1;
        duels.Add(new() { Game1Id = dune.Id, Game2Id = elm.Id });
        var withPending = TournamentInsights.Build(games, duels);
        Check(withPending.Rivalries[0] is { Winner: null } pending && pending.Higher == dune && pending.Lower == elm,
            "Among equally close rivalries, one still to be played comes first");
        Check(withPending.UpsetCount == 1 && withPending.LoopCount == 2, "A pending matchup is neither an upset nor part of a loop");
        duels.Add(new() { Game1Id = elm.Id, Game2Id = Guid.NewGuid(), IsCompleted = true, WinnerId = elm.Id });
        duels.Add(new() { Game1Id = ash.Id, Game2Id = elm.Id, IsCompleted = true, WinnerId = Guid.NewGuid() });
        var withStrays = TournamentInsights.Build(games, duels);
        Check(withStrays.UpsetCount == 1 && withStrays.LoopCount == 2 && withStrays.Rivalries.Count == withPending.Rivalries.Count,
            "Results naming an unknown entry or winner are ignored");

        Check(TournamentInsights.Build(games, duels, limit: 1) is { Upsets.Count: 1, Loops.Count: 1, Rivalries.Count: 1, LoopCount: 2 },
            "The lists are limited while the counts still cover everything");
        Check(TournamentInsights.Build([], []) is { UpsetCount: 0, LoopCount: 0, Rivalries.Count: 0 } &&
            TournamentInsights.Build([ash], duels).Rivalries.Count == 0, "Empty and single-entry tournaments have no insights");

        // A favourite beaten by the bottom entry is the biggest upset and the widest loop.
        var top = new Game { Title = "Top", Points = 40 };
        var mid = new Game { Title = "Mid", Points = 20 };
        var low = new Game { Title = "Low", Points = 3 };
        var other = new Game { Title = "Other", Points = 19 };
        List<Duel> ranked =
        [
            new() { Game1Id = top.Id, Game2Id = mid.Id, IsCompleted = true, WinnerId = top.Id },
            new() { Game1Id = mid.Id, Game2Id = low.Id, IsCompleted = true, WinnerId = mid.Id },
            new() { Game1Id = low.Id, Game2Id = top.Id, IsCompleted = true, WinnerId = low.Id },
            new() { Game1Id = other.Id, Game2Id = mid.Id, IsCompleted = true, WinnerId = other.Id },
        ];
        var notable = TournamentInsights.Build([low, other, mid, top], ranked);
        Check(notable.Upsets.Select(entry => (entry.Winner, entry.Gap)).SequenceEqual([(low, 37), (other, 1)]),
            "Upsets are listed by how far behind the winner stands");
        Check(notable.Loops.Single() is { Spread: 37 } loop && loop.First == top && loop.Second == mid && loop.Third == low,
            "A loop reports the points between its highest and lowest entry");
        Console.WriteLine("10 tournament insight checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
