// Visual feedback only. Native activation and Blazor handlers own every action.
// Delegation also covers controls introduced by enhanced navigation or rerenders.
const instanceKey = Symbol.for("BestestGame.tactileControls");

export function initialize() {
    if (window[instanceKey]) return window[instanceKey];

    const lifetime = new AbortController();
    const states = new Map();
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
    const available = control => control.isConnected &&
        !control.matches(":disabled, [aria-disabled='true']") &&
        !control.closest("[inert], [hidden]");
    const controlFor = target => {
        const control = target instanceof Element ? target.closest("button.tactile, a.tactile[href]") : null;
        return control && control.querySelector(":scope > .tactile__face") && available(control) ? control : null;
    };

    function clear(control) {
        const state = states.get(control);
        if (!state) return;
        clearTimeout(state.releaseTimer);
        clearTimeout(state.cleanupTimer);
        control.classList.remove("is-pressed", "is-returning");
        states.delete(control);
    }

    function press(control, source, input) {
        clear(control);
        const state = { source, input, phase: "pressed", face: control.querySelector(":scope > .tactile__face") };
        states.set(control, state);
        control.classList.add("is-pressed");
        return state;
    }

    function release(control) {
        const state = states.get(control);
        if (!state || state.phase !== "pressed") return;
        if (!available(control)) { clear(control); return; }
        clearTimeout(state.releaseTimer);
        state.phase = "returning";
        control.classList.remove("is-pressed");
        control.classList.add("is-returning");
        // animationend normally cleans up; this also handles cancelled animations.
        const duration = parseFloat(getComputedStyle(control).getPropertyValue("--button-return-time"));
        state.cleanupTimer = setTimeout(() => clear(control), reducedMotion.matches ? 0 : duration + 50);
    }

    function cancelAll() {
        for (const control of states.keys()) clear(control);
    }

    function listen(target, type, handler) {
        target.addEventListener(type, handler, { capture: true, signal: lifetime.signal });
    }

    listen(document, "pointerdown", event => {
        if (!event.isPrimary || event.button !== 0) return;
        const control = controlFor(event.target);
        if (control) press(control, "pointer", event.pointerId);
    });
    listen(document, "pointerup", event => {
        for (const [control, state] of states) {
            if (state.source === "pointer" && state.input === event.pointerId) release(control);
        }
    });
    listen(document, "pointercancel", event => {
        for (const [control, state] of states) {
            if (state.source === "pointer" && state.input === event.pointerId) clear(control);
        }
    });
    listen(document, "pointerout", event => {
        const control = controlFor(event.target);
        const state = control && states.get(control);
        if (state?.phase !== "pressed" || state.source !== "pointer" || state.input !== event.pointerId) return;
        if (!(event.relatedTarget instanceof Node) || !control.contains(event.relatedTarget)) clear(control);
    });
    listen(document, "keydown", event => {
        if (event.repeat || event.isComposing || event.ctrlKey || event.altKey || event.metaKey) return;
        if (event.target instanceof Element && event.target.closest("input, textarea, select, [contenteditable]")) return;
        const control = controlFor(event.target);
        if (control && (event.key === "Enter" || (event.key === " " && control.tagName === "BUTTON"))) {
            press(control, "keyboard", event.key);
        }
    });
    listen(document, "keyup", event => {
        for (const [control, state] of states) {
            if (state.source === "keyboard" && state.input === event.key) release(control);
        }
    });
    listen(document, "focusout", event => {
        const control = controlFor(event.target);
        if (control && (!(event.relatedTarget instanceof Node) || !control.contains(event.relatedTarget))) clear(control);
    });
    listen(document, "click", event => {
        const control = controlFor(event.target);
        if (!control) return;
        const state = states.get(control);
        // A trusted native click completes an existing pointer/keyboard stroke.
        if (state && state.source !== "synthetic" && event.isTrusted) return;
        const pulse = press(control, "synthetic");
        pulse.releaseTimer = setTimeout(() => release(control), 65);
        // Never call click(), preventDefault(), or an application callback here.
    });
    listen(document, "animationend", event => {
        if (event.animationName !== "tactile-return") return;
        const control = controlFor(event.target);
        if (control && states.get(control)?.phase === "returning") clear(control);
    });
    listen(window, "blur", event => { if (event.target === window) cancelAll(); });
    listen(window, "pagehide", cancelAll);
    listen(document, "visibilitychange", () => { if (document.hidden) cancelAll(); });

    const observer = new MutationObserver(() => {
        for (const [control, state] of states) {
            if (!available(control) || control.querySelector(":scope > .tactile__face") !== state.face) clear(control);
        }
    });
    observer.observe(document.documentElement, {
        subtree: true,
        childList: true,
        attributes: true,
        attributeFilter: ["disabled", "aria-disabled", "inert", "hidden"]
    });

    const controller = {
        dispose() {
            cancelAll();
            lifetime.abort();
            observer.disconnect();
            if (window[instanceKey] === controller) delete window[instanceKey];
        }
    };
    window[instanceKey] = controller;
    return controller;
}

initialize();
