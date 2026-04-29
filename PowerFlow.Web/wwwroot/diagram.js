let cy = null;
let _dotNetRef = null;
let _selectedId = null;
let _selectedEdgeIdx = null;

function isDarkMode() {
    const attr = document.documentElement.getAttribute('data-theme');
    if (attr === 'dark')  return true;
    if (attr === 'light') return false;
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

export function render(container, buses, branches, dotNetRef) {
    if (cy) { cy.destroy(); cy = null; }
    _dotNetRef = dotNetRef;
    _selectedId = null;
    _selectedEdgeIdx = null;

    const dark = isDarkMode();
    const n = buses.length;

    const nodes = buses.map(b => ({
        data: { id: String(b.id), label: String(b.id), vm: b.vm, busType: b.busType }
    }));

    const edges = branches.map((br, i) => ({
        data: {
            id: 'e' + i,
            source: String(br.fromBusId),
            target: String(br.toBusId),
            loadingPct: br.loadingPct
        }
    }));

    const layout = n <= 100
        ? { name: 'cose', animate: false, randomize: true, nodeRepulsion: 4096, idealEdgeLength: 50, numIter: 500 }
        : { name: 'random', animate: false };

    cy = cytoscape({
        container,
        elements: { nodes, edges },
        layout,
        style: [
            {
                selector: 'node',
                style: {
                    width: n > 100 ? 10 : n > 50 ? 14 : 18,
                    height: n > 100 ? 10 : n > 50 ? 14 : 18,
                    'background-color': ele => vmColor(ele.data('vm')),
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
                    'border-width': 3,
                    'border-color': '#2563eb',
                    'border-opacity': 1
                }
            },
            {
                selector: 'edge',
                style: {
                    width: 1.5,
                    'line-color': ele => loadingColor(ele.data('loadingPct')),
                    'curve-style': 'bezier',
                    opacity: 0.7
                }
            },
            {
                selector: 'edge:selected',
                style: {
                    width: 3.5,
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
