using System.Text.Json;
using BestestGame.Models;

namespace BestestGame.Services;

public class GameService
{
    public sealed record LossDetail(Guid DuelId, Guid WinnerId, string WinnerTitle);
    public sealed record DuelResult(Guid DuelId, Guid WinnerId);

    private readonly string _dbPath;
    private readonly object _databaseLock = new();

    public GameService(IConfiguration configuration, IWebHostEnvironment env)
        => _dbPath = DatabasePath(configuration, env);

    public static string DatabasePath(IConfiguration configuration, IWebHostEnvironment env)
    {
        var configuredPath = configuration["DatabasePath"];
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(env.ContentRootPath, "data.json")
            : Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(configuredPath, env.ContentRootPath);
    }

    /// <summary>Cover pictures live beside the database, so they share its lifetime and survive deployments.</summary>
    public string CoversDirectory => Path.Combine(Path.GetDirectoryName(_dbPath)!, "covers");

    private GameDatabase Load()
    {
        lock (_databaseLock)
        {
            if (!File.Exists(_dbPath))
                return new GameDatabase();

            var json = File.ReadAllText(_dbPath);
            return JsonSerializer.Deserialize<GameDatabase>(json) ?? new GameDatabase();
        }
    }

    private void Save(GameDatabase db)
    {
        lock (_databaseLock)
        {
            var json = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            File.WriteAllText(_dbPath, json);
        }
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
        lock (_databaseLock)
        {
            ArgumentNullException.ThrowIfNull(name);

            var db = Load();
            var tournament = new Tournament { Name = name.Trim() };
            db.Tournaments.Add(tournament);
            db.CurrentTournamentId = tournament.Id;
            Save(db);
            return tournament;
        }
    }

    /// <summary>
    /// Sets the given tournament as the current one.
    /// </summary>
    public void SelectTournament(Guid tournamentId)
    {
        lock (_databaseLock)
        {
            var db = Load();
            var tournament = db.Tournaments.FirstOrDefault(t => t.Id == tournamentId);
            if (tournament is null)
                return;

            db.CurrentTournamentId = tournamentId;
            Save(db);
        }
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
        lock (_databaseLock)
        {
            var tournament = GetCurrentTournament();
            if (tournament is not null)
                RecordWinners(tournament.Id, [new(duelId, winnerId)]);
        }
    }

    /// <summary>
    /// Saves a group of explicit results in one write. Already completed matches
    /// are left alone, so repeated or stale input cannot award duplicate points.
    /// Invalid batches and batches from a different selected tournament do nothing.
    /// </summary>
    public IReadOnlyList<DuelResult> RecordWinners(Guid tournamentId, IEnumerable<DuelResult> results)
    {
        lock (_databaseLock)
        {
            var db = Load();
            var tournament = GetCurrentTournament(db);
            if (tournament?.Id != tournamentId)
                return [];

            var votes = results.Distinct().ToArray();
            var duels = tournament.Duels.ToDictionary(d => d.Id);
            var games = tournament.Games.ToDictionary(g => g.Id);
            if (votes.GroupBy(vote => vote.DuelId).Any(group => group.Count() > 1) ||
                votes.Any(vote => !duels.TryGetValue(vote.DuelId, out var duel) ||
                    (duel.Game1Id != vote.WinnerId && duel.Game2Id != vote.WinnerId) ||
                    !games.ContainsKey(vote.WinnerId)))
                return [];

            var recorded = votes.Where(vote => !duels[vote.DuelId].IsCompleted).ToArray();
            foreach (var vote in recorded)
            {
                var duel = duels[vote.DuelId];
                duel.IsCompleted = true;
                duel.WinnerId = vote.WinnerId;
                games[vote.WinnerId].Points++;
            }

            if (recorded.Length > 0)
                Save(db);
            return recorded;
        }
    }

