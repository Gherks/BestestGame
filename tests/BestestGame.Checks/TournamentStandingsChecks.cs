using System.Text.Json;
using BestestGame.Components;
using BestestGame.Models;
using BestestGame.Services;

static class TournamentStandingsChecks
{
    public static void Run()
    {
        var leader = new Game { Title = "Leader", Points = 12, ReleaseYear = 2007 };
        var collection = new Game { Title = "Birch collection", Points = 7, ReleaseYear = 2020,
            IncludedTitles = [new() { Title = "Separate release", ReleaseYear = 2007 },
                new() { Title = "Separate sequel", ReleaseYear = 2010 }, new() { Title = "別のゲーム" }] };
        var tied = new Game { Title = "Amber adventure", Points = 7, ReleaseYear = 2007 };
        var last = new Game { Title = "Last", Points = 0 };
        List<Game> games = [last, collection, leader, tied];
        var before = JsonSerializer.Serialize(games);
        var standings = TournamentStandings.Rank(games);
        Check(standings.Select(entry => entry.Rank).SequenceEqual([1, 2, 2, 4]), "Competition ranks share equal points and skip occupied positions");
        Check(standings.Select(entry => entry.Game.Id).SequenceEqual([leader.Id, tied.Id, collection.Id, last.Id]), "Points lead ordering; equal scores use alphabetical presentation");
        Check(TournamentStandings.Rank(games.AsEnumerable().Reverse()).Select(entry => entry.Game.Id)
            .SequenceEqual(standings.Select(entry => entry.Game.Id)), "Tie presentation stays stable regardless of source order");
        Check(TournamentStandings.Search(standings, "Last").Single().Rank == 4, "Search never promotes a lower-ranked entry");
        Check(TournamentStandings.Search(standings, " bIRCH ").Single().Game.Id == collection.Id, "Collection names match case-insensitive trimmed search");
        Check(TournamentStandings.Search(standings, "Separate").Single().Game.Id == collection.Id, "Multiple included-title matches return only one participant");
        Check(TournamentStandings.Search(standings, "別のゲーム").Single().Rank == 2, "Undated Unicode included titles are searchable without inventing a release year");
        Check(TournamentStandings.Search(standings, "  ").Count == 4 && TournamentStandings.Search(standings, null).Count == 4, "Blank search includes all standings");
        Check(TournamentStandings.Search(standings, "No match").Count == 0, "An unmatched search returns no entries");
        var year = TournamentStandings.Rank(games, 2007);
        Check(year.Count == 3 && year.Select(entry => entry.Rank).SequenceEqual([1, 2, 2]), "Year filtering preserves collections and shared full-tournament points");
        Check(TournamentStandings.Search(year, "Separate sequel").Single().Rank == 2, "Year standings are ranked before searching all included titles");
        Check(TournamentStandings.Rank(games, 2010).Single().Rank == 1 && TournamentStandings.Rank(games, 2020).Single().Game.Id == collection.Id,
            "Included or parent year qualifies one collection; ranks recalculate for that year");
        Check(TournamentStandings.Rank(games, 1900).Count == 0 && TournamentStandings.Rank([]).Count == 0, "Unknown and empty year views are empty");
        var zeros = TournamentStandings.Rank([new() { Title = "B" }, new() { Title = "A" }]);
        Check(zeros.All(entry => entry.Rank == 1), "Unplayed zero-point entries share first rank");
        var duel = new Duel { Game1Id = tied.Id, Game2Id = collection.Id, WinnerId = collection.Id, IsCompleted = true };
        var goty = ReleaseRankings.GetEntries(games, [duel]).Where(entry => entry.Year == 2007 && entry.Points == 7).ToList();
        Check(goty.First().GameId == collection.Id && goty.First().HeadToHeadWins == 1 && !goty[0].SharesRankWith(goty[1]),
            "GOTY still separates equal points using completed head-to-head results");
        Check(TournamentStandings.Rank(games).Where(entry => entry.Game.Points == 7).All(entry => entry.Rank == 2), "Full standings do not use GOTY tie-breaks");
        Check(JsonSerializer.Serialize(games) == before, "Rank and search leave IDs, stored points, titles, years and included releases intact");
        var legacyNull = new Game { Title = "Legacy (First, Second)", ReleaseYear = 2007, IncludedTitles = null! };
        Check(TournamentStandings.Search(TournamentStandings.Rank([legacyNull], 2007), "First").Single().Game == legacyNull &&
            legacyNull.IncludedTitles is null, "Legacy null lists remain searchable by stored title without mutation");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
