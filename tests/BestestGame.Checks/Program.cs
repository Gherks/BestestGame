using System.Text.Json;
using BestestGame.Models;
using BestestGame.Services;
using Microsoft.Extensions.Configuration;

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
                new { Id = oldGameId, Title = oldTitle, Points = 7 }
            }, Duels = Array.Empty<Duel>()
        } }
    }));

    Check(service.GetGames().Single().IncludedTitles.Count == 0, "Legacy games default to an empty list");
    Check(service.ImportGames(new[] {
        new Game { Title = " Group ", IncludedTitles = [" Unrelated title ", "A title, with commas", "", "   ", "別のゲーム"] },
        new Game { Title = "group", IncludedTitles = ["Must not overwrite"] },
        new Game { Title = oldTitle }, new Game { Title = " " }
    }) == 1, "Imports skip existing and same-batch duplicate titles");

    var games = service.GetGames();
    var group = games.Single(g => g.Title == "Group");
    Check(group.IncludedTitles.SequenceEqual(new[] { "Unrelated title", "A title, with commas", "別のゲーム" }), "Arbitrary titles survive persistence");
    var legacy = games.Single(g => g.Id == oldGameId);
    Check(legacy.Title == oldTitle && legacy.Points == 7, "Import preserves original titles and points");
    Check(service.TotalDuels() == 1, "Included titles do not create their own duels");

    var duel = service.GetPendingDuels().Single();
    service.RecordWinner(duel.Id, group.Id);
    var duelsBeforeEdit = JsonSerializer.Serialize(service.GetCurrentTournament()!.Duels);
    Check(service.UpdateIncludedTitles(group.Id, ["Replacement unrelated title"]), "Can edit existing included titles");
    group = service.GetGames().Single(g => g.Id == group.Id);
    Check(group.Title == "Group" && group.Points == 1 && group.IncludedTitles.SequenceEqual(new[] { "Replacement unrelated title" }), "Edit preserves identity, title, and points");
    Check(JsonSerializer.Serialize(service.GetCurrentTournament()!.Duels) == duelsBeforeEdit, "Edit preserves all duel state");
    Check(service.UpdateIncludedTitles(group.Id, []), "Can clear included titles");
    Check(service.GetGames().Single(g => g.Id == group.Id).IncludedTitles.Count == 0, "Cleared list persists");
    Check(!service.UpdateIncludedTitles(Guid.NewGuid(), ["Unknown"]), "Unknown game cannot be edited");
    Check(service.ImportGames(new[] { "Solo", "solo", " " }) == 1, "Plain bulk import still works");
    Check(service.GetGames().Single(g => g.Title == "Solo").IncludedTitles.Count == 0, "Plain titles have empty lists");
    Check(service.TotalDuels() == 3 && service.CompletedDuels() == 1, "New imports retain completed duels");
    using var saved = JsonDocument.Parse(File.ReadAllText(path));
    Check(saved.RootElement.GetProperty("Tournaments")[0].GetProperty("Games").EnumerateArray()
        .All(g => g.GetProperty("IncludedTitles").ValueKind == JsonValueKind.Array), "Every saved game has a list");
    service.CreateTournament("Other tournament");
    Check(service.ImportGames(new[] { "Other" }) == 1, "Can import into another tournament");
    service.SelectTournament(tournamentId);
    Check(service.GetGames().Count == 3, "Imports remain scoped to the selected tournament");
    Console.WriteLine("All included-title checks passed.");
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
