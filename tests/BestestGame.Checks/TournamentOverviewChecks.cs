using System.Text.Json;
using BestestGame.Components;
using BestestGame.Models;

static class TournamentOverviewChecks
{
    public static void Run()
    {
        ExpectTask(null, false, TournamentOverview.Stage.NoTournaments, "Create a tournament", "/tournaments");
        ExpectTask(null, true, TournamentOverview.Stage.ChooseTournament, "Choose a tournament", "/tournaments");
        var tournament = new Tournament { Name = "Returning user" };
        ExpectTask(tournament, true, TournamentOverview.Stage.Setup, "Add games", "/import");
        var collection = new Game { Title = "Collection", Points = 7,
            IncludedTitles = [new() { Title = "First release", ReleaseYear = 2007 }, new() { Title = "別のゲーム" }] };
        tournament.Games.Add(collection);
        ExpectTask(tournament, true, TournamentOverview.Stage.Setup, "Add games", "/import");
        // Setup takes precedence even if a legacy file contains a stale matchup.
        tournament.Duels.Add(new Duel { Game1Id = collection.Id, Game2Id = Guid.NewGuid(), WinnerId = collection.Id });
        ExpectTask(tournament, true, TournamentOverview.Stage.Setup, "Add games", "/import");
        var other = new Game { Title = "Other", Points = 99 };
        tournament.Games.Add(other);
        var matchup = tournament.Duels.Single();
        matchup.Game2Id = other.Id;
        ExpectTask(tournament, true, TournamentOverview.Stage.Voting, "Continue voting", "/vote");
        matchup.IsCompleted = true;
        ExpectTask(tournament, true, TournamentOverview.Stage.Complete, "View rankings", "/rankings");
        tournament.Duels.Add(new Duel { Game1Id = collection.Id, Game2Id = other.Id });
        ExpectTask(tournament, true, TournamentOverview.Stage.Voting, "Continue voting", "/vote");
        tournament.Duels.Clear();
        ExpectTask(tournament, true, TournamentOverview.Stage.Complete, "View rankings", "/rankings");
        Check(tournament.Duels.Count == 0, "Browsing an existing zero-duel tournament does not generate matchups");

        var summary = TournamentOverview.GetLeaders(tournament);
        Check(summary.Games.Single().Id == other.Id && summary.TotalTied == 1,
            "Leaders use full-tournament stored points, including entries with unknown years");
        other.Points = collection.Points;
        summary = TournamentOverview.GetLeaders(tournament);
        Check(summary.Games.Select(game => game.Id).SequenceEqual(new[] { collection.Id, other.Id }) && summary.TotalTied == 2,
            "Tied leaders retain a collection as one parent participant");
        tournament.Games.AddRange(new[] { "Zulu", "alpha", "Birch" }.Select(title => new Game { Title = title, Points = 7 }));
        tournament.Games.Add(new Game { Title = "Runner-up", Points = 6, ReleaseYear = 2007 });
        var before = JsonSerializer.Serialize(tournament);
        summary = TournamentOverview.GetLeaders(tournament);
        Check(summary.Games.Select(game => game.Title).SequenceEqual(new[] { "alpha", "Birch", "Collection" }) && summary.TotalTied == 5,
            "Home bounds its summary to three leaders, orders ties consistently and counts all shared leaders");
        Check(!summary.Games.Any(game => game.Title == "Runner-up"), "A small leader summary does not include lower ranks");
        TournamentOverview.GetNextTask(tournament, true);
        Check(JsonSerializer.Serialize(tournament) == before, "Overview reads preserve IDs, scores, years, included titles, game order and duel state");
        foreach (var game in tournament.Games) game.Points = 0;
        summary = TournamentOverview.GetLeaders(tournament);
        Check(summary.Games.Count == 0 && summary.TotalTied == 0, "Unplayed entries do not claim a winning leader");
        Check(TournamentOverview.GetLeaders(new Tournament()).Games.Count == 0, "Empty tournaments have no leaders");
    }

    private static void ExpectTask(Tournament? tournament, bool hasTournaments, TournamentOverview.Stage stage, string label, string href)
    {
        var task = TournamentOverview.GetNextTask(tournament, hasTournaments);
        Check(task.Stage == stage && task.Label == label && task.Href == href, $"Expected {stage} to offer '{label}' at {href}");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
