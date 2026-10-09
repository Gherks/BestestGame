using System.Text.Json;
using BestestGame.Models;
using Microsoft.Data.Sqlite;

namespace BestestGame.Services;

/// <summary>
/// The SQLite file that holds every tournament: opening it, keeping its tables up to date, and moving a
/// whole database in or out in the shape of the JSON file that was used before.
/// </summary>
internal sealed class GameStore
{
    // Each entry upgrades the tables by one version; the file's user_version records how many have run.
    // Entries are only ever added, never edited, so every existing file can be brought up to date.
    private static readonly string[] SchemaVersions =
    [
        """
        CREATE TABLE tournaments (
            id       TEXT PRIMARY KEY,
            position INTEGER NOT NULL,
            name     TEXT NOT NULL
        );
        CREATE TABLE app_state (
            id                    INTEGER PRIMARY KEY CHECK (id = 1),
            current_tournament_id TEXT REFERENCES tournaments (id) ON DELETE SET NULL
        );
        INSERT INTO app_state (id) VALUES (1);
        CREATE TABLE games (
            id            TEXT PRIMARY KEY,
            tournament_id TEXT NOT NULL REFERENCES tournaments (id) ON DELETE CASCADE,
            position      INTEGER NOT NULL,
            title         TEXT NOT NULL,
            release_year  INTEGER CHECK (release_year BETWEEN 1 AND 9999),
            points        INTEGER NOT NULL DEFAULT 0,
            cover_image   TEXT,
            UNIQUE (tournament_id, id)
        );
        CREATE INDEX games_by_tournament ON games (tournament_id, position);
        CREATE TABLE included_titles (
            game_id      TEXT NOT NULL REFERENCES games (id) ON DELETE CASCADE,
            position     INTEGER NOT NULL,
            title        TEXT NOT NULL,
            release_year INTEGER CHECK (release_year BETWEEN 1 AND 9999),
            PRIMARY KEY (game_id, position)
        ) WITHOUT ROWID;
        -- A duel is completed exactly when it has a winner, and both of its games belong to its tournament.
        CREATE TABLE duels (
            id            TEXT PRIMARY KEY,
            tournament_id TEXT NOT NULL REFERENCES tournaments (id) ON DELETE CASCADE,
            position      INTEGER NOT NULL,
            game1_id      TEXT NOT NULL,
            game2_id      TEXT NOT NULL,
            winner_id     TEXT,
            CHECK (game1_id <> game2_id),
            CHECK (winner_id IS NULL OR winner_id IN (game1_id, game2_id)),
            FOREIGN KEY (tournament_id, game1_id) REFERENCES games (tournament_id, id) ON DELETE CASCADE,
            FOREIGN KEY (tournament_id, game2_id) REFERENCES games (tournament_id, id) ON DELETE CASCADE
        );
        CREATE INDEX duels_by_tournament ON duels (tournament_id, position);
        CREATE INDEX duels_by_game1 ON duels (tournament_id, game1_id);
        CREATE INDEX duels_by_game2 ON duels (tournament_id, game2_id);
        """
    ];

    private readonly string _connectionString;

    public GameStore(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"DatabasePath must name the SQLite database, such as data.db, rather than the earlier JSON file: {path}");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var legacyPath = Path.ChangeExtension(path, ".json");
        if (!File.Exists(path) && File.Exists(legacyPath))
            MoveLegacyFileIn(legacyPath, path);

        _connectionString = ConnectionString(path, pooling: true);
        using var connection = Open();
        // Lets backups and the development copy read the file while the application is writing to it.
        connection.Execute("PRAGMA journal_mode = WAL");
        ApplySchema(connection, path);
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string ConnectionString(string path, bool pooling) => new SqliteConnectionStringBuilder
    {
        DataSource = path, ForeignKeys = true, Pooling = pooling
    }.ToString();

