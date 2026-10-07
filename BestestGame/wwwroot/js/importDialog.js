const controllers = new WeakMap();

// Closed native disclosures can still report client rectangles for descendants.
function visible(element) {
    if (!(element instanceof HTMLElement) || !element.isConnected || !element.checkVisibility({ visibilityProperty: true })) return false;
    const bounds = element.getBoundingClientRect();
    return bounds.width > 0 && bounds.height > 0;
}

export function initialize(dialog, receiver, cancelMethod = 'CancelRemovalFromKeyboard') {
    if (controllers.has(dialog)) return;
    const state = { opener: null, closing: false };
    const onCancel = event => {
        event.preventDefault();
        if (dialog.querySelector('[aria-busy="true"]')) return;
        state.closing = true;
        dialog.close();
        receiver.invokeMethodAsync(cancelMethod).catch(() => {
            // A disconnected circuit is handled by Blazor's recovery UI.
        });
    };
    const onKeyDown = event => {
        if (event.key !== 'Tab' || !dialog.open) return;
        const controls = [...dialog.querySelectorAll('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), details > summary, [tabindex="0"]')]
            .filter(visible);
        const first = controls[0];
        const last = controls.at(-1);
        if (!first) return;
        if (!dialog.contains(document.activeElement)) {
            event.preventDefault();
            first.focus();
        } else if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    };
    dialog.addEventListener('cancel', onCancel);
    dialog.addEventListener('keydown', onKeyDown);
    state.onCancel = onCancel;
    state.onKeyDown = onKeyDown;
    controllers.set(dialog, state);
}

export function sync(dialog, open, fallback, initialFieldId) {
    const state = controllers.get(dialog);
    if (!state) return;
    if (open) {
        if (!dialog.open && !state.closing) {
            state.opener = document.activeElement;
            dialog.showModal();
            if (initialFieldId) dialog.querySelector(`#${CSS.escape(initialFieldId)}`)?.focus();
        }
        if (dialog.open && !dialog.contains(document.activeElement)) {
            dialog.querySelector('[autofocus]')?.focus();
        }
    } else {
        if (dialog.open) dialog.close();
        state.closing = false;
        if (state.opener) {
            const rowSummary = state.opener.closest('[data-game-id]')?.querySelector('.game-actions > summary');
            const target = visible(state.opener) ? state.opener : visible(rowSummary) ? rowSummary : fallback;
            if (visible(target)) {
                target.scrollIntoView({ block: 'nearest', behavior: 'instant' });
                target.focus({ preventScroll: true });
            }
            state.opener = null;
        }
    }
}

export function focusFeedback(element) {
    if (!visible(element)) return;
    element.scrollIntoView({ block: 'nearest', behavior: 'instant' });
    element.focus({ preventScroll: true });
}

export function dispose(dialog) {
    const state = controllers.get(dialog);
    if (!state) return;
    dialog.removeEventListener('cancel', state.onCancel);
    dialog.removeEventListener('keydown', state.onKeyDown);
    if (dialog.open) dialog.close();
    controllers.delete(dialog);
}
