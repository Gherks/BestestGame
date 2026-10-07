using System.Text.Json;
using BestestGame.Components;
using BestestGame.Models;
using Microsoft.JSInterop;

static class RankingsNavigationChecks
{
    public static async Task RunAsync()
    {
        var entry = Guid.NewGuid();
        Check(VotingRoutes.LegacyDestination(null, null) is null, "Ordinary voting does not redirect");
        Check(VotingRoutes.LegacyDestination(2007, null) == "/rankings?year=2007", "Legacy year-only links reach filtered Rankings");
        Check(VotingRoutes.LegacyDestination(null, entry) is null && VotingRoutes.LegacyDestination(2007, entry) is null,
            "Focused links take precedence over year redirects");
        Check(VotingRoutes.Rankings() == "/rankings" && VotingRoutes.Rankings(2007) == "/rankings?year=2007",
            "Rankings links retain the optional year");
        Check(VotingRoutes.Focus(entry, 2007) == $"/vote?focus={entry}&year=2007", "Rankings completion links retain focus and year");
        Check(VotingRoutes.Focus(null, 2007) == "/vote", "All matchups reaches ordinary voting rather than the legacy redirect");
        Check(VotingRoutes.LegacyDestination(2007, null, "goty?year=2007") is null,
            "Returning to GOTY cannot trigger the departing Voting component's legacy redirect");
        Check(VotingRoutes.LegacyDestination(2007, null, "rankings?year=2007") is null,
            "Returning to Rankings cannot trigger a stale Voting redirect");
        Check(VotingRoutes.LegacyDestination(2007, null, "/VoTe/?year=2007#matchups") == "/rankings?year=2007",
            "Actual Voting URLs retain the legacy redirect across query, fragment, slash and casing");
        Check(VotingRoutes.LegacyDestination(2007, entry, "vote?focus=" + entry + "&year=2007") is null,
            "Path-scoped legacy handling preserves focused/year Voting");
        Check(VotingRoutes.LegacyDestination(null, null, "vote") is null &&
              VotingRoutes.LegacyDestination(2007, null, "import") is null,
            "Ordinary Voting and unrelated destinations do not redirect");

        var game = new Game { Title = "Prey (2006)", ReleaseYear = 2006, Points = 7 };
        Check(EntryTitles.GetTitleDisplay(game).Title == "Prey (2006)", "Extraction retains a stored year suffix only once");
        var legacy = EntryTitles.GetTitleDisplay(new Game { Title = "Legacy (First, Second)" });
        Check(legacy.Title == "Legacy" && legacy.Subtitle == "Legacy First, Legacy Second", "Legacy collection labels survive extraction");
        game.IncludedTitles = [new() { Title = "Separate release", ReleaseYear = 2010 }, new() { Title = "Undated release" }];
        var before = JsonSerializer.Serialize(game);
        var display = EntryTitles.GetTitleDisplay(game);
        Check(display.Title == "Prey (2006)" && display.Subtitle == "Separate release (2010), Undated release",
            "Independent included-title years stay visible in shared formatting");
        Check(JsonSerializer.Serialize(game) == before, "Display formatting does not mutate identities, titles, points or years");

        var tournament = Guid.NewGuid();
        var other = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var module = new StorageModule();
        module.Values[tournament] = [entry, missing];
        var session = new ArenaExclusionSession();
        Check(!session.IsReadyFor(tournament), "Voting waits for this tab's exclusions to restore");
        await session.RestoreAsync(new StorageRuntime(module), tournament, [entry]);
        Check(session.GetIds(tournament).SetEquals([entry]) && session.GetIds(other).Count == 0,
            "Restore prunes missing IDs and never exposes exclusions for another tournament");
        await session.SetAsync(missing, true);
        Check(module.Writes == 0, "Unavailable entries cannot be added to exclusions");
        await session.SetAsync(entry, false);
        Check(module.Writes == 1 && module.Values[tournament].Length == 0, "Unchecking saves the tournament-specific session setting");
        await session.SetAsync(entry, true);
        var otherSession = new ArenaExclusionSession();
        await otherSession.RestoreAsync(new StorageRuntime(module), tournament, [entry]);
        Check(otherSession.GetIds(tournament).Contains(entry), "A newly created page restores exclusions after navigation");
        await session.RestoreAsync(new StorageRuntime(module), other, [entry]);
        Check(session.IsReadyFor(other) && session.GetIds(other).Count == 0 && session.GetIds(tournament).Count == 0,
            "Switching tournaments clears the page's previous exclusion view");
        await otherSession.DisposeAsync();
        await session.DisposeAsync();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private sealed class StorageRuntime(StorageModule module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult((TValue)(object)module);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StorageModule : IJSObjectReference
    {
        public Dictionary<Guid, Guid[]> Values { get; } = [];
        public int Writes { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            var tournament = (Guid)args![0]!;
            if (identifier == "read") return ValueTask.FromResult((TValue)(object)Values.GetValueOrDefault(tournament, []));
            if (identifier != "write") throw new InvalidOperationException(identifier);
            Values[tournament] = (Guid[])args[1]!;
            Writes++;
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
