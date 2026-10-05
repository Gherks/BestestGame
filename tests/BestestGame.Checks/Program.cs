using System.Text.Json;
using BestestGame.Models;
using BestestGame.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

var directory = Path.Combine(Path.GetTempPath(), $"bestestgame-checks-{Guid.NewGuid()}");
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "data.json");
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["DatabasePath"] = path }).Build();
    var service = new GameService(configuration, null!);
    var tournamentId = Guid.NewGuid();
    var oldGameId = Guid.NewGuid();
    const string oldTitle = "Assassin's Creed (1, 2, 3: Brotherhood, 4: Revelations)";
    File.WriteAllText(path, JsonSerializer.Serialize(new
    {
        CurrentTournamentId = tournamentId,
        Tournaments = new[] { new
        {
            Id = tournamentId, Name = "Legacy", Games = new[] {
                new { Id = oldGameId, Title = oldTitle, Points = 7, IncludedTitles = new[] { "Legacy underlying title" } }
            }, Duels = Array.Empty<Duel>()
        } }
    }));

    Check(JsonSerializer.Deserialize<Game>("{\"Title\":\"Old\"}")!.IncludedTitles.Count == 0, "Missing lists default to empty");
    Check(service.GetGames().Single().IncludedTitles.Single().Title == "Legacy underlying title", "Legacy string lists still load");
    Check(service.GetGames().Single().ReleaseYear is null && service.GetGames().Single().IncludedTitles.Single().ReleaseYear is null, "Legacy years default to unknown");
    var mixed = JsonSerializer.Deserialize<Game>("{\"IncludedTitles\":[\"Old\",{\"Title\":\"New\",\"ReleaseYear\":2001}]}")!;
    Check(mixed.IncludedTitles[0].ReleaseYear is null && mixed.IncludedTitles[1].ReleaseYear == 2001, "Mixed old and new included titles load");
    Check(service.ImportGames(new[] {
        new Game { Title = " Group ", ReleaseYear = 2007, IncludedTitles = [new() { Title = " Unrelated title ", ReleaseYear = 1999 }, new() { Title = "A title, with commas" }, new() { Title = "" }, new() { Title = "   " }, new() { Title = "別のゲーム", ReleaseYear = 2026 }] },
        new Game { Title = "group", IncludedTitles = [new() { Title = "Must not overwrite" }] },
        new Game { Title = oldTitle }, new Game { Title = " " }
    }) == 1, "Imports skip existing and same-batch duplicate titles");

    var games = service.GetGames();
    var group = games.Single(g => g.Title == "Group");
    Check(group.IncludedTitles.Select(t => t.Title).SequenceEqual(new[] { "Unrelated title", "A title, with commas", "別のゲーム" }), "Arbitrary titles survive persistence");
    Check(group.ReleaseYear == 2007 && group.IncludedTitles[0].ReleaseYear == 1999 && group.IncludedTitles[1].ReleaseYear is null && group.IncludedTitles[2].ReleaseYear == 2026, "Independent optional years survive persistence");
    var legacy = games.Single(g => g.Id == oldGameId);
    Check(legacy.Title == oldTitle && legacy.Points == 7, "Import preserves original titles and points");
    Check(service.TotalDuels() == 1, "Included titles do not create their own duels");

    var duel = service.GetPendingDuels().Single();
    service.RecordWinner(duel.Id, group.Id);
    var duelsBeforeEdit = JsonSerializer.Serialize(service.GetCurrentTournament()!.Duels);
    Check(service.UpdateGameDetails(group.Id, 2008, [new() { Title = "Replacement unrelated title", ReleaseYear = 2010 }]), "Can edit existing included titles");
    group = service.GetGames().Single(g => g.Id == group.Id);
    Check(group.Title == "Group" && group.Points == 1 && group.IncludedTitles.Select(t => t.Title).SequenceEqual(new[] { "Replacement unrelated title" }), "Edit preserves identity, title, and points");
    Check(group.ReleaseYear == 2008 && group.IncludedTitles.Single().ReleaseYear == 2010, "Edited years persist");
    Check(JsonSerializer.Serialize(service.GetCurrentTournament()!.Duels) == duelsBeforeEdit, "Edit preserves all duel state");
    Check(service.UpdateGameDetails(group.Id, null, []), "Can clear included titles");
    Check(service.GetGames().Single(g => g.Id == group.Id).IncludedTitles.Count == 0 && service.GetGames().Single(g => g.Id == group.Id).ReleaseYear is null, "Cleared list and year persist");
    var beforeInvalid = File.ReadAllText(path);
    foreach (var invalid in new[] { 0, -1, 10000 })
    {
        ExpectInvalid(() => service.ImportGames(new[] { new Game { Title = "Invalid", ReleaseYear = invalid } }));
        ExpectInvalid(() => service.UpdateGameDetails(group.Id, null, [new() { Title = "Invalid", ReleaseYear = invalid }]));
    }
    Check(File.ReadAllText(path) == beforeInvalid, "Invalid years do not change the database");
    Check(!service.UpdateGameDetails(Guid.NewGuid(), null, [new() { Title = "Unknown" }]), "Unknown game cannot be edited");
    Check(service.ImportGames(new[] { "Solo", "solo", " " }) == 1, "Plain bulk import still works");
    Check(service.GetGames().Single(g => g.Title == "Solo").IncludedTitles.Count == 0, "Plain titles have empty lists");
    Check(service.TotalDuels() == 3 && service.CompletedDuels() == 1, "New imports retain completed duels");
    using var saved = JsonDocument.Parse(File.ReadAllText(path));
    Check(saved.RootElement.GetProperty("Tournaments")[0].GetProperty("Games").EnumerateArray()
        .All(g => g.TryGetProperty("ReleaseYear", out _) && g.GetProperty("IncludedTitles").EnumerateArray()
            .All(t => t.ValueKind == JsonValueKind.Object && t.TryGetProperty("ReleaseYear", out _))), "Every saved game has a list");
    service.CreateTournament("Other tournament");
    Check(service.ImportGames(new[] { "Other" }) == 1, "Can import into another tournament");
    service.SelectTournament(tournamentId);
    Check(service.GetGames().Count == 3, "Imports remain scoped to the selected tournament");
    var collection = new Game
    {
        Title = "Collection", Points = 10, ReleaseYear = 2007,
        IncludedTitles = [new() { Title = "First release", ReleaseYear = 2007 },
            new() { Title = "Second release", ReleaseYear = 2010 },
            new() { Title = "Same year", ReleaseYear = 2007 }, new() { Title = "Unknown" }]
    };
    var releases = new List<Game> { collection,
        new() { Title = "Standalone", ReleaseYear = 2007, Points = 10 },
        new() { Title = "Runner-up", ReleaseYear = 2007, Points = 5 },
        new() { Title = "Undated", Points = 99 } };
    var releasesBefore = JsonSerializer.Serialize(releases);
    var rankings = ReleaseRankings.GetEntries(releases);
    Check(rankings.Count == 5 && rankings.All(e => e.Title != "Collection" && e.Title != "Undated"), "Yearly rankings include releases without duplicating collections or inventing unknown dates");
    Check(rankings.Where(e => e.GameId == collection.Id).All(e => e.Points == 10 && e.GroupTitle == "Collection"), "Included titles inherit their group's score and provenance");
    Check(rankings.Where(e => e.Year == 2007).Count(e => e.Points == 10) == 3, "Equal scores retain all tied leaders, including releases from the same group");
    Check(rankings.Where(e => e.Year == 2010).Single().Title == "Second release", "Included titles qualify independently by year");
    Check(ReleaseRankings.CountUnassigned(releases) == 2, "Unknown years counted per actual title");
    Check(ReleaseRankings.MatchesYear(collection, 2010) && !ReleaseRankings.MatchesYear(collection, 2025), "Leaderboard filter matches included-title years");
    Check(ReleaseRankings.GetEntries([]).Count == 0, "Empty tournaments have no yearly nominees");
    Check(JsonSerializer.Serialize(releases) == releasesBefore, "Yearly browsing never mutates games or scores");
    var headToHead = new Duel { Game1Id = collection.Id, Game2Id = releases[1].Id, IsCompleted = true, WinnerId = releases[1].Id };
    var decided = ReleaseRankings.GetEntries(releases, [headToHead]).Where(e => e.Year == 2007).ToList();
    Check(decided[0].Title == "Standalone" && decided[0].HeadToHeadWins == 1 && !decided[0].SharesRankWith(decided[1]), "Completed direct duel breaks an equal-points tie");
    Check(decided.Where(e => e.GameId == collection.Id).All(e => e.HeadToHeadWins == 0), "Included releases do not multiply the opponent's head-to-head wins");
    headToHead.IsCompleted = false;
    Check(ReleaseRankings.GetEntries(releases, [headToHead]).All(e => e.HeadToHeadWins == 0), "Pending duels cannot break ties even with a stale winner");
    headToHead.IsCompleted = true;
    headToHead.WinnerId = collection.Id;
    var changed = ReleaseRankings.GetEntries(releases, [headToHead]).Where(e => e.Year == 2007).ToList();
    Check(changed[0].GameId == collection.Id && changed[0].SharesRankWith(changed[1]), "Changing a duel winner updates the leader and keeps the same group's releases tied");
    Check(ReleaseRankings.GetEntries(releases, [headToHead]).Single(e => e.Year == 2010).HeadToHeadWins == 0, "Year tiebreakers exclude opponents released in other years");
    var tiedTrio = Enumerable.Range(0, 3).Select(i => new Game { Title = $"Cycle {i}", ReleaseYear = 2000, Points = 5 }).ToList();
    var cycle = Enumerable.Range(0, 3).Select(i => new Duel { Game1Id = tiedTrio[i].Id, Game2Id = tiedTrio[(i + 1) % 3].Id, WinnerId = tiedTrio[i].Id, IsCompleted = true }).ToList();
    var cycleRanks = ReleaseRankings.GetEntries(tiedTrio, cycle);
    Check(cycleRanks.All(e => e.HeadToHeadWins == 1 && e.SharesRankWith(cycleRanks[0])), "Circular head-to-head results remain tied");
    var liveContents = File.ReadAllText(path);
    var developmentConfig = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["DatabasePath"] = "development/data.json" }).Build();
    var developmentService = new GameService(developmentConfig, new CheckEnvironment(directory));
    Check(developmentService.GetTournaments().Count == 0, "A separate development database starts empty");
    developmentService.CreateTournament("Development only");
    Check(File.Exists(Path.Combine(directory, "development", "data.json")), "Relative database paths use the content root and create missing directories");
    Check(File.ReadAllText(path) == liveContents, "Development writes do not modify the live database");
    FocusedVotingChecks.Run(directory);
    Console.WriteLine("All persistence, release-year, GOTY ranking, focused voting, and database isolation checks passed.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

static void ExpectInvalid(Action action)
{
    try { action(); }
    catch (ArgumentOutOfRangeException) { return; }
    throw new InvalidOperationException("Invalid release year was accepted");
}

sealed class CheckEnvironment(string contentRoot) : IWebHostEnvironment
{
    public string ContentRootPath { get; set; } = contentRoot;
    public string WebRootPath { get; set; } = contentRoot;
    public string ApplicationName { get; set; } = "BestestGame.Checks";
    public string EnvironmentName { get; set; } = "Development";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
