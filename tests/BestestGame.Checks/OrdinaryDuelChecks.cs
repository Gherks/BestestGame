using System.Text.Json;
using BestestGame.Models;
using BestestGame.Services;

static class OrdinaryDuelChecks
{
    public static void Run(string directory)
    {
        var games = new List<Game> { new() { Title = "Collection", IncludedTitles = [new() { Title = "Independent", ReleaseYear = 2007 }, new() { Title = "Undated" }] }, new() { Title = "Other", ReleaseYear = 2010 }, new() { Title = "Third" } };
        var duels = new List<Duel> { new() { Game1Id = games[0].Id, Game2Id = games[1].Id }, new() { Game1Id = games[0].Id, Game2Id = games[2].Id }, new() { Game1Id = games[1].Id, Game2Id = games[2].Id } };
        var tournament = new Tournament { Name = "Ordinary", Games = games, Duels = duels };
        var other = new Tournament { Name = "Separate" };
        var service = CheckData.Create(directory, "ordinary-voting", new GameDatabase { CurrentTournamentId = tournament.Id, Tournaments = [tournament, other] });
        var session = new OrdinaryDuelSession();
        HashSet<Guid> excluded = [];
        var before = CheckData.Snapshot(service);
        session.Select(service.GetPendingDuels(), excluded, duels[0].Id);
        Check(session.Current?.Id == duels[0].Id && !session.CanUndo, "A preferred pending duel opens without undo history");
        var oldPresentation = session.Presentation;
        session.Select(service.GetPendingDuels(), excluded, avoid: duels[0].Id);
        Check(session.Current?.Id != duels[0].Id && session.Presentation != oldPresentation, "Skip chooses another available duel and advances presentation");
        Check(CheckData.Snapshot(service) == before, "Choosing and skipping never write results or scores");
        Check(session.Vote(service, tournament.Id, oldPresentation, duels[0].Id, games[0].Id) == OrdinaryDuelSession.VoteOutcome.Ignored, "A queued choice from the skipped round is ignored");
        session.Select([duels[0]], excluded, avoid: duels[0].Id);
        Check(session.Current?.Id == duels[0].Id && CheckData.Snapshot(service) == before, "A sole available matchup stays pending on Skip");
        session.Select(duels, new HashSet<Guid> { games[0].Id });
        Check(session.Current?.Id == duels[2].Id, "Both participants respect tournament exclusions");
        session.Select(duels, new HashSet<Guid> { games[0].Id, games[1].Id });
        Check(session.Current is null, "Excluded-only queues have no available choice");
        session.Select([], excluded);
        Check(session.Current is null, "Completed or empty queues have no choice");
        session.Select(duels, excluded, duels[0].Id);
        var presentation = session.Presentation;
        Check(session.Vote(service, tournament.Id, presentation, duels[1].Id, games[0].Id) == OrdinaryDuelSession.VoteOutcome.Ignored &&
            session.Vote(service, tournament.Id, presentation, duels[0].Id, games[2].Id) == OrdinaryDuelSession.VoteOutcome.Ignored && CheckData.Snapshot(service) == before,
            "Wrong duel and non-participant choices cannot write data");
        Check(session.Vote(service, tournament.Id, presentation, duels[0].Id, games[0].Id) == OrdinaryDuelSession.VoteOutcome.Saved &&
            session.LastVote == new GameService.DuelResult(duels[0].Id, games[0].Id), "Voting retains the exact saved winner for guarded undo");
        var saved = CheckData.Snapshot(service);
        Check(service.CompletedDuels() == 1 && service.GetGames().Single(game => game.Id == games[0].Id).Points == 1,
            "A collection vote awards one parent point and completes one duel");
        Check(session.Vote(service, tournament.Id, presentation, duels[0].Id, games[1].Id) == OrdinaryDuelSession.VoteOutcome.Changed && CheckData.Snapshot(service) == saved,
            "Repeated completed input cannot overwrite a winner or add points");
        session.Select(service.GetPendingDuels(), excluded, duels[1].Id);
        var skipped = session.Current!.Id;
        session.Select(service.GetPendingDuels(), excluded, avoid: skipped);
        Check(session.Current?.Id != skipped && session.CanUndo && session.LastVote?.DuelId == duels[0].Id,
            "Skip after a vote retains that vote's undo and chooses another pending matchup");
        Check(session.Undo(service, tournament.Id, presentation) == OrdinaryDuelSession.UndoOutcome.Ignored && session.CanUndo && CheckData.Snapshot(service) == saved,
            "Stale Undo cannot consume newer undo availability");
        Check(session.Undo(service, tournament.Id, session.Presentation) == OrdinaryDuelSession.UndoOutcome.Undone && !session.CanUndo,
            "Vote then Skip then Undo reverses the last saved vote");
        session.Select(service.GetPendingDuels(), excluded, duels[0].Id);
        Check(session.Current?.Id == duels[0].Id && service.CompletedDuels() == 0 && service.GetGames().All(game => game.Points == 0),
            "Undo restores the same pending duel and original parent scores");
        Check(JsonSerializer.Serialize(service.GetCurrentTournament()) == JsonSerializer.Serialize(tournament),
            "A full vote/skip/undo cycle preserves original IDs, years, included titles and duel identities");
        before = CheckData.Snapshot(service);
        Check(session.Undo(service, tournament.Id, session.Presentation) == OrdinaryDuelSession.UndoOutcome.Ignored && CheckData.Snapshot(service) == before,
            "Repeated Undo has no result to remove");
        session.Vote(service, tournament.Id, session.Presentation, duels[0].Id, games[0].Id);
        service.ChangeCompletedDuelWinner(duels[0].Id, games[1].Id);
        before = CheckData.Snapshot(service);
        Check(session.Undo(service, tournament.Id, session.Presentation) == OrdinaryDuelSession.UndoOutcome.Changed && !session.CanUndo && CheckData.Snapshot(service) == before,
            "Undo rejects a later correction and preserves its winner and points");
        session.Select(service.GetPendingDuels(), excluded, duels[1].Id);
        session.Vote(service, tournament.Id, session.Presentation, duels[1].Id, games[0].Id);
        service.UndoMatch(duels[1].Id);
        before = CheckData.Snapshot(service);
        Check(session.Undo(service, tournament.Id, session.Presentation) == OrdinaryDuelSession.UndoOutcome.Changed && CheckData.Snapshot(service) == before,
            "An already removed result cannot be undone or decremented again");
        session.Select(service.GetPendingDuels(), excluded, duels[2].Id);
        var competitor = new OrdinaryDuelSession();
        competitor.Select(service.GetPendingDuels(), excluded, duels[2].Id);
        competitor.Vote(service, tournament.Id, competitor.Presentation, duels[2].Id, games[1].Id);
        before = CheckData.Snapshot(service);
        Check(session.Vote(service, tournament.Id, session.Presentation, duels[2].Id, games[2].Id) == OrdinaryDuelSession.VoteOutcome.Changed && CheckData.Snapshot(service) == before,
            "A stale presentation cannot replace another session's saved result");
        competitor.Select(service.GetPendingDuels(), excluded);
        service.SelectTournament(other.Id);
        before = CheckData.Snapshot(service);
        Check(competitor.Undo(service, tournament.Id, competitor.Presentation) == OrdinaryDuelSession.UndoOutcome.Changed && CheckData.Snapshot(service) == before,
            "Undo cannot cross the application-wide tournament selection");
        session.Select([duels[1]], excluded);
        Check(session.Vote(service, tournament.Id, session.Presentation, duels[1].Id, games[0].Id) == OrdinaryDuelSession.VoteOutcome.Changed && CheckData.Snapshot(service) == before,
            "Voting cannot cross the selected tournament");
        var token = session.Presentation;
        session.Reset();
        Check(session.Current is null && !session.CanUndo && session.Presentation != token && CheckData.Snapshot(service) == before,
            "New visits/tournaments reset only local choices and undo state");
        ClosestFirst();
        UndoHistory(directory);
        Sides();
        Console.WriteLine("38 ordinary voting checks passed.");
    }

