// Temporary, per-tab UI settings. Never write exclusions into tournament data.
const memory = new Map();
const keyFor = tournamentId => `bestestgame:arena-bans:${tournamentId}`;

export function read(tournamentId, availableIds) {
    const key = keyFor(tournamentId);
    let value = memory.get(key) ?? [];
    try {
        const stored = sessionStorage.getItem(key);
        if (stored === null) value = [];
        else {
            try { value = JSON.parse(stored); }
            catch { value = []; }
        }
    } catch { /* In-memory settings still work during this tab's navigation. */ }
    const available = new Set(availableIds.map(id => id.toLowerCase()));
    const ids = [...new Set((Array.isArray(value) ? value : [])
        .filter(id => typeof id === 'string').map(id => id.toLowerCase()))]
        .filter(id => available.has(id));
    write(tournamentId, ids);
    return ids;
}

export function write(tournamentId, ids) {
    const key = keyFor(tournamentId);
    memory.set(key, [...ids]);
    try { sessionStorage.setItem(key, JSON.stringify(ids)); }
    catch { /* Browser-session persistence is unavailable; keep this tab usable. */ }
}