    private static void ApplySchema(SqliteConnection connection, string path)
    {
        var version = connection.Count("PRAGMA user_version");
        if (version > SchemaVersions.Length)
            throw new InvalidOperationException($"{path} was written by a newer version of this application.");

        for (var next = version; next < SchemaVersions.Length; next++)
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(SchemaVersions[next]);
            connection.Execute($"PRAGMA user_version = {next + 1}");
            transaction.Commit();
        }
    }

    /// <summary>
    /// Builds the database from the JSON file an earlier version saved. The new file only takes its place
    /// once reading it back gives exactly what the JSON file holds; the JSON file itself is never changed.
    /// </summary>
    private static void MoveLegacyFileIn(string legacyPath, string path)
    {
        var buildingPath = path + ".importing";
        DeleteDatabaseFiles(buildingPath);
        try
        {
            var legacy = JsonSerializer.Deserialize<GameDatabase>(File.ReadAllText(legacyPath)) ?? new GameDatabase();
            // Unpooled, so the file is closed and complete before it is renamed.
            using (var connection = new SqliteConnection(ConnectionString(buildingPath, pooling: false)))
            {
                connection.Open();
                ApplySchema(connection, buildingPath);
                using (var transaction = connection.BeginTransaction())
                {
                    Import(connection, legacy);
                    transaction.Commit();
                }
                if (JsonSerializer.Serialize(Export(connection)) != JsonSerializer.Serialize(legacy))
                    throw new InvalidOperationException("Reading the new database back did not give the same data.");
            }
            File.Move(buildingPath, path);
            Console.WriteLine($"Moved {legacyPath} into {path}: {legacy.Tournaments.Count} tournaments, " +
                $"{legacy.Tournaments.Sum(tournament => tournament.Games.Count)} entries, " +
                $"{legacy.Tournaments.Sum(tournament => tournament.Duels.Count)} matchups. The JSON file is no longer read.");
        }
        catch (Exception exception)
        {
            DeleteDatabaseFiles(buildingPath);
            throw new InvalidOperationException(
                $"Could not move {legacyPath} into a SQLite database. The file was left as it is. {exception.Message}", exception);
        }
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            File.Delete(path + suffix);
    }

    public static Guid? CurrentTournamentId(SqliteConnection connection)
        => connection.Query("SELECT current_tournament_id FROM app_state WHERE id = 1", row => row.IdOrNull(0)).FirstOrDefault();

    public static List<Tournament> ReadTournaments(SqliteConnection connection, Guid? only = null)
    {
        var tournaments = connection.Query(
            $"SELECT id, name FROM tournaments{(only is null ? "" : " WHERE id = $only")} ORDER BY position",
            row => new Tournament { Id = row.Id(0), Name = row.GetString(1) }, only is null ? [] : [("$only", only)]);
        foreach (var tournament in tournaments)
        {
            tournament.Games = ReadGames(connection, tournament.Id);
            tournament.Duels = ReadDuels(connection, tournament.Id);
        }
        return tournaments;
    }

    /// <summary>A tournament's entries in the order they were added.</summary>
    public static List<Game> ReadGames(SqliteConnection connection, Guid tournamentId)
    {
        var games = connection.Query(
            "SELECT id, title, release_year, points, cover_image FROM games WHERE tournament_id = $tournament ORDER BY position",
            row => new Game
            {
                Id = row.Id(0), Title = row.GetString(1), ReleaseYear = row.IntOrNull(2), Points = row.GetInt32(3), CoverImage = row.TextOrNull(4)
            }, ("$tournament", tournamentId));
        var byId = games.ToDictionary(game => game.Id);
        var included = connection.Query(
            """
            SELECT included.game_id, included.title, included.release_year
            FROM included_titles included JOIN games ON games.id = included.game_id
            WHERE games.tournament_id = $tournament ORDER BY included.game_id, included.position
            """,
            row => (GameId: row.Id(0), Title: new IncludedTitle { Title = row.GetString(1), ReleaseYear = row.IntOrNull(2) }),
            ("$tournament", tournamentId));
        foreach (var (gameId, title) in included)
            byId[gameId].IncludedTitles.Add(title);
        return games;
    }

    /// <summary>A tournament's duels in the order they were created, optionally only those matching a condition.</summary>
    public static List<Duel> ReadDuels(SqliteConnection connection, Guid tournamentId, string? condition = null)
        => connection.Query(
            $"SELECT id, game1_id, game2_id, winner_id FROM duels WHERE tournament_id = $tournament{(condition is null ? "" : $" AND {condition}")} ORDER BY position",
            ReadDuel, ("$tournament", tournamentId));

    public static Duel ReadDuel(SqliteDataReader row)
    {
        var winnerId = row.IdOrNull(3);
        return new Duel { Id = row.Id(0), Game1Id = row.Id(1), Game2Id = row.Id(2), WinnerId = winnerId, IsCompleted = winnerId is not null };
    }

    /// <summary>Everything stored, in the shape of the earlier JSON file.</summary>
    public static GameDatabase Export(SqliteConnection connection)
        => new() { Tournaments = ReadTournaments(connection), CurrentTournamentId = CurrentTournamentId(connection) };

    /// <summary>Adds whole tournaments to an empty database, keeping their identities, order, scores and results.</summary>
    public static void Import(SqliteConnection connection, GameDatabase data)
    {
        using var rows = new Rows(connection);
        using var addTournament = connection.Prepare(
            "INSERT INTO tournaments (id, position, name) VALUES ($id, $position, $name)", "$id", "$position", "$name");
        for (var index = 0; index < data.Tournaments.Count; index++)
        {
            var tournament = data.Tournaments[index];
            addTournament.Run(tournament.Id, index, tournament.Name);
            for (var position = 0; position < tournament.Games.Count; position++)
                rows.AddGame(tournament.Id, position, tournament.Games[position]);
            for (var position = 0; position < tournament.Duels.Count; position++)
                rows.AddDuel(tournament.Id, position, tournament.Duels[position]);
        }
        connection.Execute("UPDATE app_state SET current_tournament_id = $id WHERE id = 1", ("$id", data.CurrentTournamentId));
    }

    /// <summary>Insert statements that are prepared once and reused for every row of a larger write.</summary>
    public sealed class Rows(SqliteConnection connection) : IDisposable
    {
        private readonly SqliteCommand _game = connection.Prepare(
            """
            INSERT INTO games (id, tournament_id, position, title, release_year, points, cover_image)
            VALUES ($id, $tournament, $position, $title, $year, $points, $cover)
            """, "$id", "$tournament", "$position", "$title", "$year", "$points", "$cover");
        private readonly SqliteCommand _includedTitle = connection.Prepare(
            "INSERT INTO included_titles (game_id, position, title, release_year) VALUES ($game, $position, $title, $year)",
            "$game", "$position", "$title", "$year");
        private readonly SqliteCommand _duel = connection.Prepare(
            """
            INSERT INTO duels (id, tournament_id, position, game1_id, game2_id, winner_id)
            VALUES ($id, $tournament, $position, $game1, $game2, $winner)
            """, "$id", "$tournament", "$position", "$game1", "$game2", "$winner");

        public void AddGame(Guid tournamentId, int position, Game game)
        {
            _game.Run(game.Id, tournamentId, position, game.Title, game.ReleaseYear, game.Points, game.CoverImage);
            AddIncludedTitles(game.Id, game.IncludedTitles);
        }

        public void AddIncludedTitles(Guid gameId, IReadOnlyList<IncludedTitle> titles)
        {
            for (var position = 0; position < titles.Count; position++)
                _includedTitle.Run(gameId, position, titles[position].Title, titles[position].ReleaseYear);
        }

        public void AddDuel(Guid tournamentId, int position, Duel duel)
        {
            if (duel.IsCompleted != duel.WinnerId.HasValue)
                throw new InvalidOperationException($"Duel {duel.Id} must be completed exactly when it has a winner.");
            _duel.Run(duel.Id, tournamentId, position, duel.Game1Id, duel.Game2Id, duel.WinnerId);
        }

        public void Dispose()
        {
            _game.Dispose();
            _includedTitle.Dispose();
            _duel.Dispose();
        }
    }
}

