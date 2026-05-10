// splitter.js — drag-resizable vertical divider between two side-by-side panes.
//
// The left pane's inline `width` (as a percentage of the container) is mutated
// on drag; the right pane uses flex:1 to fill the remainder. Percentages —
// not pixels — so the split ratio survives window resizes correctly.
//
// Cytoscape doesn't auto-react to container resize, so we dispatch a
// `pf-resize` event on every move. diagram.js listens for it and calls
// cy.resize().

let attached = false;

const STORAGE_KEY = 'pf-diagram-w-pct';
const MIN_LEFT_PX  = 360;
const MIN_RIGHT_PX = 320;
const HANDLE_PX    = 10;
const KEY_STEP     = 2.5;          // %, per arrow key

export function attach(container, left, handle) {
    if (!container || !left || !handle) return;
    if (attached) return;          // single-instance per page is enough
    attached = true;

    // Restore prior split percentage if the user's set one before.
    try {
        const saved = localStorage.getItem(STORAGE_KEY);
        const p = saved ? parseFloat(saved) : NaN;
        if (Number.isFinite(p) && p >= 10 && p <= 90) {
            left.style.width = p + '%';
        }
    } catch (_) { /* localStorage may be unavailable in private mode */ }

    function bounds() {
        const containerW = container.getBoundingClientRect().width;
        const minP = (MIN_LEFT_PX / containerW) * 100;
        const maxP = ((containerW - MIN_RIGHT_PX - HANDLE_PX) / containerW) * 100;
        return { containerW, minP, maxP };
    }
    function clampPercent(p) {
        const { minP, maxP } = bounds();
        return Math.max(minP, Math.min(maxP, p));
    }
    function persist(p) {
        try { localStorage.setItem(STORAGE_KEY, p.toString()); } catch (_) {}
    }
    function fireResize() {
        // Cytoscape only resizes its renderer when explicitly told.
        window.dispatchEvent(new Event('pf-resize'));
    }

    let dragging = false;
    let startX = 0;
    let startPercent = 0;

    function onDown(e) {
        if (e.button !== undefined && e.button !== 0) return;   // primary button only
        dragging = true;
        startX = e.clientX;
        const containerW = container.getBoundingClientRect().width;
        startPercent = (left.getBoundingClientRect().width / containerW) * 100;
        document.body.classList.add('pf-resizing');
        if (handle.setPointerCapture) handle.setPointerCapture(e.pointerId);
        e.preventDefault();
    }
    function onMove(e) {
        if (!dragging) return;
        const { containerW } = bounds();
        const dx = e.clientX - startX;
        const dPercent = (dx / containerW) * 100;
        const newP = clampPercent(startPercent + dPercent);
        left.style.width = newP + '%';
        fireResize();
    }
    function onUp(e) {
        if (!dragging) return;
        dragging = false;
        document.body.classList.remove('pf-resizing');
        if (handle.releasePointerCapture) handle.releasePointerCapture(e.pointerId);
        const p = parseFloat(left.style.width) || startPercent;
        persist(p);
        fireResize();
    }
    function onDoubleClick() {
        // Reset to the default split (defined by CSS).
        left.style.width = '';
        try { localStorage.removeItem(STORAGE_KEY); } catch (_) {}
        fireResize();
    }
    function onKeyDown(e) {
        if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
        const { containerW } = bounds();
        const cur = (left.getBoundingClientRect().width / containerW) * 100;
        const next = clampPercent(cur + (e.key === 'ArrowLeft' ? -KEY_STEP : KEY_STEP));
        left.style.width = next + '%';
        persist(next);
        fireResize();
        e.preventDefault();
    }

    handle.addEventListener('pointerdown', onDown);
    handle.addEventListener('pointermove', onMove);
    handle.addEventListener('pointerup', onUp);
    handle.addEventListener('pointercancel', onUp);
    handle.addEventListener('dblclick', onDoubleClick);
    handle.addEventListener('keydown', onKeyDown);
}
