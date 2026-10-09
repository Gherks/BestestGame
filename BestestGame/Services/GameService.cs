using BestestGame.Models;
using Microsoft.Data.Sqlite;

namespace BestestGame.Services;

public class GameService
{
    public sealed record LossDetail(Guid DuelId, Guid WinnerId, string WinnerTitle);
    public sealed record DuelResult(Guid DuelId, Guid WinnerId);

    private readonly string _dbPath;
    private readonly GameStore _store;
    private readonly object _databaseLock = new();

    public GameService(IConfiguration configuration, IWebHostEnvironment env)
    {
        _dbPath = DatabasePath(configuration, env);
        _store = new GameStore(_dbPath);
    }

    public static string DatabasePath(IConfiguration configuration, IWebHostEnvironment env)
    {
        var configuredPath = configuration["DatabasePath"];
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(env.ContentRootPath, "data.db")
            : Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(configuredPath, env.ContentRootPath);
    }

    /// <summary>Cover pictures live beside the database, so they share its lifetime and survive deployments.</summary>
    public string CoversDirectory => Path.Combine(Path.GetDirectoryName(_dbPath)!, "covers");

    // One reader or writer at a time, so what a method reads cannot change before it finishes.
    private T Read<T>(Func<SqliteConnection, T> query)
    {
        lock (_databaseLock)
        {
            using var connection = _store.Open();
            return query(connection);
        }
    }

    // A change is saved completely or, if it fails part of the way, not at all.
    private T Write<T>(Func<SqliteConnection, T> change)
    {
        lock (_databaseLock)
        {
            using var connection = _store.Open();
            using var transaction = connection.BeginTransaction();
            var result = change(connection);
            transaction.Commit();
            return result;
        }
    }

    /// <summary>
    /// Returns everything stored, in the shape of the JSON file that earlier versions saved.
    /// </summary>
    public GameDatabase Export() => Read(GameStore.Export);

    /// <summary>
    /// Fills an empty database with whole tournaments, keeping their identities, order, scores and results.
    /// </summary>
    public void Import(GameDatabase database)
        => Write(connection =>
        {
            if (connection.Count("SELECT COUNT(*) FROM tournaments") > 0)
                throw new InvalidOperationException("Data can only be imported into an empty database.");
            GameStore.Import(connection, database);
            return true;
        });

    /// <summary>
    /// Returns all tournaments ordered by name.
    /// </summary>
    public List<Tournament> GetTournaments()
    {
        return Read(connection => GameStore.ReadTournaments(connection)).OrderBy(t => t.Name).ToList();
    }

    /// <summary>
    /// Returns the currently selected tournament, or null if none is selected.
    /// </summary>
    public Tournament? GetCurrentTournament()
    {
        return Read(connection => GameStore.CurrentTournamentId(connection) is { } tournamentId
            ? GameStore.ReadTournaments(connection, tournamentId).FirstOrDefault()
            : null);
    }

    /// <summary>
    /// Creates a new tournament and sets it as the current one.
    /// </summary>
    public Tournament CreateTournament(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var tournament = new Tournament { Name = name.Trim() };
        return Write(connection =>
        {
            connection.Execute(
                "INSERT INTO tournaments (id, position, name) VALUES ($id, (SELECT COALESCE(MAX(position), -1) + 1 FROM tournaments), $name)",
                ("$id", tournament.Id), ("$name", tournament.Name));
            connection.Execute("UPDATE app_state SET current_tournament_id = $id WHERE id = 1", ("$id", tournament.Id));
            return tournament;
        });
    }

    /// <summary>
    /// Sets the given tournament as the current one.
    /// </summary>
    public void SelectTournament(Guid tournamentId)
    {
        Write(connection => connection.Execute(
            "UPDATE app_state SET current_tournament_id = $id WHERE id = 1 AND EXISTS (SELECT 1 FROM tournaments WHERE id = $id)",
            ("$id", tournamentId)));
    }

    /// <summary>
    /// Renames a tournament. Its games, results and selection are untouched.
    /// </summary>
    public bool RenameTournament(Guid tournamentId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return Write(connection => connection.Execute(
            "UPDATE tournaments SET name = $name WHERE id = $id", ("$name", name.Trim()), ("$id", tournamentId)) > 0);
    }

    /// <summary>
    /// Deletes a tournament with its games, matchups, results and cover pictures.
    /// Deleting the current tournament leaves none selected.
    /// </summary>
    public bool DeleteTournament(Guid tournamentId)
    {
        lock (_databaseLock)
        {
            var (deleted, covers) = Write(connection =>
            {
                var pictures = connection.Query("SELECT cover_image FROM games WHERE tournament_id = $id",
                    row => row.TextOrNull(0), ("$id", tournamentId));
                // Its games, their included titles and its duels go with it, and the selection is cleared if it was selected.
                return (connection.Execute("DELETE FROM tournaments WHERE id = $id", ("$id", tournamentId)) > 0, pictures);
            });
            if (!deleted)
                return false;

            foreach (var cover in covers)
                DeleteCoverFile(cover);
            return true;
        }
    }

