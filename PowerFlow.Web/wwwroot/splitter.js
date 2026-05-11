// splitter.js — drag-resizable vertical divider between two side-by-side panes.
//
// The left pane's inline `width` (as a percentage of the container) is mutated
// on drag; the right pane uses flex:1 to fill the remainder. Percentages —
// not pixels — so the split ratio survives window resizes correctly.
//
// Cytoscape doesn't auto-react to container resize, so we dispatch a
// `pf-resize` event on every move. diagram.js listens for it and calls
// cy.resize().
//
// Re-attach safe: when Blazor switches cases the workspace DOM is torn down and
// rebuilt. attach() tracks the active handle element; if a different element is
// passed it tears down the old listeners and wires up the new ones so the
// splitter keeps working across case changes.

const STORAGE_KEY  = 'pf-diagram-w-pct';
const MIN_LEFT_PX  = 360;
const MIN_RIGHT_PX = 320;
const HANDLE_PX    = 10;
const KEY_STEP     = 2.5;   // %, per arrow key

let _handle    = null;
let _listeners = null;

export function attach(container, left, handle) {
    if (!container || !left || !handle) return;

    if (_handle === handle) return;

    if (_listeners !== null) {
        const { onDown, onMove, onUp, onDoubleClick, onKeyDown } = _listeners;
        _handle.removeEventListener('pointerdown',   onDown);
        _handle.removeEventListener('pointermove',   onMove);
        _handle.removeEventListener('pointerup',     onUp);
        _handle.removeEventListener('pointercancel', onUp);
        _handle.removeEventListener('dblclick',      onDoubleClick);
        _handle.removeEventListener('keydown',       onKeyDown);
    }

    _handle = handle;

    try {
        const saved = localStorage.getItem(STORAGE_KEY);
        const p = saved ? parseFloat(saved) : NaN;
        if (Number.isFinite(p) && p >= 10 && p <= 90) {
            left.style.width = p + '%';
        }
    } catch (_) { /* localStorage may be unavailable in private mode */ }

    function clamp(p, containerW) {
        const minP = (MIN_LEFT_PX / containerW) * 100;
        const maxP = ((containerW - MIN_RIGHT_PX - HANDLE_PX) / containerW) * 100;
        return Math.max(minP, Math.min(maxP, p));
    }
    function persist(p) {
        try { localStorage.setItem(STORAGE_KEY, p.toString()); } catch (_) {}
    }
    function fireResize() {
        window.dispatchEvent(new Event('pf-resize'));
    }

    let dragging = false;
    let startX = 0;
    let startPercent = 0;
    // Container width is constant for the lifetime of a single drag — cache it
    // at pointerdown so onMove never forces a layout flush inside the tight loop.
    let dragContainerW = 0;

    function onDown(e) {
        if (e.button !== 0) return;   // primary button only
        dragging = true;
        startX = e.clientX;
        dragContainerW = container.getBoundingClientRect().width;
        startPercent = (left.getBoundingClientRect().width / dragContainerW) * 100;
        document.body.classList.add('pf-resizing');
        if (handle.setPointerCapture) handle.setPointerCapture(e.pointerId);
        e.preventDefault();
    }
    function onMove(e) {
        if (!dragging) return;
        const dx = e.clientX - startX;
        const dPercent = (dx / dragContainerW) * 100;
        const newP = clamp(startPercent + dPercent, dragContainerW);
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
        left.style.width = '';
        try { localStorage.removeItem(STORAGE_KEY); } catch (_) {}
        fireResize();
    }
    function onKeyDown(e) {
        if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
        const cw = container.getBoundingClientRect().width;
        // left.style.width is already a percentage string set by this module —
        // parse it directly rather than forcing a layout read via getBoundingClientRect.
        // Fall back to getBoundingClientRect only on the first key press if no
        // inline style is set yet (CSS default + no localStorage entry).
        const cur = parseFloat(left.style.width) ||
                    (left.getBoundingClientRect().width / cw) * 100;
        const next = clamp(cur + (e.key === 'ArrowLeft' ? -KEY_STEP : KEY_STEP), cw);
        left.style.width = next + '%';
        persist(next);
        fireResize();
        e.preventDefault();
    }

    handle.addEventListener('pointerdown',   onDown);
    handle.addEventListener('pointermove',   onMove);
    handle.addEventListener('pointerup',     onUp);
    handle.addEventListener('pointercancel', onUp);
    handle.addEventListener('dblclick',      onDoubleClick);
    handle.addEventListener('keydown',       onKeyDown);

    _listeners = { onDown, onMove, onUp, onDoubleClick, onKeyDown };
}
