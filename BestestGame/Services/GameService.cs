using System.Text.Json;
using BestestGame.Models;

namespace BestestGame.Services;

public class GameService
{
    public sealed record LossDetail(Guid DuelId, Guid WinnerId, string WinnerTitle);

    private readonly string _dbPath;
    private readonly Random _random = new();

    public GameService(IConfiguration configuration, IWebHostEnvironment env)
    {
        var configuredPath = configuration["DatabasePath"];
        _dbPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(env.ContentRootPath, "data.json")
            : Path.GetFullPath(configuredPath);
    }

    private GameDatabase Load()
    {
        if (!File.Exists(_dbPath))
            return new GameDatabase();

        var json = File.ReadAllText(_dbPath);
        return JsonSerializer.Deserialize<GameDatabase>(json) ?? new GameDatabase();
    }

    private void Save(GameDatabase db)
    {
        var json = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_dbPath, json);
    }

    private Tournament? GetCurrentTournament(GameDatabase db)
    {
        if (db.CurrentTournamentId is null)
            return null;

        return db.Tournaments.FirstOrDefault(t => t.Id == db.CurrentTournamentId);
    }

    /// <summary>
    /// Returns all tournaments ordered by name.
    /// </summary>
    public List<Tournament> GetTournaments()
    {
        return Load().Tournaments.OrderBy(t => t.Name).ToList();
    }

    /// <summary>
    /// Returns the currently selected tournament, or null if none is selected.
    /// </summary>
    public Tournament? GetCurrentTournament()
    {
        var db = Load();
        return GetCurrentTournament(db);
    }

    /// <summary>
    /// Creates a new tournament and sets it as the current one.
    /// </summary>
    public Tournament CreateTournament(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var db = Load();
        var tournament = new Tournament { Name = name.Trim() };
        db.Tournaments.Add(tournament);
        db.CurrentTournamentId = tournament.Id;
        Save(db);
        return tournament;
    }

    /// <summary>
    /// Sets the given tournament as the current one.
    /// </summary>
    public void SelectTournament(Guid tournamentId)
    {
        var db = Load();
        var tournament = db.Tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (tournament is null)
            return;

        db.CurrentTournamentId = tournamentId;
        Save(db);
    }

    public List<Game> GetGames()
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return [];

        return tournament.Games.OrderByDescending(g => g.Points).ToList();
    }

    public List<Duel> GetPendingDuels()
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return [];

        return tournament.Duels.Where(d => !d.IsCompleted).ToList();
    }

    public (Duel? duel, Game? game1, Game? game2) GetRandomPendingDuel()
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return (null, null, null);

        var pending = tournament.Duels.Where(d => !d.IsCompleted).ToList();
        if (pending.Count == 0)
            return (null, null, null);

        var duel = PickDuel(tournament, pending);
        var game1 = tournament.Games.FirstOrDefault(g => g.Id == duel.Game1Id);
        var game2 = tournament.Games.FirstOrDefault(g => g.Id == duel.Game2Id);
        return (duel, game1, game2);
    }

    private Duel PickDuel(Tournament tournament, List<Duel> pending)
    {
        int total = tournament.Duels.Count;
        int completed = total - pending.Count;

        // After 33% of matches are completed, prioritize duels involving bottom-ranked games
        if (total > 0 && completed >= total / 3)
        {
            var bottomGameIds = tournament.Games
                .OrderBy(g => g.Points)
                .Take(Math.Max(1, tournament.Games.Count / 3))
                .Select(g => g.Id)
                .ToHashSet();

            var bottomDuels = pending
                .Where(d => bottomGameIds.Contains(d.Game1Id) || bottomGameIds.Contains(d.Game2Id))
                .ToList();

            if (bottomDuels.Count > 0)
                return bottomDuels[_random.Next(bottomDuels.Count)];
        }

        return pending[_random.Next(pending.Count)];
    }

    /// <summary>
    /// Returns a specific duel and its two games by duel ID.
    /// </summary>
    public (Duel? duel, Game? game1, Game? game2) GetDuel(Guid duelId)
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return (null, null, null);

        var duel = tournament.Duels.FirstOrDefault(d => d.Id == duelId);
        if (duel is null)
            return (null, null, null);

        var game1 = tournament.Games.FirstOrDefault(g => g.Id == duel.Game1Id);
        var game2 = tournament.Games.FirstOrDefault(g => g.Id == duel.Game2Id);
        return (duel, game1, game2);
    }

    public void RecordWinner(Guid duelId, Guid winnerId)
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return;

        var duel = tournament.Duels.FirstOrDefault(d => d.Id == duelId);
        if (duel == null) return;

        duel.IsCompleted = true;
        duel.WinnerId = winnerId;

        var winner = tournament.Games.FirstOrDefault(g => g.Id == winnerId);
        if (winner != null)
            winner.Points++;

        Save(db);
    }

    public void ImportGames(IEnumerable<string> titles)
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return;

        var newGames = titles
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Where(t => !tournament.Games.Any(g => string.Equals(g.Title, t, StringComparison.OrdinalIgnoreCase)))
            .Select(t => new Game { Title = t })
            .ToList();

        tournament.Games.AddRange(newGames);

        // Generate all missing duels (every game vs every other game)
        var allGames = tournament.Games;
        for (int i = 0; i < allGames.Count; i++)
        {
            for (int j = i + 1; j < allGames.Count; j++)
            {
                var g1 = allGames[i];
                var g2 = allGames[j];
                bool duelExists = tournament.Duels.Any(d =>
                    (d.Game1Id == g1.Id && d.Game2Id == g2.Id) ||
                    (d.Game1Id == g2.Id && d.Game2Id == g1.Id));

                if (!duelExists)
                {
                    tournament.Duels.Add(new Duel { Game1Id = g1.Id, Game2Id = g2.Id });
                }
            }
        }

        Save(db);
    }

    /// <summary>
    /// Removes a game from the current tournament and deletes all duels that include it.
    /// </summary>
    public bool RemoveGame(Guid gameId)
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return false;

        var game = tournament.Games.FirstOrDefault(g => g.Id == gameId);
        if (game is null)
            return false;

        tournament.Games.Remove(game);
        tournament.Duels.RemoveAll(d => d.Game1Id == gameId || d.Game2Id == gameId);
        RecalculatePoints(tournament);

        Save(db);
        return true;
    }

    public bool HasPendingDuels() => GetPendingDuels().Count > 0;

    public int TotalDuels()
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        return tournament?.Duels.Count ?? 0;
    }

    public int CompletedDuels()
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        return tournament?.Duels.Count(d => d.IsCompleted) ?? 0;
    }

    /// <summary>
    /// Undoes a completed duel by resetting its state and decrementing the winner's points.
    /// </summary>
    public void UndoMatch(Guid duelId)
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return;

        var duel = tournament.Duels.FirstOrDefault(d => d.Id == duelId);
        if (duel is null || !duel.IsCompleted)
            return;

        if (duel.WinnerId is { } winnerId)
        {
            var winner = tournament.Games.FirstOrDefault(g => g.Id == winnerId);
            if (winner is not null)
                winner.Points = Math.Max(0, winner.Points - 1);
        }

        duel.IsCompleted = false;
        duel.WinnerId = null;

        Save(db);
    }

    /// <summary>
    /// Returns a dictionary mapping each game ID to detailed completed losses.
    /// </summary>
    public Dictionary<Guid, List<LossDetail>> GetGamesPickedOverDetails()
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return [];

        var gamesById = tournament.Games.ToDictionary(g => g.Id);
        var result = tournament.Games.ToDictionary(g => g.Id, _ => new List<LossDetail>());

        foreach (var duel in tournament.Duels.Where(d => d.IsCompleted))
        {
            if (duel.WinnerId is not { } winnerId)
                continue;

            var loserId = duel.Game1Id == winnerId ? duel.Game2Id : duel.Game1Id;

            if (gamesById.TryGetValue(winnerId, out var winner) && result.ContainsKey(loserId))
            {
                result[loserId].Add(new LossDetail(duel.Id, winnerId, winner.Title));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a dictionary mapping each game ID to detailed wins (games the given game beat).
    /// </summary>
    public Dictionary<Guid, List<LossDetail>> GetWinsOverDetails()
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return [];

        var gamesById = tournament.Games.ToDictionary(g => g.Id);
        var result = tournament.Games.ToDictionary(g => g.Id, _ => new List<LossDetail>());

        foreach (var duel in tournament.Duels.Where(d => d.IsCompleted))
        {
            if (duel.WinnerId is not { } winnerId)
                continue;

            var loserId = duel.Game1Id == winnerId ? duel.Game2Id : duel.Game1Id;

            if (gamesById.TryGetValue(loserId, out var loser) && result.ContainsKey(winnerId))
            {
                result[winnerId].Add(new LossDetail(duel.Id, loserId, loser.Title));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a dictionary mapping each game ID to the titles of games that were picked over it.
    /// </summary>
    public Dictionary<Guid, List<string>> GetGamesPickedOver()
    {
        return GetGamesPickedOverDetails()
            .ToDictionary(
                x => x.Key,
                x => x.Value.Select(loss => loss.WinnerTitle).ToList());
    }

    /// <summary>
    /// Changes the winner of a completed duel and updates points.
    /// </summary>
    public bool ChangeCompletedDuelWinner(Guid duelId, Guid winnerId)
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return false;

        var duel = tournament.Duels.FirstOrDefault(d => d.Id == duelId);
        if (duel is null || !duel.IsCompleted || !duel.WinnerId.HasValue)
            return false;

        if (duel.Game1Id != winnerId && duel.Game2Id != winnerId)
            return false;

        var previousWinnerId = duel.WinnerId.Value;
        if (previousWinnerId == winnerId)
            return false;

        var previousWinner = tournament.Games.FirstOrDefault(g => g.Id == previousWinnerId);
        var newWinner = tournament.Games.FirstOrDefault(g => g.Id == winnerId);
        if (newWinner is null)
            return false;

        if (previousWinner is not null)
            previousWinner.Points = Math.Max(0, previousWinner.Points - 1);

        newWinner.Points++;
        duel.WinnerId = winnerId;

        Save(db);
        return true;
    }

    /// <summary>
    /// Returns a dictionary mapping each game ID to the number of completed duels it participated in.
    /// </summary>
    public Dictionary<Guid, int> GetMatchesPlayedPerGame()
    {
        var db = Load();
        var tournament = GetCurrentTournament(db);
        if (tournament is null)
            return [];

        var counts = new Dictionary<Guid, int>();
        foreach (var game in tournament.Games)
        {
            counts[game.Id] = 0;
        }

        foreach (var duel in tournament.Duels.Where(d => d.IsCompleted))
        {
            if (counts.ContainsKey(duel.Game1Id))
                counts[duel.Game1Id]++;
            if (counts.ContainsKey(duel.Game2Id))
                counts[duel.Game2Id]++;
        }

        return counts;
    }

    private static void RecalculatePoints(Tournament tournament)
    {
        foreach (var game in tournament.Games)
        {
            game.Points = 0;
        }

        var gamesById = tournament.Games.ToDictionary(g => g.Id);
        foreach (var duel in tournament.Duels.Where(d => d.IsCompleted))
        {
            if (!duel.WinnerId.HasValue)
                continue;

            if (gamesById.TryGetValue(duel.WinnerId.Value, out var winner))
            {
                winner.Points++;
            }
        }
    }
}