    public List<Game> GetGames()
    {
        return Read(connection => GameStore.CurrentTournamentId(connection) is { } tournamentId
            ? GameStore.ReadGames(connection, tournamentId).OrderByDescending(g => g.Points).ToList()
            : []);
    }

    public List<Duel> GetPendingDuels()
    {
        return Read(connection => GameStore.CurrentTournamentId(connection) is { } tournamentId
            ? GameStore.ReadDuels(connection, tournamentId, "winner_id IS NULL")
            : []);
    }

    /// <summary>
    /// Returns a specific duel and its two games by duel ID.
    /// </summary>
    public (Duel? duel, Game? game1, Game? game2) GetDuel(Guid duelId)
    {
        return Read<(Duel?, Game?, Game?)>(connection =>
        {
            if (GameStore.CurrentTournamentId(connection) is not { } tournamentId ||
                !FindDuels(connection, tournamentId, [duelId]).TryGetValue(duelId, out var duel))
                return (null, null, null);

            var games = GameStore.ReadGames(connection, tournamentId);
            return (duel, games.FirstOrDefault(g => g.Id == duel.Game1Id), games.FirstOrDefault(g => g.Id == duel.Game2Id));
        });
    }

    private static Dictionary<Guid, Duel> FindDuels(SqliteConnection connection, Guid tournamentId, IEnumerable<Guid> duelIds)
    {
        using var find = connection.Prepare(
            "SELECT id, game1_id, game2_id, winner_id FROM duels WHERE tournament_id = $tournament AND id = $id", "$tournament", "$id");
        var duels = new Dictionary<Guid, Duel>();
        foreach (var duelId in duelIds.Distinct())
            foreach (var duel in find.With(tournamentId, duelId).ReadAll(GameStore.ReadDuel))
                duels[duel.Id] = duel;
        return duels;
    }

    public void RecordWinner(Guid duelId, Guid winnerId)
    {
        lock (_databaseLock)
        {
            if (Read(GameStore.CurrentTournamentId) is { } tournamentId)
                RecordWinners(tournamentId, [new(duelId, winnerId)]);
        }
    }

    /// <summary>
    /// Saves a group of explicit results in one write. Already completed matches
    /// are left alone, so repeated or stale input cannot award duplicate points.
    /// Invalid batches and batches from a different selected tournament do nothing.
    /// </summary>
    public IReadOnlyList<DuelResult> RecordWinners(Guid tournamentId, IEnumerable<DuelResult> results)
    {
        return Write<IReadOnlyList<DuelResult>>(connection =>
        {
            if (GameStore.CurrentTournamentId(connection) != tournamentId)
                return [];

            var votes = results.Distinct().ToArray();
            var duels = FindDuels(connection, tournamentId, votes.Select(vote => vote.DuelId));
            if (votes.GroupBy(vote => vote.DuelId).Any(group => group.Count() > 1) ||
                votes.Any(vote => !duels.TryGetValue(vote.DuelId, out var duel) ||
                    (duel.Game1Id != vote.WinnerId && duel.Game2Id != vote.WinnerId)))
                return [];

            var recorded = votes.Where(vote => !duels[vote.DuelId].IsCompleted).ToArray();
            using var setWinner = connection.Prepare("UPDATE duels SET winner_id = $winner WHERE id = $id", "$winner", "$id");
            using var addPoint = connection.Prepare("UPDATE games SET points = points + 1 WHERE id = $id", "$id");
            foreach (var vote in recorded)
            {
                setWinner.Run(vote.WinnerId, vote.DuelId);
                addPoint.Run(vote.WinnerId);
            }
            return recorded;
        });
    }

    /// <summary>
    /// Reverses a complete batch only if its results still match what was saved.
    /// This prevents undo from erasing a later correction to one of its matches.
    /// </summary>
    public bool UndoWinners(Guid tournamentId, IEnumerable<DuelResult> results)
    {
        return Write(connection =>
        {
            if (GameStore.CurrentTournamentId(connection) != tournamentId)
                return false;

            var votes = results.Distinct().ToArray();
            var duels = FindDuels(connection, tournamentId, votes.Select(vote => vote.DuelId));
            if (votes.Length == 0 || votes.Any(vote =>
                    !duels.TryGetValue(vote.DuelId, out var duel) || !duel.IsCompleted || duel.WinnerId != vote.WinnerId))
                return false;

            using var clearWinner = connection.Prepare("UPDATE duels SET winner_id = NULL WHERE id = $id", "$id");
            using var removePoint = connection.Prepare("UPDATE games SET points = MAX(0, points - 1) WHERE id = $id", "$id");
            foreach (var vote in votes)
            {
                clearWinner.Run(vote.DuelId);
                removePoint.Run(vote.WinnerId);
            }
            return true;
        });
    }

