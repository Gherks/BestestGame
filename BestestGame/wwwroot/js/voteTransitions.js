// Presentation only: wait for a saved-vote revision, never invoke or delay a vote.
const instanceKey = Symbol.for("BestestGame.voteTransitions");

export function initialize() {
    if (window[instanceKey]) return window[instanceKey];
    const lifetime = new AbortController();
    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
    const states = new Map();

    function clear(root) {
        const state = states.get(root);
        if (!state) return;
        clearTimeout(state.timer);
        state.animations?.forEach(animation => animation.cancel());
        state.incoming?.classList.remove("vote-transition-entering");
        state.snapshot.remove();
        states.delete(root);
    }

    function clearAll() {
        for (const root of states.keys()) clear(root);
    }

    function remember(event) {
        const target = event.target instanceof Element ? event.target : null;
        const root = target?.closest("[data-motion-arena]");
        if (!root) return;
        const button = target.closest("button[data-motion-choice]");
        if (!button) {
            if (target.closest("button, input, select, a")) clear(root);
            return;
        }
        if (reducedMotion.matches || document.hidden || button.disabled ||
            root.closest("[inert], [hidden]") ||
            [...document.querySelectorAll('dialog[open], [role="dialog"][aria-modal="true"]')]
                .some(dialog => dialog.getClientRects().length)) return;

        const round = root.querySelector("[data-motion-round]");
        if (!round || !round.getClientRects().length) return;
        const presentation = root.dataset.presentation;
        // Queued clicks from one presentation share its first snapshot/choice.
        const previous = states.get(root);
        if (previous && !previous.committed && previous.presentation === presentation) return;
        clear(root);

        const snapshot = round.cloneNode(true);
        snapshot.inert = true;
        snapshot.setAttribute("aria-hidden", "true");
        snapshot.classList.add("vote-transition-outgoing");
        snapshot.querySelectorAll("[id]").forEach(element => element.removeAttribute("id"));
        // Copies must never masquerade as live application choices/shortcuts.
        snapshot.querySelectorAll("[data-choice], [data-action], [data-duel-id], [data-winner-id]").forEach(element => {
            for (const attribute of ["data-choice", "data-action", "data-duel-id", "data-winner-id"])
                element.removeAttribute(attribute);
        });
        snapshot.querySelectorAll(".is-pressed, .is-returning").forEach(element =>
            element.classList.remove("is-pressed", "is-returning"));
        snapshot.querySelectorAll("button, a, input, select, textarea, [tabindex]").forEach(element =>
            element.setAttribute("tabindex", "-1"));
        const state = {
            snapshot, bounds: round.getBoundingClientRect(), presentation,
            revision: root.dataset.voteRevision, choice: button.dataset.motionChoice
        };
        // A rejected/disconnected action must not leave a retained snapshot.
        state.timer = setTimeout(() => { if (states.get(root) === state) clear(root); }, 5000);
        states.set(root, state);
    }

    function settle(root, state) {
        const incoming = root.querySelector("[data-motion-round], [data-motion-result]");
        if (!incoming || reducedMotion.matches || document.hidden) { clear(root); return; }
        clearTimeout(state.timer);
        state.committed = true;
        state.nextPresentation = root.dataset.presentation;
        const { snapshot, bounds } = state;
        const incomingHeight = incoming.getBoundingClientRect().height;
        state.incoming = incoming;
        incoming.classList.add("vote-transition-entering");
        Object.assign(snapshot.style, {
            left: `${bounds.left}px`, top: `${bounds.top}px`, width: `${bounds.width}px`
        });
        const selected = snapshot.querySelector(`[data-motion-choice="${state.choice}"]`);
        selected?.setAttribute("data-settled", "true");
        const face = selected?.querySelector(":scope > .tactile__face");
        if (face) {
            const label = document.createElement("span");
            label.className = "vote-transition-saved";
            label.textContent = "Selected";
            face.append(label);
        }
        document.body.append(snapshot);
        const outgoingAnimation = snapshot.animate([
            { transform: "translateX(0)", offset: 0 },
            { transform: `translateX(${-bounds.right * .85}px)`, offset: .85 },
            { transform: `translateX(${-bounds.right - 160}px)`, offset: 1 }
        ], { duration: 600, delay: 0, easing: "cubic-bezier(.55,0,.8,.4)", fill: "both" });
        const fadeAnimation = snapshot.animate([
            { opacity: 1 },
            { opacity: 0 }
        ], { duration: 100, delay: 180, easing: "ease-out", fill: "both" });
        const incomingAnimation = incoming.animate([
            { transform: `translateX(${window.innerWidth - bounds.left + 32}px)` },
            { transform: "translateX(0)" }
        ], { duration: 360, delay: 180, easing: "cubic-bezier(.22,1,.36,1)", fill: "backwards" });
        const incomingFadeAnimation = incoming.animate([
            { opacity: 0 },
            { opacity: 1 }
        ], { duration: 100, delay: 180, easing: "ease-out", fill: "backwards" });
        // Keep surrounding controls below the outgoing round until it has faded.
        // This matters when a ten-opponent group becomes a much shorter duel.
        const layoutAnimation = incoming.animate([
            { minHeight: `${Math.max(bounds.height, incomingHeight)}px`, offset: 0 },
            { minHeight: `${Math.max(bounds.height, incomingHeight)}px`, offset: 280 / 540, easing: "cubic-bezier(.16,1,.3,1)" },
            { minHeight: `${incomingHeight}px`, offset: 1 }
        ], { duration: 540, easing: "linear", fill: "none" });
        state.animations = [outgoingAnimation, fadeAnimation, incomingAnimation, incomingFadeAnimation, layoutAnimation];
        outgoingAnimation.finished.then(() => snapshot.remove()).catch(() => {});
        incomingAnimation.finished.then(() => {
            if (states.get(root) === state) clear(root);
        }).catch(() => {});
        state.timer = setTimeout(() => { if (states.get(root) === state) clear(root); }, 700);
    }

    const observer = new MutationObserver(() => {
        for (const [root, state] of states) {
            if (!root.isConnected) { clear(root); continue; }
            if (state.committed) {
                if (root.dataset.presentation !== state.nextPresentation) clear(root);
            } else if (root.dataset.voteRevision !== state.revision) {
                if (root.dataset.settledChoice === state.choice) settle(root, state);
                else clear(root);
            } else if (root.dataset.presentation !== state.presentation) {
                clear(root);
            }
        }
    });
    observer.observe(document.documentElement, {
        subtree: true, childList: true, attributes: true,
        attributeFilter: ["data-vote-revision", "data-presentation"]
    });
    document.addEventListener("click", remember, { capture: true, signal: lifetime.signal });
    document.addEventListener("visibilitychange", () => { if (document.hidden) clearAll(); }, { signal: lifetime.signal });
    window.addEventListener("pagehide", clearAll, { signal: lifetime.signal });
    window.addEventListener("resize", clearAll, { signal: lifetime.signal });
    // React to user scrolling, rather than scroll anchoring during layout changes.
    document.addEventListener("wheel", clearAll, { passive: true, signal: lifetime.signal });
    document.addEventListener("touchmove", clearAll, { passive: true, signal: lifetime.signal });
    document.addEventListener("keydown", event => {
        if (["PageUp", "PageDown", "Home", "End", "ArrowUp", "ArrowDown"].includes(event.key)) clearAll();
    }, { signal: lifetime.signal });
    window.addEventListener("blur", event => { if (event.target === window) clearAll(); }, { signal: lifetime.signal });
    reducedMotion.addEventListener("change", clearAll, { signal: lifetime.signal });

    const controller = {
        dispose() {
            clearAll();
            lifetime.abort();
            observer.disconnect();
            if (window[instanceKey] === controller) delete window[instanceKey];
        }
    };
    window[instanceKey] = controller;
    return controller;
}

initialize();