internal static class SqliteExtensions
{
    // IDs are stored as lowercase text, the way the earlier JSON file wrote them.
    private static object Stored(object? value) => value switch { null => DBNull.Value, Guid id => id.ToString(), _ => value };

    /// <summary>A statement to run repeatedly, with its values given in the order of the named parameters.</summary>
    public static SqliteCommand Prepare(this SqliteConnection connection, string sql, params string[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var name in parameters)
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        return command;
    }

    public static SqliteCommand With(this SqliteCommand command, params object?[] values)
    {
        for (var index = 0; index < values.Length; index++)
            command.Parameters[index].Value = Stored(values[index]);
        return command;
    }

    public static int Run(this SqliteCommand command, params object?[] values) => command.With(values).ExecuteNonQuery();

    private static SqliteCommand Command(SqliteConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, Stored(value));
        return command;
    }

    /// <summary>Runs a statement and returns how many rows it changed.</summary>
    public static int Execute(this SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteNonQuery();
    }

    public static int Count(this SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static List<T> Query<T>(this SqliteConnection connection, string sql, Func<SqliteDataReader, T> read,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return command.ReadAll(read);
    }

    public static List<T> ReadAll<T>(this SqliteCommand command, Func<SqliteDataReader, T> read)
    {
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(read(reader));
        return rows;
    }

    public static Guid Id(this SqliteDataReader row, int column) => Guid.Parse(row.GetString(column));
    public static Guid? IdOrNull(this SqliteDataReader row, int column) => row.IsDBNull(column) ? null : row.Id(column);
    public static int? IntOrNull(this SqliteDataReader row, int column) => row.IsDBNull(column) ? null : row.GetInt32(column);
    public static string? TextOrNull(this SqliteDataReader row, int column) => row.IsDBNull(column) ? null : row.GetString(column);
}
