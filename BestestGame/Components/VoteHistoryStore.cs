using System.Text.Json;
using BestestGame.Models;
using BestestGame.Services;
using Microsoft.JSInterop;

namespace BestestGame.Components;

// Single-matchup votes this browser tab can still undo. Voting and Rankings share one list per
// tournament in sessionStorage, so Undo survives a reload and works from either page.
public static class VoteHistoryStore
{
    private static string Key(Guid tournamentId) => $"bestestgame:ordinary:{tournamentId}:undo";

    /// <param name="module">The focusedVoting.js module, which owns the session helpers.</param>
    public static async Task<List<GameService.DuelResult>> ReadAsync(IJSObjectReference module, Guid tournamentId, IEnumerable<Duel> duels)
    {
        var saved = await module.InvokeAsync<string?>("readSession", Key(tournamentId));
        if (string.IsNullOrEmpty(saved)) return [];
        try
        {
            return OrdinaryDuelSession.StillSaved(JsonSerializer.Deserialize<GameService.DuelResult?[]>(saved) ?? [], duels);
        }
        catch (JsonException) { return []; }
    }

    public static ValueTask WriteAsync(IJSObjectReference module, Guid tournamentId, IEnumerable<GameService.DuelResult> results)
        => module.InvokeVoidAsync("saveSession", Key(tournamentId), JsonSerializer.Serialize(results));
}