    public int ImportGames(IEnumerable<string> titles)
        => ImportGames(titles.Select(title => new Game { Title = title }));

    public int ImportGames(IEnumerable<Game> games)
    {
        return Write(connection =>
        {
            if (GameStore.CurrentTournamentId(connection) is not { } tournamentId)
                return 0;

            var existing = connection.Query("SELECT id, title FROM games WHERE tournament_id = $tournament ORDER BY position",
                row => (Id: row.Id(0), Title: row.GetString(1)), ("$tournament", tournamentId));
            var existingTitles = existing.Select(g => g.Title)
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

            using var rows = new GameStore.Rows(connection);
            var gamePosition = connection.Count(
                "SELECT COALESCE(MAX(position), -1) + 1 FROM games WHERE tournament_id = $tournament", ("$tournament", tournamentId));
            foreach (var game in newGames)
                rows.AddGame(tournamentId, gamePosition++, game);

            // Generate all missing duels (every game vs every other game)
            var allGames = existing.Select(g => g.Id).Concat(newGames.Select(g => g.Id)).ToList();
            var paired = connection.Query("SELECT game1_id, game2_id FROM duels WHERE tournament_id = $tournament",
                row => (row.Id(0), row.Id(1)), ("$tournament", tournamentId)).ToHashSet();
            var duelPosition = connection.Count(
                "SELECT COALESCE(MAX(position), -1) + 1 FROM duels WHERE tournament_id = $tournament", ("$tournament", tournamentId));
            for (int i = 0; i < allGames.Count; i++)
            {
                for (int j = i + 1; j < allGames.Count; j++)
                {
                    var g1 = allGames[i];
                    var g2 = allGames[j];
                    if (!paired.Contains((g1, g2)) && !paired.Contains((g2, g1)))
                    {
                        rows.AddDuel(tournamentId, duelPosition++, new Duel { Game1Id = g1, Game2Id = g2 });
                    }
                }
            }

            return newGames.Count;
        });
    }

    public bool UpdateGameDetails(Guid gameId, int? releaseYear, IEnumerable<IncludedTitle> includedTitles)
    {
        return Write(connection =>
        {
            if (!IsInCurrentTournament(connection, gameId))
                return false;

            var year = ValidateReleaseYear(releaseYear);
            var titles = CleanIncludedTitles(includedTitles);
            connection.Execute("UPDATE games SET release_year = $year WHERE id = $id", ("$year", year), ("$id", gameId));
            connection.Execute("DELETE FROM included_titles WHERE game_id = $id", ("$id", gameId));
            using var rows = new GameStore.Rows(connection);
            rows.AddIncludedTitles(gameId, titles);
            return true;
        });
    }

    private static bool IsInCurrentTournament(SqliteConnection connection, Guid gameId)
        => connection.Count(
            "SELECT COUNT(*) FROM games WHERE id = $id AND tournament_id = (SELECT current_tournament_id FROM app_state WHERE id = 1)",
            ("$id", gameId)) > 0;

    /// <summary>
    /// Changes an entry's title in the current tournament. Its ID, results and points stay as they are.
    /// As when importing, two entries cannot share a title.
    /// </summary>
    public bool RenameGame(Guid gameId, string title)
    {
        return Write(connection =>
        {
            var name = title?.Trim();
            if (string.IsNullOrEmpty(name) || GameStore.CurrentTournamentId(connection) is not { } tournamentId)
                return false;

            // Compared here rather than by the database, whose case-insensitive matching only covers ASCII letters.
            var titles = connection.Query("SELECT id, title FROM games WHERE tournament_id = $tournament",
                row => (Id: row.Id(0), Title: row.GetString(1)), ("$tournament", tournamentId));
            if (!titles.Any(game => game.Id == gameId) || titles.Any(other =>
                    other.Id != gameId && string.Equals(other.Title, name, StringComparison.OrdinalIgnoreCase)))
                return false;

            connection.Execute("UPDATE games SET title = $title WHERE id = $id", ("$title", name), ("$id", gameId));
            return true;
        });
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
            var (removed, cover) = Write<(bool, string?)>(connection =>
            {
                if (GameStore.CurrentTournamentId(connection) is not { } tournamentId)
                    return (false, null);

                var covers = connection.Query("SELECT cover_image FROM games WHERE tournament_id = $tournament AND id = $id",
                    row => row.TextOrNull(0), ("$tournament", tournamentId), ("$id", gameId));
                if (covers.Count == 0)
                    return (false, null);

                // Its included titles and duels go with it.
                connection.Execute("DELETE FROM games WHERE id = $id", ("$id", gameId));
                RecalculatePoints(connection, tournamentId);
                return (true, covers[0]);
            });
            if (removed)
                DeleteCoverFile(cover);
            return removed;
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
            var (found, replaced) = Write<(bool, string?)>(connection =>
            {
                if (!IsInCurrentTournament(connection, gameId))
                    return (false, null);

                var previous = connection.Query("SELECT cover_image FROM games WHERE id = $id", row => row.TextOrNull(0), ("$id", gameId))[0];
                connection.Execute("UPDATE games SET cover_image = $cover WHERE id = $id", ("$cover", fileName), ("$id", gameId));
                return (true, previous);
            });
            if (found && replaced != fileName) DeleteCoverFile(replaced);
            return found;
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
        return Read(connection => connection.Count(
            "SELECT COUNT(*) FROM duels WHERE tournament_id = (SELECT current_tournament_id FROM app_state WHERE id = 1)"));
    }

