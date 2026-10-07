using System.Text.Json;
using BestestGame.Components;
using BestestGame.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;

static class TournamentNavigationChecks
{
    public static void Run()
    {
        const string root = "https://example.test/";
        var focus = Guid.NewGuid();
        Check(Switch(root, $"vote?focus={focus}&year=2007") == root + "vote", "Switching focused Voting removes focus and its year instead of redirecting to Rankings");
        Check(Switch(root, "rankings?year=2007&focus=old") == root + "rankings?year=2007", "Rankings retains its release year while clearing old focus");
        Check(Switch(root, "goty?year=1900&focus=old") == root + "goty?year=1900", "GOTY retains a year even when the next tournament has no entries for it");
        Check(Switch(root, "import?focus=old&year=2007") == root + "import", "Games retains its route without entry or year context");
        Check(Switch(root, "tournaments?focus=old&year=2007") == root + "tournaments" &&
            Switch(root, "?focus=old&year=2007") == root, "Tournaments and Home discard irrelevant entry/year queries");
        Check(Switch(root, "vote") == root + "vote", "Ordinary Voting retains its destination when switching");
        Check(Switch(root, "rankings?year=2007&tab=sample#results") == root + "rankings?year=2007&tab=sample#results", "Unrelated query values and fragments are preserved");
        Check(Switch("https://example.test/best/", "Rankings/?Year=2007&FOCUS=old") == "https://example.test/best/Rankings/?Year=2007", "Path bases, trailing slash and query casing are handled");
        Check(!new Uri(Switch(root, "vote?focus=one&focus=two&year=2007&year=2010")).Query.Any(), "Repeated old focus/year values are removed");

        var directory = Path.Combine(Path.GetTempPath(), $"bestestgame-navigation-{Guid.NewGuid()}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "data.json");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DatabasePath"] = path }).Build();
            var service = new GameService(configuration, null!);
            var first = service.CreateTournament("First");
            service.ImportGames(["A", "B"]);
            var duel = service.GetPendingDuels().Single();
            service.RecordWinner(duel.Id, duel.Game1Id);
            var second = service.CreateTournament("Second");
            service.ImportGames(["Other"]);
            var before = JsonSerializer.Serialize(service.GetTournaments());
            service.SelectTournament(first.Id);
            Check(service.GetCurrentTournament()!.Id == first.Id && JsonSerializer.Serialize(service.GetTournaments()) == before,
                "Selection preserves every tournament's games, points and duels");
            Check(new GameService(configuration, null!).GetCurrentTournament()!.Id == first.Id, "The active selection stays application-wide and persisted");
            service.SelectTournament(second.Id);
            Check(service.GetGames().Single().Title == "Other" && service.GetPendingDuels().Count == 0, "Switching reads only the selected tournament's entries and matchups");
            var saved = File.ReadAllText(path);
            service.SelectTournament(Guid.NewGuid());
            Check(File.ReadAllText(path) == saved, "An unavailable tournament selection cannot overwrite existing data");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Switch(string root, string path) => TournamentNavigation.SwitchDestination(new CheckNavigation(root, root + path));
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private sealed class CheckNavigation : NavigationManager
    {
        public CheckNavigation(string root, string uri) => Initialize(root, uri);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }
}
