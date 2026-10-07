// Interactive pages can replace the heading after FocusOnNavigate focuses it.
// Retain that focus across hydration, unless the user has moved it elsewhere.
let focusedHeading = null;

function trackFocus(target) {
    focusedHeading = target instanceof HTMLElement && target.matches('#main-content h1')
        ? target
        : null;
}

trackFocus(document.activeElement);
document.addEventListener('focusin', event => trackFocus(event.target));
document.addEventListener('pointerdown', () => { focusedHeading = null; }, true);
document.addEventListener('keydown', () => { focusedHeading = null; }, true);
window.addEventListener('blur', () => { focusedHeading = null; });

new MutationObserver(() => {
    if (!focusedHeading || focusedHeading.isConnected) return;

    focusedHeading = null;
    // Removing the focused node leaves focus on the body. Respect other focus.
    if (document.activeElement !== document.body) return;

    const heading = document.querySelector('#main-content h1');
    if (!(heading instanceof HTMLElement)) return;
    if (!heading.hasAttribute('tabindex')) heading.setAttribute('tabindex', '-1');
    heading.focus({ preventScroll: true });
}).observe(document.body, { childList: true, subtree: true });

// The mobile disclosure works before hydration. Collapse it on navigation and
// let Escape return keyboard focus to its summary without trapping the page.
document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    const menu = document.activeElement?.closest('[data-mobile-menu][open]');
    if (!menu) return;
    event.preventDefault();
    menu.open = false;
    menu.querySelector('summary')?.focus();
});

let menuNavigation = false;
document.addEventListener('click', event => {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    if (event.target instanceof Element && event.target.closest('[data-mobile-menu] a[href]'))
        menuNavigation = true;
});

Blazor.addEventListener('enhancedload', () => {
    document.querySelectorAll('[data-mobile-menu][open]').forEach(menu => { menu.open = false; });
    if (!menuNavigation) return;
    menuNavigation = false;
    // A same-page link may leave focus on the body when its menu collapses.
    // Restore the heading, then the existing observer retains it through hydration.
    if (document.activeElement !== document.body && !document.activeElement?.closest('[data-mobile-menu]')) return;
    const heading = document.querySelector('#main-content h1');
    if (!(heading instanceof HTMLElement)) return;
    if (!heading.hasAttribute('tabindex')) heading.setAttribute('tabindex', '-1');
    heading.focus({ preventScroll: true });
});
