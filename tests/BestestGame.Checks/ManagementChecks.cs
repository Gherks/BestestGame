using System.Text.Json;
using BestestGame.Models;
using BestestGame.Services;
using Microsoft.Extensions.Configuration;

// Renaming entries and tournaments, and deleting a tournament.
static class ManagementChecks
{
    public static void Run(string directory)
    {
        var path = Path.Combine(directory, "management.json");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DatabasePath"] = path }).Build();
        var service = new GameService(config, null!);
        var kept = service.CreateTournament("Kept");
        service.ImportGames(new[] { "Keeper" });
        var doomed = service.CreateTournament("Doomed");
        service.ImportGames(new[] { "Typo", "Other", "Third" });
        var typo = service.GetGames().Single(game => game.Title == "Typo");
        var other = service.GetGames().Single(game => game.Title == "Other");
        var duel = service.GetPendingDuels().First(pending => pending.Game1Id == typo.Id || pending.Game2Id == typo.Id);
        service.RecordWinner(duel.Id, typo.Id);
        var duelsBefore = JsonSerializer.Serialize(service.GetCurrentTournament()!.Duels);

        Check(service.RenameGame(typo.Id, "  Fixed title ") && service.GetGames().Single(game => game.Id == typo.Id) is { Title: "Fixed title", Points: 1 } &&
            JsonSerializer.Serialize(service.GetCurrentTournament()!.Duels) == duelsBefore, "Renaming an entry trims the title and keeps its ID, points and results");
        var saved = File.ReadAllText(path);
        Check(!service.RenameGame(typo.Id, "oTHER") && !service.RenameGame(typo.Id, "  ") && !service.RenameGame(Guid.NewGuid(), "New") &&
            File.ReadAllText(path) == saved, "Duplicate, blank and unknown renames change nothing");
        Check(service.RenameGame(typo.Id, "Fixed title") && service.RenameGame(other.Id, "OTHER") && service.GetGames().Single(game => game.Id == other.Id).Title == "OTHER",
            "An entry can keep its title or change only its capitals");
        service.SelectTournament(kept.Id);
        Check(!service.RenameGame(typo.Id, "Elsewhere"), "Entries of another tournament cannot be renamed");

        Check(service.RenameTournament(doomed.Id, " Renamed ") && service.GetTournaments().Single(t => t.Id == doomed.Id) is { Name: "Renamed", Games.Count: 3 } &&
            service.GetCurrentTournament()!.Id == kept.Id, "Renaming a tournament trims the name and keeps its games and the selection");
        Check(!service.RenameTournament(doomed.Id, " ") && !service.RenameTournament(Guid.NewGuid(), "Unknown"), "Blank names and unknown tournaments are refused");

        Directory.CreateDirectory(service.CoversDirectory);
        var picture = Path.Combine(service.CoversDirectory, "doomed.jpg");
        var keptPicture = Path.Combine(service.CoversDirectory, "kept.jpg");
        File.WriteAllText(picture, "picture");
        File.WriteAllText(keptPicture, "picture");
        service.SetCover(service.GetGames().Single().Id, "kept.jpg");
        service.SelectTournament(doomed.Id);
        service.SetCover(typo.Id, "doomed.jpg");
        service.SelectTournament(kept.Id);
        Check(service.DeleteTournament(doomed.Id) && service.GetTournaments().Single().Id == kept.Id && service.GetCurrentTournament()!.Id == kept.Id &&
            service.GetGames().Single().Title == "Keeper", "Deleting another tournament leaves the selected one and its games alone");
        Check(!File.Exists(picture) && File.Exists(keptPicture), "Only the deleted tournament's cover pictures are removed");
        Check(!service.DeleteTournament(doomed.Id), "A deleted tournament cannot be deleted again");
        Check(service.DeleteTournament(kept.Id) && service.GetTournaments().Count == 0 && service.GetCurrentTournament() is null && service.GetGames().Count == 0,
            "Deleting the selected tournament leaves none selected");
        Console.WriteLine("10 management checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
