// The transitions are fetched with this module's own version, so both always come from the same release.
const { connect: connectTransitions, disconnect: disconnectTransitions } =
    await import(`./duelTransitions.js${new URL(import.meta.url).search}`);

const handlers = new WeakMap();
const shortcuts = {
    "1": "win", arrowleft: "win",
    "2": "lose", arrowright: "lose",
    m: "split", u: "undo", s: "skip"
};

export function connect(root, actionKeys = shortcuts, scrollToArena = true) {
    if (!(root instanceof HTMLElement) || !root.isConnected) return;
    disconnect(root);
    connectTransitions(root);
    let lastPresentation;
    const handler = event => {
        if (!root.isConnected) {
            disconnect(root);
            return;
        }
        if (event.repeat || event.isComposing || event.ctrlKey || event.altKey || event.metaKey ||
            root.dataset.shortcutsEnabled !== "true" ||
            [...document.querySelectorAll('dialog[open], [role="dialog"][aria-modal="true"]')]
                .some(dialog => dialog.getClientRects().length > 0) ||
            event.target.closest?.("input, textarea, select, [contenteditable]")) return;

        const action = actionKeys[event.key.toLowerCase()];
        if (root.dataset.duelTransitionBusy === "true" &&
            ["left", "right", "win", "lose"].includes(action)) return;
        const button = action && root.querySelector(`[data-action="${action}"]`);
        if (!button || button.disabled) return;

        event.preventDefault();
        // Wait for the rendered choice to advance before sending another key.
        if (lastPresentation === root.dataset.presentation) return;
        lastPresentation = root.dataset.presentation;
        button.click();
    };
    handlers.set(root, handler);
    document.addEventListener("keydown", handler);
    if (scrollToArena) root.scrollIntoView({ block: "start", behavior: "instant" });
}

export function disconnect(root) {
    disconnectTransitions(root);
    const handler = handlers.get(root);
    if (handler) document.removeEventListener("keydown", handler);
    handlers.delete(root);
}

export function readSession(key) {
    try { return sessionStorage.getItem(key); }
    catch { return null; }
}

export function saveSession(key, value) {
    try { sessionStorage.setItem(key, value); }
    catch { /* Voting still works when browser storage is unavailable. */ }
}
