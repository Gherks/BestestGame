using System.Text.Json;
using System.Text.Json.Nodes;
using BestestGame.Models;
using BestestGame.Services;
using Microsoft.Data.Sqlite;

// Moving the earlier JSON file into SQLite, and what the database itself refuses to hold.
static class MigrationChecks
{
    public static void Run(string directory)
    {
        directory = Path.Combine(directory, "migration");
        Directory.CreateDirectory(directory);

        var games = new List<Game>
        {
            new() { Title = "Tied first", Points = 1, ReleaseYear = 1998, CoverImage = "first.jpg" },
            new() { Title = "Tied second", Points = 1, IncludedTitles = [new() { Title = "Part one", ReleaseYear = 2001 }, new() { Title = "Part two" }] },
            new() { Title = "別のゲーム", Points = 0 }
        };
        var duels = new List<Duel>
        {
            new() { Game1Id = games[1].Id, Game2Id = games[2].Id, IsCompleted = true, WinnerId = games[1].Id },
            new() { Game1Id = games[0].Id, Game2Id = games[2].Id },
            new() { Game1Id = games[0].Id, Game2Id = games[1].Id, IsCompleted = true, WinnerId = games[0].Id }
        };
        var main = new Tournament { Name = "Zebra", Games = games, Duels = duels };
        var empty = new Tournament { Name = "BÖNIS" };
        var legacy = new GameDatabase { CurrentTournamentId = main.Id, Tournaments = [main, empty] };
        // Written the way the earlier version saved it, with one included title in the oldest format.
        var document = JsonNode.Parse(JsonSerializer.Serialize(legacy))!;
        document["Tournaments"]![0]!["Games"]![1]!["IncludedTitles"]![1] = "Part two";
        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var jsonPath = Path.Combine(directory, "data.json");
        var path = CheckData.PathFor(directory, "data");
        File.WriteAllText(path + ".importing", "left behind by an interrupted attempt");

        var service = CheckData.FromLegacyFile(directory, "data", json);
        Check(File.Exists(path) && !File.Exists(path + ".importing"), "Opening beside an earlier JSON file creates the database and leaves no unfinished file");
        Check(CheckData.Snapshot(service) == JsonSerializer.Serialize(legacy),
            "Every tournament, entry, included title, cover, score, result and the selection arrives unchanged and in order");
        Check(File.ReadAllText(jsonPath) == json, "The JSON file itself is not changed");
        Check(service.GetGames().Select(game => game.Title).SequenceEqual(new[] { "Tied first", "Tied second", "別のゲーム" }) &&
            service.GetPendingDuels().Single().Id == duels[1].Id &&
            service.GetCurrentTournament()!.Duels.Select(duel => duel.Id).SequenceEqual(duels.Select(duel => duel.Id)),
            "Tied entries and duels keep the order they had in the file");
        Check(service.GetTournaments().Select(tournament => tournament.Name).SequenceEqual(new[] { "BÖNIS", "Zebra" }), "Tournaments are still listed by name");

        service.RecordWinner(duels[1].Id, games[2].Id);
        File.WriteAllText(jsonPath, "{\"Tournaments\":[],\"CurrentTournamentId\":null}");
        var reopened = CheckData.Open(path);
        Check(reopened.CompletedDuels() == 3 && reopened.GetGames().Single(game => game.Id == games[2].Id).Points == 1,
            "Results saved afterwards are there when the application starts again");
        Check(reopened.GetTournaments().Count == 2, "Once the database exists the JSON file is no longer read");

        Check(Count(path, "PRAGMA user_version") == 2 && Text(path, "PRAGMA journal_mode") == "wal",
            "The file records its schema version and can be read while the application writes");
        reopened.ImportGames(new[] { new Game { Title = "Removed", IncludedTitles = [new() { Title = "Removed part" }] } });
        var removed = reopened.GetGames().Single(game => game.Title == "Removed");
        Check(Count(path, "SELECT COUNT(*) FROM duels") == 6 && Count(path, "SELECT COUNT(*) FROM included_titles") == 3 &&
            reopened.RemoveGame(removed.Id) && Count(path, "SELECT COUNT(*) FROM duels") == 3 && Count(path, "SELECT COUNT(*) FROM included_titles") == 2,
            "Removing an entry removes its duels and included titles from the database");
        Check(reopened.DeleteTournament(main.Id) && Count(path, "SELECT COUNT(*) FROM games") == 0 && Count(path, "SELECT COUNT(*) FROM duels") == 0 &&
            Count(path, "SELECT COUNT(*) FROM included_titles") == 0 && reopened.GetCurrentTournament() is null && reopened.GetTournaments().Single().Id == empty.Id,
            "Deleting a tournament leaves none of its rows behind and clears the selection");
        Check(Fails(() => reopened.Import(legacy)) && reopened.GetTournaments().Count == 1, "Whole databases can only be imported into an empty one");

        // Data the tables cannot represent stops the move instead of being repaired quietly.
        var other = new Tournament { Name = "Other", Games = [new() { Title = "Elsewhere" }] };
        Refused("duel-without-winner", Legacy(d => d[0].WinnerId = null));
        Refused("stale-winner", Legacy(d => d[1].WinnerId = games[0].Id));
        Refused("outside-winner", Legacy(d => d[0].WinnerId = games[0].Id));
        Refused("self-duel", Legacy(d => d[1].Game2Id = d[1].Game1Id));
        Refused("unknown-game", Legacy(d => d[1].Game2Id = Guid.NewGuid()));
        Refused("other-tournament-game", new GameDatabase { Tournaments = [other, new() { Name = "Mixed", Games = games, Duels = [new() { Game1Id = games[0].Id, Game2Id = other.Games[0].Id }] }] });
        Refused("repeated-entry", new GameDatabase { Tournaments = [new() { Name = "Repeated", Games = [games[0], games[0]] }] });
        Refused("unknown-selection", new GameDatabase { CurrentTournamentId = Guid.NewGuid(), Tournaments = [empty] });
        Refused("invalid-year", new GameDatabase { Tournaments = [new() { Name = "Year", Games = [new() { Title = "Year", ReleaseYear = 10000 }] }] });
        Refused("not-json", "{\"Tournaments\": [");

        var blank = CheckData.FromLegacyFile(directory, "blank", "{\"Tournaments\": [], \"CurrentTournamentId\": null}\n");
        Check(blank.GetTournaments().Count == 0 && blank.CreateTournament("First").Name == "First", "An empty JSON file becomes an empty, usable database");
        Check(Fails(() => CheckData.Open(Path.Combine(directory, "blank.json"))), "The JSON file cannot be configured as the database");
        var newerPath = CheckData.PathFor(directory, "newer");
        CheckData.Open(newerPath).CreateTournament("Kept");
        Count(newerPath, "PRAGMA user_version = 99");
        Check(Fails(() => CheckData.Open(newerPath)) && Count(newerPath, "SELECT COUNT(*) FROM tournaments") == 1,
            "A database written by a newer version is refused and left alone");

        // A collection's picture used to be recorded on the collection; it now belongs to its first included title.
        var pictured = new Game { Title = "Pictured", CoverImage = "collection.jpg", IncludedTitles = [new() { Title = "First" }, new() { Title = "Second" }] };
        var single = new Game { Title = "Single", CoverImage = "single.jpg" };
        var earlier = new GameDatabase { Tournaments = [new() { Name = "Pictures", Games = [pictured, single] }] };
        bool Moved(GameService opened) => opened.Export().Tournaments.Single().Games is [var collection, var alone] && collection.CoverImage is null &&
            collection.IncludedTitles.Select(title => title.CoverImage).SequenceEqual(["collection.jpg", null]) &&
            collection.Cover == "collection.jpg" && alone.CoverImage == "single.jpg";
        Check(Moved(CheckData.FromLegacyFile(directory, "pictures", JsonSerializer.Serialize(earlier))),
            "A collection's picture in an earlier JSON file moves to its first included title");
        var versionPath = CheckData.PathFor(directory, "version1");
        Check(Moved(CheckData.Create(directory, "version1", earlier)), "Imported data from before titles had pictures is moved the same way");
        // The same database as the first version of the tables held it.
        foreach (var statement in new[] { "UPDATE games SET cover_image = 'collection.jpg' WHERE title = 'Pictured'",
            "ALTER TABLE included_titles DROP COLUMN cover_image", "PRAGMA user_version = 1" })
            Count(versionPath, statement);
        // As after a restart: the connections kept open above still remember the tables as they were.
        SqliteConnection.ClearAllPools();
        Check(Moved(CheckData.Open(versionPath)) && Count(versionPath, "PRAGMA user_version") == 2 &&
            Count(versionPath, "SELECT COUNT(*) FROM games WHERE cover_image IS NOT NULL") == 1,
            "An existing database is brought up to date: its collections' pictures move to their first included titles");
        Console.WriteLine("27 migration checks passed.");

        GameDatabase Legacy(Action<List<Duel>> change)
        {
            var copy = JsonSerializer.Deserialize<GameDatabase>(JsonSerializer.Serialize(legacy))!;
            change(copy.Tournaments[0].Duels);
            return copy;
        }

        void Refused(string name, object data)
        {
            var text = data as string ?? JsonSerializer.Serialize(data);
            var target = CheckData.PathFor(directory, name);
            Check(Fails(() => CheckData.FromLegacyFile(directory, name, text)) && !File.Exists(target) && !File.Exists(target + ".importing") &&
                File.ReadAllText(Path.Combine(directory, name + ".json")) == text,
                $"Legacy data that cannot be stored faithfully ({name}) stops the application, creating no database and leaving the file alone");
        }
    }

    // Read with a connection of the checks' own, as a backup or the sqlite3 command would.
    private static int Count(string path, string sql) => Convert.ToInt32(Scalar(path, sql));
    private static string Text(string path, string sql) => (string)Scalar(path, sql)!;
    private static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static bool Fails(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
