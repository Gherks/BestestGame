const handlers = new WeakMap();
const shortcuts = {
    "1": "win", arrowleft: "win",
    "2": "lose", arrowright: "lose",
    m: "split", u: "undo", s: "skip"
};

export function connect(root) {
    disconnect(root);
    let lastPresentation;
    const handler = event => {
        if (!root.isConnected) {
            disconnect(root);
            return;
        }
        if (event.repeat || event.isComposing || event.ctrlKey || event.altKey || event.metaKey ||
            root.dataset.shortcutsEnabled !== "true" ||
            event.target.closest?.("input, textarea, select, [contenteditable]")) return;

        const action = shortcuts[event.key.toLowerCase()];
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
    root.scrollIntoView({ block: "start", behavior: "instant" });
}

export function disconnect(root) {
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
