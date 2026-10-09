using Microsoft.JSInterop;

namespace BestestGame.Components;

// Each page owns its helper; sessionStorage is the authority across navigation.
public sealed class ArenaExclusionSession : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private Guid? _tournamentId;
    private HashSet<Guid> _available = [];
    private HashSet<Guid> _ids = [];
    private readonly HashSet<Guid> _empty = [];

    public bool IsReadyFor(Guid tournamentId) => _tournamentId == tournamentId;
    public IReadOnlySet<Guid> GetIds(Guid? tournamentId) => tournamentId == _tournamentId ? _ids : _empty;

    public async Task RestoreAsync(IJSRuntime js, Guid tournamentId, IEnumerable<Guid> available)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", ScriptModules.Url("arenaExclusions.js"));
        _available = available.ToHashSet();
        var restored = await _module.InvokeAsync<Guid[]>("read", tournamentId, _available.ToArray());
        _ids = restored.Where(_available.Contains).ToHashSet();
        _tournamentId = tournamentId;
    }

    public async Task SetAsync(Guid gameId, bool excluded)
    {
        if (_module is null || _tournamentId is null || !_available.Contains(gameId)) return;
        _ids = new(_ids);
        if (excluded) _ids.Add(gameId);
        else _ids.Remove(gameId);
        await _module.InvokeVoidAsync("write", _tournamentId, _ids.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