    /// <summary>
    /// Reverses a complete batch only if its results still match what was saved.
    /// This prevents undo from erasing a later correction to one of its matches.
    /// </summary>
    public bool UndoWinners(Guid tournamentId, IEnumerable<DuelResult> results)
    {
        lock (_databaseLock)
        {
            var db = Load();
            var tournament = GetCurrentTournament(db);
            if (tournament?.Id != tournamentId)
                return false;

            var votes = results.Distinct().ToArray();
            var duels = tournament.Duels.ToDictionary(d => d.Id);
            var games = tournament.Games.ToDictionary(g => g.Id);
            if (votes.Length == 0 || votes.Any(vote =>
                    !duels.TryGetValue(vote.DuelId, out var duel) || !duel.IsCompleted ||
                    duel.WinnerId != vote.WinnerId || !games.ContainsKey(vote.WinnerId)))
                return false;

            foreach (var vote in votes)
            {
                var duel = duels[vote.DuelId];
                duel.IsCompleted = false;
                duel.WinnerId = null;
                games[vote.WinnerId].Points = Math.Max(0, games[vote.WinnerId].Points - 1);
            }

            Save(db);
            return true;
        }
    }

    public int ImportGames(IEnumerable<string> titles)
        => ImportGames(titles.Select(title => new Game { Title = title }));

    public int ImportGames(IEnumerable<Game> games)
    {
        lock (_databaseLock)
        {
            var db = Load();
            var tournament = GetCurrentTournament(db);
            if (tournament is null)
                return 0;

            var existingTitles = tournament.Games.Select(g => g.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newGames = games
                .Where(g => !string.IsNullOrWhiteSpace(g.Title))
                .Where(g => existingTitles.Add(g.Title.Trim()))
                .Select(g => new Game
                {
                    Title = g.Title.Trim(),
                    ReleaseYear = ValidateReleaseYear(g.ReleaseYear),
                    IncludedTitles = CleanIncludedTitles(g.IncludedTitles)
                })
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
            return newGames.Count;
        }
    }

    public bool UpdateGameDetails(Guid gameId, int? releaseYear, IEnumerable<IncludedTitle> includedTitles)
    {
        lock (_databaseLock)
        {
            var db = Load();
            var game = GetCurrentTournament(db)?.Games.FirstOrDefault(g => g.Id == gameId);
            if (game is null)
                return false;

            game.ReleaseYear = ValidateReleaseYear(releaseYear);
            game.IncludedTitles = CleanIncludedTitles(includedTitles);
            Save(db);
            return true;
        }
    }

    private static List<IncludedTitle> CleanIncludedTitles(IEnumerable<IncludedTitle>? titles)
        => (titles ?? []).Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .Select(t => new IncludedTitle { Title = t.Title.Trim(), ReleaseYear = ValidateReleaseYear(t.ReleaseYear) }).ToList();

    private static int? ValidateReleaseYear(int? year)
    {
        if (year is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(year), "Release year must be between 1 and 9999.");
        return year;
    }

    /// <summary>
    /// Removes a game from the current tournament and deletes all duels that include it.
    /// </summary>
    public bool RemoveGame(Guid gameId)
    {
        lock (_databaseLock)
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
            DeleteCoverFile(game.CoverImage);
            return true;
        }
    }

    /// <summary>
    /// Records which stored picture belongs to an entry of the selected tournament, or none.
    /// The picture it replaces is deleted.
    /// </summary>
    public bool SetCover(Guid gameId, string? fileName)
    {
        if (fileName is not null && (fileName.Length == 0 || Path.GetFileName(fileName) != fileName))
            throw new ArgumentException("A cover is a file name inside the covers folder.", nameof(fileName));
        lock (_databaseLock)
        {
            var db = Load();
            var game = GetCurrentTournament(db)?.Games.FirstOrDefault(g => g.Id == gameId);
            if (game is null)
                return false;

            var replaced = game.CoverImage;
            game.CoverImage = fileName;
            Save(db);
            if (replaced != fileName) DeleteCoverFile(replaced);
            return true;
        }
    }

    private void DeleteCoverFile(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName) || Path.GetFileName(fileName) != fileName) return;
        try { File.Delete(Path.Combine(CoversDirectory, fileName)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* A leftover picture is harmless. */ }
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
        lock (_databaseLock)
        {
            var tournament = GetCurrentTournament();
            var duel = tournament?.Duels.FirstOrDefault(d => d.Id == duelId);
            if (duel?.WinnerId is { } winnerId)
                UndoWinners(tournament!.Id, [new(duelId, winnerId)]);
        }
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
        lock (_databaseLock)
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
