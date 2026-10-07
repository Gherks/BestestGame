const listeners = new WeakMap();

export function initialize(root) {
    if (listeners.has(root)) return;
    const onKeyDown = event => {
        if (event.key !== 'Escape') return;
        const actions = event.target.closest('.game-actions[open]');
        if (!actions || !root.contains(actions)) return;
        event.preventDefault();
        actions.open = false;
        const summary = actions.querySelector('summary');
        summary.scrollIntoView({ block: 'nearest', behavior: 'instant' });
        summary.focus({ preventScroll: true });
    };
    const onToggle = event => {
        const actions = event.target;
        if (!(actions instanceof HTMLDetailsElement) || !actions.matches('.game-actions')) return;
        const summary = actions.querySelector('summary');
        const target = actions.open ? actions.querySelector('.game-action-list') : document.activeElement === summary ? summary : null;
        target?.scrollIntoView({ block: 'nearest', behavior: 'instant' });
    };
    root.addEventListener('keydown', onKeyDown);
    root.addEventListener('toggle', onToggle, true);
    listeners.set(root, { onKeyDown, onToggle });
}

export function dispose(root) {
    const state = listeners.get(root);
    if (state) {
        root.removeEventListener('keydown', state.onKeyDown);
        root.removeEventListener('toggle', state.onToggle, true);
    }
    listeners.delete(root);
}
