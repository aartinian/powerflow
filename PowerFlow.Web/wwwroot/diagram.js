let cy = null;
let _dotNetRef = null;
let _selectedId = null;
let _selectedEdgeIdx = null;

// External callers (e.g. the split-pane divider) can dispatch this event to
// force Cytoscape to recompute its viewport — Cytoscape doesn't watch the
// container element's size automatically.
window.addEventListener('pf-resize', () => { if (cy) cy.resize(); });

function isDarkMode() {
    const attr = document.documentElement.getAttribute('data-theme');
    if (attr === 'dark')  return true;
    if (attr === 'light') return false;
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

export function render(container, buses, branches, dotNetRef, mode) {
    // Tear down any previous instance. Wrap in try/catch because destroy() can
    // throw if its container was already detached from the DOM (which happens
    // when Blazor unmounts the diagram block during mode switches).
    if (cy) {
        try { cy.destroy(); } catch (_) { /* swallow — old instance is dead either way */ }
        cy = null;
    }

    // Bail out if the container isn't a live DOM element. Cytoscape's initRenderer
    // reads container.className unconditionally; a null or detached node throws
    // "null is not an object (evaluating 'n.className')" deep in the library.
    if (!container || !container.parentNode || !container.isConnected) return;

    _dotNetRef = dotNetRef;
    _selectedId = null;
    _selectedEdgeIdx = null;

    const isDc = mode === 'dc';
    const dark = isDarkMode();
    const n = buses.length;

    const nodes = buses.map(b => ({
        // For AC, vm is the voltage magnitude (used by vmColor).
        // For DC, vmTone is a pre-computed angle-deviation severity 0..2 used by dcColor.
        data: { id: String(b.id), label: String(b.id), vm: b.vm, vmTone: b.vmTone, busType: b.busType }
    }));

    const edges = branches.map((br, i) => ({
        data: {
            id: 'e' + i,
            source: String(br.fromBusId),
            target: String(br.toBusId),
            loadingPct: br.loadingPct
        }
    }));

    // Layout: tuned by graph size. `random` is never useful — it produces a
    // hairball with no structure. We always run cose, but with progressively
    // cheaper parameters as n grows. Above ~600, cose is too slow to be
    // pleasant on first paint, so fall back to a concentric layout that at
    // least groups slack/PV/PQ buses into rings.
    const layout =
        n <= 100  ? { name: 'cose', animate: false, randomize: true, nodeRepulsion: 4096, idealEdgeLength: 50,  numIter: 500 } :
        n <= 300  ? { name: 'cose', animate: false, randomize: true, nodeRepulsion: 8192, idealEdgeLength: 40,  numIter: 250, gravity: 0.6 } :
        n <= 600  ? { name: 'cose', animate: false, randomize: true, nodeRepulsion: 12000, idealEdgeLength: 30, numIter: 120, gravity: 0.8 } :
                    { name: 'concentric', animate: false, padding: 30, spacingFactor: 0.7,
                      concentric: ele => ele.data('busType') === 3 ? 3 : ele.data('busType') === 2 ? 2 : 1,
                      levelWidth: () => 1 };

    // Visual scale tiers. Dense graphs need smaller nodes, thinner edges,
    // lower opacity so the structure isn't drowned in ink.
    const nodeSize = n > 500 ? 5 : n > 300 ? 7 : n > 100 ? 10 : n > 50 ? 14 : 18;
    const edgeWidth = n > 500 ? 0.6 : n > 300 ? 0.9 : n > 100 ? 1.1 : 1.5;
    const edgeOpacity = n > 500 ? 0.25 : n > 200 ? 0.4 : n > 100 ? 0.55 : 0.7;
    // `haystack` skips control-point math entirely — much cheaper than bezier
    // for large straight-line edge sets. Lose curved aesthetics; gain frame rate.
    const curveStyle = n > 200 ? 'haystack' : 'bezier';

    cy = cytoscape({
        container,
        elements: { nodes, edges },
        layout,
        // Performance hints kick in tiered. Below ~200 buses everything stays
        // crisp; above that we trade visual fidelity (during pan/zoom only)
        // for smooth interaction.
        textureOnViewport:   n > 200,
        hideEdgesOnViewport: n > 400,
        hideLabelsOnViewport: n > 100,
        pixelRatio: n > 400 ? 1 : 'auto',
        motionBlur: false,
        wheelSensitivity: 0.2,
        minZoom: 0.05,
        maxZoom: 4,
        style: [
            {
                selector: 'node',
                style: {
                    width: nodeSize,
                    height: nodeSize,
                    'background-color': ele => isDc ? dcColor(ele.data('vmTone')) : vmColor(ele.data('vm')),
                    label: n <= 57 ? 'data(label)' : '',
                    'font-size': 8,
                    color: dark ? '#94a3b8' : '#334155',
                    'text-background-color': dark ? '#162035' : '#ffffff',
                    'text-background-opacity': dark ? 0.65 : 0,
                    'text-background-padding': '2px',
                    'text-valign': 'center',
                    'text-halign': 'right',
                    'text-margin-x': 4
                }
            },
            {
                selector: 'node:selected',
                style: {
                    'border-width': n > 300 ? 2 : 3,
                    'border-color': '#2563eb',
                    'border-opacity': 1
                }
            },
            {
                selector: 'edge',
                style: {
                    width: edgeWidth,
                    'line-color': ele => loadingColor(ele.data('loadingPct')),
                    'curve-style': curveStyle,
                    opacity: edgeOpacity
                }
            },
            {
                selector: 'edge:selected',
                style: {
                    width: Math.max(edgeWidth * 2.3, 2.5),
                    opacity: 1,
                    'line-color': '#2563eb'
                }
            }
        ]
    });

    // Node tap
    cy.on('tap', 'node', (evt) => {
        const node = evt.target;
        const id = parseInt(node.id());
        _selectedEdgeIdx = null;
        if (id === _selectedId) {
            _selectedId = null;
            cy.elements().unselect();
            _dotNetRef?.invokeMethodAsync('OnBackgroundTap');
        } else {
            _selectedId = id;
            cy.elements().unselect();
            node.select();
            _dotNetRef?.invokeMethodAsync('OnNodeTap', id);
        }
    });

    // Edge tap
    cy.on('tap', 'edge', (evt) => {
        const edge = evt.target;
        const idx = parseInt(edge.id().substring(1)); // 'e3' → 3
        _selectedId = null;
        if (idx === _selectedEdgeIdx) {
            _selectedEdgeIdx = null;
            cy.elements().unselect();
            _dotNetRef?.invokeMethodAsync('OnBackgroundTap');
        } else {
            _selectedEdgeIdx = idx;
            cy.elements().unselect();
            edge.select();
            _dotNetRef?.invokeMethodAsync('OnEdgeTap', idx);
        }
    });

    // Background tap
    cy.on('tap', (evt) => {
        if (evt.target === cy) {
            _selectedId = null;
            _selectedEdgeIdx = null;
            cy.elements().unselect();
            _dotNetRef?.invokeMethodAsync('OnBackgroundTap');
        }
    });
}

export function resetZoom() {
    if (cy) cy.fit(null, 30);
}

export function unselectAll() {
    if (cy) {
        cy.elements().unselect();
        _selectedId = null;
        _selectedEdgeIdx = null;
    }
}

export function destroy() {
    if (cy) { cy.destroy(); cy = null; }
    _dotNetRef = null;
    _selectedId = null;
    _selectedEdgeIdx = null;
}

function vmColor(vm) {
    if (vm == null) return '#94a3b8';
    if (vm < 0.95) return '#ef4444';
    if (vm > 1.05) return '#f59e0b';
    return '#22c55e';
}

function loadingColor(pct) {
    if (pct == null || isNaN(pct)) return '#94a3b8';
    if (pct >= 90) return '#ef4444';
    if (pct >= 70) return '#f59e0b';
    return '#22c55e';
}

// DC: voltage magnitudes are 1 pu by assumption, so colour by |Va| deviation
// from the slack instead. Severity passed in pre-bucketed: 0=fine, 1=watch, 2=stressed.
function dcColor(tone) {
    if (tone === 2) return '#ef4444';
    if (tone === 1) return '#f59e0b';
    return '#22c55e';
}
