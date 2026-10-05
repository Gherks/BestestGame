using System.Text.Json;
using BestestGame.Models;
using BestestGame.Services;
using Microsoft.Extensions.Configuration;

static class FocusedVotingChecks
{
    public static void Run(string directory)
    {
        var path = Path.Combine(directory, "focused-voting.json");
        var standings = Enumerable.Range(0, 113).Select(index =>
            new Game { Title = $"Opponent {index:D3}", Points = 112 - index }).ToList();
        var tournament = new Tournament { Name = "113 completed entries", Games = standings };
        for (var first = 0; first < standings.Count; first++)
            for (var second = first + 1; second < standings.Count; second++)
                tournament.Duels.Add(new Duel
                {
                    Game1Id = standings[first].Id, Game2Id = standings[second].Id,
                    WinnerId = standings[first].Id, IsCompleted = true
                });
        var seed = JsonSerializer.Serialize(new GameDatabase
        {
            CurrentTournamentId = tournament.Id, Tournaments = [tournament]
        });
        File.WriteAllText(path, seed);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DatabasePath"] = path }).Build();
        var service = new GameService(configuration, null!);
        service.ImportGames(["Newcomer"]);
        var newcomer = service.GetGames().Single(game => game.Title == "Newcomer");
        var pending = service.GetPendingDuels().OrderBy(duel =>
            standings.FindIndex(game => game.Id == OpponentId(duel, newcomer.Id))).ToArray();
        var session = new AdaptiveDuelSession();
        session.Synchronize(pending.Select(duel => duel.Id));
        Check(pending.Length == 113 && service.CompletedDuels() == 6328, "Adding an entry preserves all 6,328 old results and creates 113 matchups");
        Check(session.Snapshot().Length == 12 && session.Snapshot()[^1].Length == 3,
            "113 opponents form eleven groups of ten and a final group of three");
        Check(session.Snapshot().SelectMany(group => group).SequenceEqual(pending.Select(duel => duel.Id)),
            "Initial groups follow the standings with no missing or duplicate opponents");

        var initialQueue = session.Snapshot();
        var contentsBeforeSplit = File.ReadAllText(path);
        foreach (var size in new[] { 5, 3, 2, 1 })
        {
            Check(session.SplitCurrent(), "A mixed group can be split");
            Check(session.Current(true).Length == size && session.RemainingCount == 113,
                "Splitting reaches single duels without dropping opponents");
        }
        Check(!session.SplitCurrent() && File.ReadAllText(path) == contentsBeforeSplit,
            "A single duel cannot be split and no split records any results");
        session.Restore(initialQueue);
        Check(session.Current(true).Length == 10, "Undoing a split restores its original group");
        var originalFirst = session.Current(true);
        session.Synchronize(pending.Reverse().Select(duel => duel.Id));
        Check(session.Current(true).SequenceEqual(originalFirst), "Score changes cannot reorder an existing group");
        Check(session.Current(false).SequenceEqual(originalFirst.Take(1)), "Rapid 1v1 presents just the first opponent");
        session.SkipCurrent(false);
        Check(session.Current(false)[0] == originalFirst[1] && session.RemainingCount == 113,
            "Skipping a single duel keeps it in the queue and advances to the next opponent");
        session.Restore(initialQueue);
        session.SkipCurrent(true);
        Check(session.Current(true).SequenceEqual(initialQueue[1]) && session.Snapshot()[^1].SequenceEqual(initialQueue[0]),
            "Skipping a group moves the whole group to the end without resolving it");
        session.Restore(initialQueue);

        var wins = session.Current(true).Select(id => new GameService.DuelResult(id, newcomer.Id)).ToArray();
        Check(service.RecordWinners(tournament.Id, wins).Count == 10, "One group vote records ten individual results");
        Check(service.GetGames().Single(game => game.Id == newcomer.Id).Points == 10 && service.CompletedDuels() == 6338,
            "A winning batch awards exactly one point per duel and persists after reload");
        var contentsAfterWins = File.ReadAllText(path);
        Check(service.RecordWinners(tournament.Id, wins).Count == 0 && File.ReadAllText(path) == contentsAfterWins,
            "Repeating a completed batch cannot add points or overwrite results");
        service.RecordWinner(wins[0].DuelId, OpponentId(pending[0], newcomer.Id));
        service.RecordWinner(pending[10].Id, standings[0].Id);
        Check(File.ReadAllText(path) == contentsAfterWins, "Single voting also rejects completed matches and invalid winners");
        session.Synchronize(service.GetPendingDuels().Select(duel => duel.Id));
        Check(session.RemainingCount == 103 && session.Current(true).SequenceEqual(initialQueue[1]),
            "Completed opponents leave the queue without reshuffling the next group");
        var losses = session.Current(true).Select(id => new GameService.DuelResult(id,
            OpponentId(pending.Single(duel => duel.Id == id), newcomer.Id))).ToArray();
        Check(service.RecordWinners(tournament.Id, losses).Count == 10, "A losing batch records a separate win for every opponent");
        Check(losses.All(vote => service.GetGames().Single(game => game.Id == vote.WinnerId).Points ==
            standings.Single(game => game.Id == vote.WinnerId).Points + 1), "Each winning opponent receives exactly one point");
        Check(service.GetGames().Single(game => game.Id == newcomer.Id).Points == 10, "Losing a batch does not add points to the newcomer");

        Check(service.ChangeCompletedDuelWinner(losses[0].DuelId, newcomer.Id), "Individual batch results remain editable");
        var contentsAfterCorrection = File.ReadAllText(path);
        Check(!service.UndoWinners(tournament.Id, losses) && File.ReadAllText(path) == contentsAfterCorrection,
            "Batch undo refuses to erase a later correction and does not partially undo anything");
        service.ChangeCompletedDuelWinner(losses[0].DuelId, losses[0].WinnerId);
        Check(service.UndoWinners(tournament.Id, losses) && service.CompletedDuels() == 6338,
            "Undo reverses every result in a batch together");
        Check(losses.All(vote => service.GetGames().Single(game => game.Id == vote.WinnerId).Points ==
            standings.Single(game => game.Id == vote.WinnerId).Points), "Undo restores all opponents' scores");
        var contentsAfterUndo = File.ReadAllText(path);
        Check(!service.UndoWinners(tournament.Id, losses) && File.ReadAllText(path) == contentsAfterUndo,
            "Repeated undo cannot subtract points again");
        Check(service.RecordWinners(tournament.Id, [losses[0], new(Guid.NewGuid(), newcomer.Id)]).Count == 0 &&
            File.ReadAllText(path) == contentsAfterUndo, "An invalid batch never partially records its valid results");
        Check(service.RecordWinners(tournament.Id, [losses[0], new(losses[0].DuelId, newcomer.Id)]).Count == 0 &&
            File.ReadAllText(path) == contentsAfterUndo, "Conflicting winners for the same duel are rejected");

        Parallel.Invoke(() => service.RecordWinners(tournament.Id, losses), () => service.RecordWinners(tournament.Id, losses),
            () => service.ImportGames(["Concurrent entry"]));
        Check(service.CompletedDuels() == 6348 && losses.All(vote => service.GetGames().Single(game => game.Id == vote.WinnerId).Points ==
            standings.Single(game => game.Id == vote.WinnerId).Points + 1), "Concurrent duplicate batches still award points only once");
        Check(service.GetGames().Count == 115 && service.TotalDuels() == 6555,
            "Concurrent importing retains the new entry, every generated duel, and all saved votes");
        service.CreateTournament("Different tournament");
        var contentsAfterSwitch = File.ReadAllText(path);
        Check(service.RecordWinners(tournament.Id, losses).Count == 0 && !service.UndoWinners(tournament.Id, losses) &&
            File.ReadAllText(path) == contentsAfterSwitch, "Voting and undo cannot cross a tournament switch");

        // Alternate wins and losses throughout the standings, forcing recursive
        // splits instead of allowing rank position to imply unasked results.
        File.WriteAllText(path, seed);
        service.ImportGames(["Newcomer"]);
        newcomer = service.GetGames().Single(game => game.Title == "Newcomer");
        pending = service.GetPendingDuels().OrderBy(duel =>
            standings.FindIndex(game => game.Id == OpponentId(duel, newcomer.Id))).ToArray();
        var desiredWins = pending.Select((duel, index) => (duel.Id, Wins: index % 2 == 0))
            .ToDictionary(item => item.Id, item => item.Wins);
        session = new AdaptiveDuelSession();
        session.Synchronize(pending.Select(duel => duel.Id));
        while (session.RemainingCount > 0)
        {
            var current = session.Current(true);
            if (current.Select(id => desiredWins[id]).Distinct().Count() > 1)
            {
                session.SplitCurrent();
                continue;
            }
            var results = current.Select(id => new GameService.DuelResult(id, desiredWins[id] ? newcomer.Id :
                OpponentId(pending.Single(duel => duel.Id == id), newcomer.Id))).ToArray();
            Check(service.RecordWinners(tournament.Id, results).Count == current.Length, "Every explicit choice records its intended matchups");
            session.Remove(current);
        }
        Check(service.GetPendingDuels().Count == 0 && service.CompletedDuels() == 6441 &&
            service.GetGames().Single(game => game.Id == newcomer.Id).Points == 57,
            "A full adaptive session finishes all 113 duels with arbitrary preferences and the exact score");
        Check(service.GetCurrentTournament()!.Duels.Where(duel => duel.Game1Id != newcomer.Id && duel.Game2Id != newcomer.Id)
            .All(duel => duel.IsCompleted && duel.WinnerId == duel.Game1Id), "Focused voting preserves every existing result");

        var restored = new AdaptiveDuelSession();
        restored.Restore([[pending[0].Id, pending[0].Id], [], [pending[1].Id]]);
        restored.Synchronize([pending[1].Id, pending[2].Id]);
        Check(restored.Snapshot().SelectMany(group => group).SequenceEqual(new[] { pending[1].Id, pending[2].Id }),
            "Resuming prunes completed and duplicate matches and appends newly available opponents");
    }

    private static Guid OpponentId(Duel duel, Guid entryId) => duel.Game1Id == entryId ? duel.Game2Id : duel.Game1Id;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
