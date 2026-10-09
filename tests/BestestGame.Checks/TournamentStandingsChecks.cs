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
        Check(standings.Select(entry => entry.Game.Id).SequenceEqual([leader.Id, tied.Id, collection.Id, last.Id]), "Points lead ordering; equal scores without a result between them use alphabetical presentation");
        Check(TournamentStandings.Rank(games.AsEnumerable().Reverse()).Select(entry => entry.Game.Id)
            .SequenceEqual(standings.Select(entry => entry.Game.Id)), "Tie presentation stays stable regardless of source order");
        Check(TournamentStandings.Search(standings, "Last").Single().Rank == 4, "Search never promotes a lower-ranked entry");
        Check(TournamentStandings.Search(standings, " bIRCH ").Single().Game.Id == collection.Id, "Collection names match case-insensitive trimmed search");
        Check(TournamentStandings.Search(standings, "Separate").Single().Game.Id == collection.Id, "Multiple included-title matches return only one participant");
        Check(TournamentStandings.Search(standings, "別のゲーム").Single().Rank == 2, "Undated Unicode included titles are searchable without inventing a release year");
        Check(TournamentStandings.Search(standings, "  ").Count == 4 && TournamentStandings.Search(standings, null).Count == 4, "Blank search includes all standings");
        Check(TournamentStandings.Search(standings, "No match").Count == 0, "An unmatched search returns no entries");
        var year = TournamentStandings.Rank(games, year: 2007);
        Check(year.Count == 3 && year.Select(entry => entry.Rank).SequenceEqual([1, 2, 2]), "Year filtering preserves collections and shared full-tournament points");
        Check(TournamentStandings.Search(year, "Separate sequel").Single().Rank == 2, "Year standings are ranked before searching all included titles");
        Check(TournamentStandings.Rank(games, year: 2010).Single().Rank == 1 && TournamentStandings.Rank(games, year: 2020).Single().Game.Id == collection.Id,
            "Included or parent year qualifies one collection; ranks recalculate for that year");
        Check(TournamentStandings.Rank(games, year: 1900).Count == 0 && TournamentStandings.Rank([]).Count == 0, "Unknown and empty year views are empty");
        var zeros = TournamentStandings.Rank([new() { Title = "B" }, new() { Title = "A" }]);
        Check(zeros.All(entry => entry.Rank == 1), "Unplayed zero-point entries share first rank");
        var duel = new Duel { Game1Id = tied.Id, Game2Id = collection.Id, WinnerId = collection.Id, IsCompleted = true };
        var goty = ReleaseRankings.GetEntries(games, [duel]).Where(entry => entry.Year == 2007 && entry.Points == 7).ToList();
        Check(goty.First().GameId == collection.Id && goty.First().HeadToHeadWins == 1 && !goty[0].SharesRankWith(goty[1]),
            "GOTY still separates equal points using completed head-to-head results");
        Check(TournamentStandings.Rank(games).Where(entry => entry.Game.Points == 7).All(entry => entry.Rank == 2), "Without results, equal points share a rank");
        var decided = TournamentStandings.Rank(games, [duel]);
        Check(decided.Select(entry => entry.Game.Id).SequenceEqual([leader.Id, collection.Id, tied.Id, last.Id]) &&
            decided.Select(entry => entry.Rank).SequenceEqual([1, 2, 3, 4]), "A completed matchup between two entries level on points puts its winner first");
        Check(TournamentStandings.Rank(games, [duel], year: 2007).Select(entry => entry.Game.Id).SequenceEqual([leader.Id, collection.Id, tied.Id]),
            "Year standings use the same head-to-head order");
        duel.IsCompleted = false;
        Check(TournamentStandings.Rank(games, [duel]).Where(entry => entry.Game.Points == 7).All(entry => entry.Rank == 2), "A pending matchup cannot break a tie, even with a stale winner");
        duel.IsCompleted = true;
        HeadToHeadTiers();
        OpenLead();
        Check(JsonSerializer.Serialize(games) == before, "Rank and search leave IDs, stored points, titles, years and included releases intact");
        var legacyNull = new Game { Title = "Legacy (First, Second)", ReleaseYear = 2007, IncludedTitles = null! };
        Check(TournamentStandings.Search(TournamentStandings.Rank([legacyNull], year: 2007), "First").Single().Game == legacyNull &&
            legacyNull.IncludedTitles is null, "Legacy null lists remain searchable by stored title without mutation");
    }

    private static void HeadToHeadTiers()
    {
        var level = "ABCD".Select(letter => new Game { Title = letter.ToString(), Points = 5 }).ToList();
        Duel Beat(int winner, int loser) => new() { Game1Id = level[loser].Id, Game2Id = level[winner].Id, WinnerId = level[winner].Id, IsCompleted = true };
        string Order(IEnumerable<Duel> duels, IEnumerable<Game>? games = null)
            => string.Join(" ", TournamentStandings.Rank(games ?? level, duels).Select(entry => $"{entry.Game.Title}{entry.Rank}"));
        Check(Order([Beat(0, 1), Beat(1, 2), Beat(2, 0)], level.Take(3)) == "A1 B1 C1", "A circle of wins keeps its entries on a shared rank");
        Check(Order([Beat(2, 0), Beat(2, 1), Beat(1, 0)], level.Take(3)) == "C1 B2 A3", "Wins among the level entries order them");
        // A and B both win twice in the group, C and D once: each pair is then settled by its own matchup.
        Check(Order([Beat(1, 0), Beat(1, 3), Beat(0, 2), Beat(0, 3), Beat(2, 1), Beat(3, 2)]) == "B1 A2 D3 C4",
            "Entries still level after counting wins are compared again among themselves");
        Check(Order([Beat(2, 3)]) == "C1 A2 B2 D2", "One result lifts its winner; entries with nothing between them share a rank");
        var outsider = new Game { Title = "Outsider", Points = 9 };
        Check(Order([new() { Game1Id = outsider.Id, Game2Id = level[1].Id, WinnerId = level[1].Id, IsCompleted = true }], level.Take(2).Append(outsider)) == "Outsider1 A2 B2",
            "Results against entries on other points never break a tie");
    }

    // Whether a year's first place can still change, read from points and matchups left alone.
    private static void OpenLead()
    {
        var ahead = new Game { Title = "Ahead", ReleaseYear = 2026, Points = 48 };
        var chaser = new Game { Title = "Chaser", ReleaseYear = 2026, Points = 42 };
        var beaten = new Game { Title = "Beaten", ReleaseYear = 2026, Points = 10 };
        var elsewhere = new Game { Title = "Elsewhere", ReleaseYear = 2020, Points = 47 };
        List<Game> games = [ahead, chaser, beaten, elsewhere];
        ReleaseRankings.Lead Lead(Dictionary<Guid, int> pending, IEnumerable<Duel>? duels = null)
            => ReleaseRankings.GetLead(ReleaseRankings.GetEntries(games, duels).Where(entry => entry.Year == 2026).ToList(), pending);

        var open = Lead(new() { [ahead.Id] = 24, [chaser.Id] = 39, [beaten.Id] = 5, [elsewhere.Id] = 60 });
        Check(!open.Settled && open.Leaders.Single().GameId == ahead.Id && open.Contenders.Single() is { Pending: 39, Reach: 81 } contender &&
            contender.GameId == chaser.Id, "An unfinished nominee that can reach the leader's points keeps the year open; other years and hopeless entries do not");
        Check(Lead(new() { [ahead.Id] = 24, [chaser.Id] = 5, [beaten.Id] = 5 }) is { Settled: true, Contenders.Count: 0 },
            "The year is decided once nobody else can reach the leader, even while the leader has matchups left");
        Check(!Lead(new() { [chaser.Id] = 6 }).Settled && Lead(new() { [chaser.Id] = 5 }).Settled,
            "Reaching exactly the leader's points still counts as open");
        chaser.Points = 48;
        Check(Lead([]) is { Settled: true, Leaders.Count: 2 }, "Finished leaders level on points are shared winners");
        Check(Lead(new() { [chaser.Id] = 1 }) is { Settled: false, Contenders.Count: 0, Leaders.Count: 2 },
            "Leaders sharing first place stay open while one of them has a matchup left");
        Duel direct = new() { Game1Id = ahead.Id, Game2Id = chaser.Id, WinnerId = chaser.Id, IsCompleted = true };
        Check(Lead([], [direct]) is { Settled: true } decided && decided.Leaders.Single().GameId == chaser.Id,
            "A head-to-head result between level leaders decides the year");
        Check(ReleaseRankings.GetLead([], new Dictionary<Guid, int>()) is { Settled: true, Leaders.Count: 0 }, "A year without nominees has nothing open");
        var collection = new Game { Title = "Set", Points = 30, IncludedTitles = [new() { Title = "One", ReleaseYear = 2026 }, new() { Title = "Two", ReleaseYear = 2026 }] };
        games.Add(collection);
        Check(Lead(new() { [collection.Id] = 40 }, [direct]).Contenders.Single() is { Title: "Set", Reach: 70 },
            "A collection with several releases in the year is one contender, under its own name");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
