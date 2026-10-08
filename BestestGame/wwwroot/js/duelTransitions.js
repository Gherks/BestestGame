// A vote saves normally. An inert copy leaves while Blazor renders the next duel.
const transitions = new WeakMap();
const voteActions = new Set(["left", "right", "win", "lose"]);
// The outgoing copy holds still this long, so the glow on the chosen side sets in before it leaves.
const hold = 220;

function clear(state) {
    const pending = state.pending;
    state.pending = null;
    if (!pending) return;
    clearTimeout(pending.timeout);
    pending.outgoingAnimation?.cancel();
    pending.incomingAnimation?.cancel();
    pending.copy.remove();
    state.stage.style.minHeight = pending.minHeight;
    delete state.stage.dataset.transitionPhase;
    delete state.root.dataset.duelTransitionBusy;
}

function synchronizeStage(state) {
    const stage = state.root.querySelector(":scope > [data-duel-stage]");
    if (stage !== state.stage) {
        clear(state);
        state.stage = stage;
    }
    return stage;
}

function enter(state) {
    const pending = state.pending;
    if (!pending || !pending.left || !pending.changed || pending.entering) return;
    if (!state.root.isConnected) { disconnect(state.root); return; }
    const content = state.stage.querySelector(":scope > [data-duel-content]");
    if (!content) { clear(state); return; }
    pending.entering = true;
    pending.copy.remove();
    state.stage.dataset.transitionPhase = "incoming";
    pending.incomingAnimation = content.animate([
        { opacity: 0, transform: `translateX(${pending.distance}px)` },
        { opacity: 1, transform: "translateX(0)" }
    ], { duration: 220, easing: "cubic-bezier(0, 0, .2, 1)", fill: "both" });
    pending.incomingAnimation.finished.then(() => {
        if (state.pending === pending) clear(state);
    }).catch(() => { /* Cancellation restores the static presentation. */ });
}

function leave(state, button) {
    const content = state.stage.querySelector(":scope > [data-duel-content]");
    if (!content || typeof content.animate !== "function") return;
    const copy = content.cloneNode(true);
    // The copy shows which side won as it leaves. Matched by position, before its attributes go.
    const choices = [...content.querySelectorAll("button[data-action]")];
    copy.querySelectorAll("button[data-action]").forEach((choice, index) =>
        choice.classList.add(choices[index] === button ? "duel-chosen" : "duel-passed"));
    // The copy must never expose a second actionable or accessible duel.
    for (const element of [copy, ...copy.querySelectorAll("*")]) {
        element.removeAttribute("id");
        for (const attribute of [...element.attributes])
            if (attribute.name.startsWith("data-")) element.removeAttribute(attribute.name);
    }
    copy.inert = true;
    copy.setAttribute("aria-hidden", "true");
    copy.classList.add("duel-outgoing");
    copy.style.width = `${content.getBoundingClientRect().width}px`;
    const pending = state.pending = {
        copy, key: state.stage.dataset.duelKey, presentation: state.root.dataset.presentation,
        minHeight: state.stage.style.minHeight,
        distance: Math.min(96, content.getBoundingClientRect().width * .3)
    };
    state.stage.style.minHeight = `${state.stage.getBoundingClientRect().height}px`;
    state.stage.append(copy);
    state.stage.dataset.transitionPhase = "outgoing";
    state.root.dataset.duelTransitionBusy = "true";
    pending.outgoingAnimation = copy.animate([
        { opacity: 1, transform: "translateX(0)" },
        { opacity: 0, transform: `translateX(-${pending.distance}px)` }
    ], { delay: hold, duration: 180, easing: "cubic-bezier(.4, 0, 1, 1)", fill: "both" });
    pending.outgoingAnimation.finished.then(() => {
        if (state.pending !== pending) return;
        pending.left = true;
        enter(state);
    }).catch(() => { /* Cancellation restores the static presentation. */ });
    // An ignored event or failed render must never leave choices hidden/locked.
    pending.timeout = setTimeout(() => clear(state), 2000);
}

export function connect(root) {
    if (!(root instanceof HTMLElement) || !root.isConnected) return;
    disconnect(root);
    const stage = root.querySelector(":scope > [data-duel-stage]");
    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
    const state = { root, stage, pending: null };
    const onClick = event => {
        const button = event.target.closest?.("button[data-action]");
        if (!button || button.closest("[data-presentation]") !== root || button.disabled) return;
        if (!synchronizeStage(state)) return;
        if (!voteActions.has(button.dataset.action)) { clear(state); return; }
        if (state.pending) {
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }
        if (reducedMotion.matches) return;
        try { leave(state, button); }
        catch { clear(state); /* Native voting remains available without effects. */ }
    };
    const observer = new MutationObserver(() => {
        if (!root.isConnected) { disconnect(root); return; }
        synchronizeStage(state);
        const pending = state.pending;
        if (!pending) return;
        if (state.stage.dataset.duelKey !== pending.key) {
            pending.changed = true;
            enter(state);
        } else if (root.dataset.presentation !== pending.presentation) {
            clear(state);
        }
    });
    const cancel = () => clear(state);
    const onKeyDown = event => {
        // Tab reveals the live content before focus moves. Pointer focus must
        // not unlock a second vote while the new duel is still arriving.
        if (event.key === "Tab") clear(state);
    };
    // Store the same mutable state used by the listeners for reliable disposal.
    Object.assign(state, { observer, onClick, cancel, onKeyDown, reducedMotion });
    transitions.set(root, state);
    observer.observe(root, { childList: true, subtree: true, attributes: true,
        attributeFilter: ["data-duel-key", "data-presentation"] });
    observer.observe(document.body, { childList: true, subtree: true });
    root.addEventListener("click", onClick, true);
    document.addEventListener("keydown", onKeyDown, true);
    root.addEventListener("change", cancel, true);
    reducedMotion.addEventListener("change", cancel);
    window.addEventListener("resize", cancel);
    document.addEventListener("visibilitychange", cancel);
}

export function disconnect(root) {
    const state = transitions.get(root);
    if (!state) return;
    clear(state);
    state.observer.disconnect();
    root.removeEventListener("click", state.onClick, true);
    document.removeEventListener("keydown", state.onKeyDown, true);
    root.removeEventListener("change", state.cancel, true);
    state.reducedMotion.removeEventListener("change", state.cancel);
    window.removeEventListener("resize", state.cancel);
    document.removeEventListener("visibilitychange", state.cancel);
    transitions.delete(root);
}