    public int CompletedDuels()
    {
        return Read(connection => connection.Count(
            "SELECT COUNT(*) FROM duels WHERE winner_id IS NOT NULL AND tournament_id = (SELECT current_tournament_id FROM app_state WHERE id = 1)"));
    }

    /// <summary>
    /// Undoes a completed duel by resetting its state and decrementing the winner's points.
    /// </summary>
    public void UndoMatch(Guid duelId)
    {
        lock (_databaseLock)
        {
            var saved = Read<(Guid TournamentId, Guid WinnerId)?>(connection =>
                GameStore.CurrentTournamentId(connection) is { } tournamentId &&
                FindDuels(connection, tournamentId, [duelId]).GetValueOrDefault(duelId)?.WinnerId is { } winnerId
                    ? (tournamentId, winnerId)
                    : null);
            if (saved is { } result)
                UndoWinners(result.TournamentId, [new(duelId, result.WinnerId)]);
        }
    }

    // The current tournament's entries in the order they were added, with its completed duels.
    private (List<Game> Games, List<Duel> Completed) ReadResults()
    {
        return Read(connection => GameStore.CurrentTournamentId(connection) is { } tournamentId
            ? (GameStore.ReadGames(connection, tournamentId), GameStore.ReadDuels(connection, tournamentId, "winner_id IS NOT NULL"))
            : (new List<Game>(), new List<Duel>()));
    }

    /// <summary>
    /// Returns a dictionary mapping each game ID to detailed completed losses.
    /// </summary>
    public Dictionary<Guid, List<LossDetail>> GetGamesPickedOverDetails()
    {
        var (games, completed) = ReadResults();
        var gamesById = games.ToDictionary(g => g.Id);
        var result = games.ToDictionary(g => g.Id, _ => new List<LossDetail>());

        foreach (var duel in completed)
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
        var (games, completed) = ReadResults();
        var gamesById = games.ToDictionary(g => g.Id);
        var result = games.ToDictionary(g => g.Id, _ => new List<LossDetail>());

        foreach (var duel in completed)
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
        return Write(connection =>
        {
            if (GameStore.CurrentTournamentId(connection) is not { } tournamentId ||
                !FindDuels(connection, tournamentId, [duelId]).TryGetValue(duelId, out var duel) ||
                duel.WinnerId is not { } previousWinnerId)
                return false;

            if ((duel.Game1Id != winnerId && duel.Game2Id != winnerId) || previousWinnerId == winnerId)
                return false;

            connection.Execute("UPDATE games SET points = MAX(0, points - 1) WHERE id = $id", ("$id", previousWinnerId));
            connection.Execute("UPDATE games SET points = points + 1 WHERE id = $id", ("$id", winnerId));
            connection.Execute("UPDATE duels SET winner_id = $winner WHERE id = $id", ("$winner", winnerId), ("$id", duelId));
            return true;
        });
    }

    /// <summary>
    /// Returns a dictionary mapping each game ID to the number of completed duels it participated in.
    /// </summary>
    public Dictionary<Guid, int> GetMatchesPlayedPerGame()
    {
        var (games, completed) = ReadResults();
        var counts = new Dictionary<Guid, int>();
        foreach (var game in games)
        {
            counts[game.Id] = 0;
        }

        foreach (var duel in completed)
        {
            if (counts.ContainsKey(duel.Game1Id))
                counts[duel.Game1Id]++;
            if (counts.ContainsKey(duel.Game2Id))
                counts[duel.Game2Id]++;
        }

        return counts;
    }

    private static void RecalculatePoints(SqliteConnection connection, Guid tournamentId)
    {
        connection.Execute(
            """
            UPDATE games SET points = (SELECT COUNT(*) FROM duels
                                       WHERE duels.tournament_id = games.tournament_id AND duels.winner_id = games.id)
            WHERE tournament_id = $tournament
            """, ("$tournament", tournamentId));
    }
}
