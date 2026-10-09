using System.Text.Json;
using BestestGame.Models;
using BestestGame.Services;
using Microsoft.Extensions.Configuration;

// Every check gets its own database file; none of them depends on how the service stores its data.
static class CheckData
{
    public const string Extension = ".db";

    public static string PathFor(string directory, string name) => Path.Combine(directory, name + Extension);

    public static GameService Open(string path) => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["DatabasePath"] = path }).Build(), null!);

    public static GameService Create(string directory, string name, GameDatabase? seed = null)
    {
        var service = Open(PathFor(directory, name));
        if (seed is not null) service.Import(seed);
        return service;
    }

    /// <summary>A service that starts from a data file written by an earlier version of the application.</summary>
    public static GameService FromLegacyFile(string directory, string name, string json)
    {
        File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        return Open(PathFor(directory, name));
    }

    /// <summary>Everything the service has stored, for checking that an action changed nothing.</summary>
    public static string Snapshot(GameService service) => JsonSerializer.Serialize(service.Export());
}