    // Selection order only: no service or stored data is involved.
    private static void ClosestFirst()
    {
        var entries = new[] { 10, 10, 7, 2 }.Select(points => new Game { Points = points }).ToArray();
        var points = entries.ToDictionary(game => game.Id, game => game.Points);
        Duel Pair(int first, int second) => new() { Game1Id = entries[first].Id, Game2Id = entries[second].Id };
        Duel level = Pair(0, 1), nearA = Pair(0, 2), nearB = Pair(1, 2), far = Pair(0, 3);
        Duel[] pending = [far, nearA, level, nearB];
        HashSet<Guid> excluded = [];
        var session = new OrdinaryDuelSession(new Random(1));
        HashSet<Guid> Picks(Guid? avoid = null, IReadOnlySet<Guid>? without = null, bool closest = true)
            => Enumerable.Range(0, 200).Select(_ =>
            {
                session.Select(pending, without ?? excluded, avoid: avoid, points: closest ? points : null);
                return session.Current!.Id;
            }).ToHashSet();
        Check(Picks().SetEquals([level.Id]), "Closest first always opens the matchup that is level on points");
        Check(Picks(avoid: level.Id).SetEquals([nearA.Id, nearB.Id]),
            "Skip moves on to the next closest matchups, and equally close ones stay random");
        Check(Picks(without: new HashSet<Guid> { entries[1].Id }).SetEquals([nearA.Id]),
            "Closest first only considers matchups without excluded entries");
        session.Select(pending, excluded, preferred: far.Id, points: points);
        Check(session.Current?.Id == far.Id, "A requested matchup still opens ahead of closer ones");
        Check(Picks(closest: false).SetEquals(pending.Select(duel => duel.Id)),
            "With closest first off, every available matchup can come up");
    }

