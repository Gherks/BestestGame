namespace BestestGame.Components;

/// <summary>
/// How a page's content enters, as on Game of the year. Prerendered markup waits unseen, because the
/// interactive render replaces it and would restart any motion begun on it. Once the page is interactive and
/// ready its content arrives, and shortly after the page settles, so content that renders later (a new duel,
/// a search result, a re-sorted list) does not arrive again. The motion is in app.css: the page carries
/// <c>data-entrance</c>, and what moves carries <c>arrive</c>, <c>arrive-canvas</c> or <c>arrive-develop</c>.
/// </summary>
public sealed class PageEntrance
{
    // Longer than the slowest arrival in app.css, delays included.
    private static readonly TimeSpan Arrival = TimeSpan.FromMilliseconds(1500);
    private bool _started;
    private bool _settled;

    /// <summary>The page's <c>data-entrance</c>: "waiting" until it is ready, "enter" while it arrives, then nothing.
    /// A page that has begun to arrive keeps arriving, even should it briefly stop being ready.</summary>
    public string? State(bool ready) => _settled ? null : ready || _started ? "enter" : "waiting";

    /// <summary>Call after each interactive render. The first ready one schedules the settle.</summary>
    public void Rendered(bool ready, Func<Task> refresh)
    {
        if (!ready || _started || _settled) return;
        _started = true;
        _ = SettleAsync(refresh);
    }

    /// <summary>Settles at once, for an action that replaces arriving content before it has arrived.</summary>
    public void Settle() => _settled = true;

    private async Task SettleAsync(Func<Task> refresh)
    {
        await Task.Delay(Arrival);
        if (_settled) return;
        _settled = true;
        try { await refresh(); }
        catch (ObjectDisposedException) { /* The page has closed. */ }
    }
}
