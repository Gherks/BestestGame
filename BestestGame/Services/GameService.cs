using System.Text.Json;
using BestestGame.Models;

namespace BestestGame.Services;

public class GameService
{
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

        var duel = pending[_random.Next(pending.Count)];
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
}
