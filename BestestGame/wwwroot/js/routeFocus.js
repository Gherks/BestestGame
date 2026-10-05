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
