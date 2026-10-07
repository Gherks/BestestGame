// Layout only: reserve the dock's actual height, including enlarged text/feedback.
// Keep keyboard focus above the dock while moving between choices and entry context.
const controls = new WeakMap();

export function connect(root) {
    // Enhanced navigation can remove a referenced element before interop runs.
    if (!(root instanceof HTMLElement) || !root.isConnected) return;
    disconnect(root);
    const dock = root.querySelector("[data-focused-controls]");
    if (!dock) return;
    const observer = new ResizeObserver(() => {
        if (!root.isConnected) { disconnect(root); return; }
        root.style.setProperty("--focused-controls-height", `${dock.getBoundingClientRect().height + 16}px`);
    });
    const onFocus = event => {
        const target = event.target;
        if (!(target instanceof HTMLElement) || dock.contains(target)) return;
        setTimeout(() => {
            // A tap on a tall choice must not move the page under the pointer.
            if (!root.isConnected || !target.matches(":focus-visible") ||
                getComputedStyle(dock).position !== "sticky") return;
            const bounds = target.getBoundingClientRect();
            const dockBounds = dock.getBoundingClientRect();
            // A choice taller than the space above the dock keeps its top in view.
            if (dockBounds.top < innerHeight && bounds.bottom > dockBounds.top - 12)
                window.scrollBy({ top: Math.min(bounds.bottom - dockBounds.top + 12, Math.max(0, bounds.top)), behavior: "instant" });
        }, 0);
    };
    controls.set(root, { observer, onFocus });
    observer.observe(dock);
    root.addEventListener("focusin", onFocus);
}

export function disconnect(root) {
    if (!(root instanceof HTMLElement)) return;
    const state = controls.get(root);
    state?.observer.disconnect();
    if (state) root.removeEventListener("focusin", state.onFocus);
    root.style.removeProperty("--focused-controls-height");
    controls.delete(root);
}
