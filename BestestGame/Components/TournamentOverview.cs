using BestestGame.Models;

namespace BestestGame.Components;

// Read-only presentation policy shared by Home and tournament continuation links.
public static class TournamentOverview
{
    public enum Stage { NoTournaments, ChooseTournament, Setup, Voting, Complete }
    public sealed record NextTask(Stage Stage, string Label, string Href);
    public sealed record LeaderSummary(IReadOnlyList<Game> Games, int TotalTied);

    public static NextTask GetNextTask(Tournament? tournament, bool hasTournaments)
    {
        var stage = tournament is null
            ? hasTournaments ? Stage.ChooseTournament : Stage.NoTournaments
            : tournament.Games.Count < 2 ? Stage.Setup
            : tournament.Duels.Any(duel => !duel.IsCompleted) ? Stage.Voting : Stage.Complete;
        return stage switch
        {
            Stage.NoTournaments => new(stage, "Create a tournament", "/tournaments"),
            Stage.ChooseTournament => new(stage, "Choose a tournament", "/tournaments"),
            Stage.Setup => new(stage, "Add games", "/import"),
            Stage.Voting => new(stage, "Continue voting", "/vote"),
            _ => new(stage, "View rankings", "/rankings")
        };
    }

    public static LeaderSummary GetLeaders(Tournament tournament)
    {
        var leaders = TournamentStandings.Rank(tournament.Games, tournament.Duels)
            .Where(entry => entry.Rank == 1 && entry.Game.Points > 0).Select(entry => entry.Game).ToList();
        return new(leaders.Take(3).ToList(), leaders.Count);
    }
}
