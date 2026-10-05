const controllers = new WeakMap();

export function initialize(dialog, receiver, cancelMethod = 'CancelRemovalFromKeyboard') {
    if (controllers.has(dialog)) return;
    const state = { opener: null, closing: false };
    const onCancel = event => {
        event.preventDefault();
        state.closing = true;
        dialog.close();
        receiver.invokeMethodAsync(cancelMethod).catch(() => {
            // A disconnected circuit is handled by Blazor's recovery UI.
        });
    };
    const onKeyDown = event => {
        if (event.key !== 'Tab' || !dialog.open) return;
        const controls = [...dialog.querySelectorAll('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]')]
            .filter(control => control.getClientRects().length > 0);
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

export function sync(dialog, open, fallback) {
    const state = controllers.get(dialog);
    if (!state) return;
    if (open) {
        if (!dialog.open && !state.closing) {
            state.opener = document.activeElement;
            dialog.showModal();
        }
        if (dialog.open && !dialog.contains(document.activeElement)) {
            dialog.querySelector('[autofocus]')?.focus();
        }
    } else {
        if (dialog.open) dialog.close();
        state.closing = false;
        if (state.opener) {
            const target = state.opener.isConnected ? state.opener : fallback;
            if (target instanceof HTMLElement) target.focus({ preventScroll: target === state.opener });
            state.opener = null;
        }
    }
}

export function dispose(dialog) {
    const state = controllers.get(dialog);
    if (!state) return;
    dialog.removeEventListener('cancel', state.onCancel);
    dialog.removeEventListener('keydown', state.onKeyDown);
    if (dialog.open) dialog.close();
    controllers.delete(dialog);
}
