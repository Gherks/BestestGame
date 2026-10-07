using System.Text.Json;
using BestestGame.Components;
using BestestGame.Models;

static class GameLibraryChecks
{
    public static void Run()
    {
        var collection = new Game { Title = "Collection", Points = 7,
            IncludedTitles = [new() { Title = "Unrelated release", ReleaseYear = 2007 },
                new() { Title = "Unrelated sequel", ReleaseYear = 2010 }, new() { Title = "別のゲーム" }] };
        var legacy = JsonSerializer.Deserialize<Game>("{\"Title\":\"Legacy (1, 2)\",\"Points\":3,\"IncludedTitles\":[\"Older release\"]}")!;
        var standalone = new Game { Title = "Étoile 東京", ReleaseYear = 2007, Points = 1 };
        var tournament = new Tournament { Games = [standalone, collection, legacy] };
        tournament.Duels = [new() { Game1Id = collection.Id, Game2Id = legacy.Id, IsCompleted = true, WinnerId = collection.Id },
            new() { Game1Id = collection.Id, Game2Id = standalone.Id, WinnerId = collection.Id },
            new() { Game1Id = legacy.Id, Game2Id = standalone.Id }];
        var before = JsonSerializer.Serialize(tournament);
        Check(GameLibrary.Search(tournament.Games, null).SequenceEqual(tournament.Games), "Null search preserves the complete library's display order");
        Check(GameLibrary.Search(tournament.Games, "   ").SequenceEqual(tournament.Games), "Blank/reset search restores every parent entry");
        Check(GameLibrary.Search(tournament.Games, "  cOlLeCtIoN ").Single().Id == collection.Id, "Search trims whitespace and matches parent titles without case sensitivity");
        Check(GameLibrary.Search(tournament.Games, "unrelated").Single().Id == collection.Id, "Multiple matching included titles return their collection once");
        Check(GameLibrary.Search(tournament.Games, "sequel").Single().Id == collection.Id, "An included release finds its parent regardless of its independent year");
        Check(GameLibrary.Search(tournament.Games, "別のゲーム").Single().Id == collection.Id, "Undated Unicode included titles are searchable");
        Check(GameLibrary.Search(tournament.Games, "ÉTOILE 東京").Single().Id == standalone.Id, "Unicode standalone titles remain searchable");
        Check(GameLibrary.Search(tournament.Games, "Older").Single().Id == legacy.Id, "Migrated string-based included titles are searchable");
        Check(GameLibrary.Search(tournament.Games, "Legacy (1").Single().Id == legacy.Id, "Legacy parent title text is searchable without rewriting it");
        Check(GameLibrary.Search(tournament.Games, "missing").Count == 0 && GameLibrary.Search([], "any").Count == 0, "Missing and empty-library searches have no matches");
        var counts = GameLibrary.PendingMatchups(tournament);
        Check(counts.Count == 3 && counts[collection.Id] == 1 && counts[legacy.Id] == 1 && counts[standalone.Id] == 2,
            "Pending counts use individual stored duels, ignoring completed results and a stale pending winner");
        Check(counts.Keys.ToHashSet().SetEquals(tournament.Games.Select(game => game.Id)), "Included titles never acquire separate pending participants");
        GameLibrary.Search(tournament.Games, "release");
        Check(JsonSerializer.Serialize(tournament) == before, "Library search/count reads leave order, IDs, points, years, included titles and duel state unchanged");
        foreach (var duel in tournament.Duels) duel.IsCompleted = true;
        Check(GameLibrary.PendingMatchups(tournament).Values.All(count => count == 0), "Completed entries have no unfinished actions");
        Check(GameLibrary.PendingMatchups(new Tournament { Games = [new() { Title = "Only entry" }] }).Values.Single() == 0,
            "A single entry with no duels has no misleading Finish matchups action");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