    // Which entry is shown on the left must not depend on which was added first.
    private static void Sides()
    {
        Guid older = Guid.NewGuid(), newer = Guid.NewGuid();
        var shown = Enumerable.Range(0, 400).Select(_ => new Duel { Game1Id = older, Game2Id = newer })
            .Select(duel => (Duel: duel, Sides: OrdinaryDuelSession.Sides(duel))).ToList();
        Check(shown.All(item => (item.Sides.Left == older && item.Sides.Right == newer) || (item.Sides.Left == newer && item.Sides.Right == older)),
            "Both participants are always shown, one on each side");
        var olderOnLeft = shown.Count(item => item.Sides.Left == older);
        Check(olderOnLeft is > 140 and < 260, "The entry added first is on the left in about half of the duels");
        Check(shown.All(item => OrdinaryDuelSession.Sides(item.Duel) == item.Sides), "A duel keeps its sides whenever it is shown again");
    }

    private static void UndoHistory(string directory)
    {
        var games = Enumerable.Range(0, 4).Select(index => new Game { Title = $"Entry {index}" }).ToList();
        var duels = (from first in Enumerable.Range(0, 4) from second in Enumerable.Range(first + 1, 3 - first)
                     select new Duel { Game1Id = games[first].Id, Game2Id = games[second].Id }).ToList();
        var tournament = new Tournament { Name = "Undo", Games = games, Duels = duels };
        var service = CheckData.Create(directory, "ordinary-undo", new GameDatabase { CurrentTournamentId = tournament.Id, Tournaments = [tournament] });
        HashSet<Guid> excluded = [];
        var session = new OrdinaryDuelSession();
        void Vote(OrdinaryDuelSession voter, Duel duel)
        {
            voter.Select(service.GetPendingDuels(), excluded, duel.Id);
            voter.Vote(service, tournament.Id, voter.Presentation, duel.Id, duel.Game1Id);
        }
        foreach (var duel in duels.Take(3)) Vote(session, duel);
        Check(session.History.Select(result => result.DuelId).SequenceEqual(duels.Take(3).Select(duel => duel.Id)) &&
            session.LastVote?.DuelId == duels[2].Id, "Each saved vote joins the undo history, newest last");
        // A reload starts a new session that takes over the history the tab kept.
        var reloaded = new OrdinaryDuelSession();
        reloaded.RestoreHistory(session.History, service.GetCurrentTournament()!.Duels);
        Check(reloaded.CanUndo && reloaded.History.SequenceEqual(session.History), "A new session takes over the saved history unchanged");
        var undone = Enumerable.Range(0, 3).Select(_ => reloaded.Undo(service, tournament.Id, reloaded.Presentation)).ToArray();
        Check(undone.All(outcome => outcome == OrdinaryDuelSession.UndoOutcome.Undone) && !reloaded.CanUndo &&
            service.CompletedDuels() == 0 && service.GetGames().All(game => game.Points == 0),
            "Every restored vote can be undone in turn, leaving no results or points behind");
        foreach (var duel in duels.Take(3)) Vote(session = new OrdinaryDuelSession(), duel);
        GameService.DuelResult[] kept = [new(duels[0].Id, duels[0].Game1Id), new(duels[1].Id, duels[1].Game1Id), new(duels[2].Id, duels[2].Game1Id)];
        service.ChangeCompletedDuelWinner(duels[0].Id, duels[0].Game2Id);
        service.UndoMatch(duels[1].Id);
        var stored = service.GetCurrentTournament()!.Duels;
        Check(OrdinaryDuelSession.StillSaved([.. kept, null, new(Guid.NewGuid(), games[0].Id)], stored).SequenceEqual([kept[2]]),
            "Corrected, removed, unknown and empty entries are dropped when a history is restored");
        Check(OrdinaryDuelSession.StillSaved([kept[2], kept[2]], stored).Count == 1, "A repeated entry is restored once");
        var many = Enumerable.Range(0, OrdinaryDuelSession.UndoCapacity + 5)
            .Select(_ => new Duel { IsCompleted = true, WinnerId = games[0].Id }).ToList();
        var capped = OrdinaryDuelSession.StillSaved(many.Select(duel => new GameService.DuelResult(duel.Id, games[0].Id)), many);
        Check(capped.Count == OrdinaryDuelSession.UndoCapacity && capped[^1].DuelId == many[^1].Id && capped[0].DuelId == many[5].Id,
            "A long history keeps only its newest votes");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
