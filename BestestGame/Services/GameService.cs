using System.Text.Json;
using BestestGame.Models;

namespace BestestGame.Services;

public class GameService
{
    private readonly string _dbPath;
    private readonly Random _random = new();

    public GameService(IWebHostEnvironment env)
    {
        _dbPath = Path.Combine(env.ContentRootPath, "data.json");
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

    public List<Game> GetGames()
    {
        return Load().Games.OrderByDescending(g => g.Points).ToList();
    }

    public List<Duel> GetPendingDuels()
    {
        return Load().Duels.Where(d => !d.IsCompleted).ToList();
    }

    public (Duel? duel, Game? game1, Game? game2) GetRandomPendingDuel()
    {
        var db = Load();
        var pending = db.Duels.Where(d => !d.IsCompleted).ToList();
        if (pending.Count == 0)
            return (null, null, null);

        var duel = pending[_random.Next(pending.Count)];
        var game1 = db.Games.FirstOrDefault(g => g.Id == duel.Game1Id);
        var game2 = db.Games.FirstOrDefault(g => g.Id == duel.Game2Id);
        return (duel, game1, game2);
    }

    public void RecordWinner(Guid duelId, Guid winnerId)
    {
        var db = Load();
        var duel = db.Duels.FirstOrDefault(d => d.Id == duelId);
        if (duel == null) return;

        duel.IsCompleted = true;
        duel.WinnerId = winnerId;

        var winner = db.Games.FirstOrDefault(g => g.Id == winnerId);
        if (winner != null)
            winner.Points++;

        Save(db);
    }

    public void ImportGames(IEnumerable<string> titles)
    {
        var db = Load();

        var newGames = titles
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Where(t => !db.Games.Any(g => string.Equals(g.Title, t, StringComparison.OrdinalIgnoreCase)))
            .Select(t => new Game { Title = t })
            .ToList();

        db.Games.AddRange(newGames);

        // Generate all missing duels (every game vs every other game)
        var allGames = db.Games;
        for (int i = 0; i < allGames.Count; i++)
        {
            for (int j = i + 1; j < allGames.Count; j++)
            {
                var g1 = allGames[i];
                var g2 = allGames[j];
                bool duelExists = db.Duels.Any(d =>
                    (d.Game1Id == g1.Id && d.Game2Id == g2.Id) ||
                    (d.Game1Id == g2.Id && d.Game2Id == g1.Id));

                if (!duelExists)
                {
                    db.Duels.Add(new Duel { Game1Id = g1.Id, Game2Id = g2.Id });
                }
            }
        }

        Save(db);
    }

    public bool HasPendingDuels() => GetPendingDuels().Count > 0;

    public int TotalDuels() => Load().Duels.Count;

    public int CompletedDuels() => Load().Duels.Count(d => d.IsCompleted);
}
