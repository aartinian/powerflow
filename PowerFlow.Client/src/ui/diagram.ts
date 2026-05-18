import cytoscape from 'cytoscape';
import type { Core, ElementDefinition } from 'cytoscape';
import type { NetworkDto, SolveResultDto } from '../types.js';

export type DiagramSelection =
  | { kind: 'bus'; busId: number }
  | { kind: 'branch'; branchIndex: number }
  | { kind: 'none' };

export interface DiagramHandle {
  setNetwork(network: NetworkDto | null): void;
  applyEdit(network: NetworkDto): void;
  applyTopology(network: NetworkDto): void;
  setSolveResult(result: SolveResultDto | null): void;
  focusBus(busId: number): void;
  focusBranch(branchIndex: number): void;
  resetView(): void;
  resize(): void;
  zoomIn(): void;
  zoomOut(): void;
  destroy(): void;
}

// Cytoscape-based network diagram. Ported from PowerFlow.Web/wwwroot/diagram.js.
// Visual tiers (node size, edge style, render hints) follow the same thresholds
// as the original — kept intentionally tuned for 50 / 200 / 400 / 600 bus
// breakpoints so the case14 → case300 spectrum stays readable on first paint.
export function mountDiagram(
  container: HTMLElement,
  onSelect: (selection: DiagramSelection) => void,
): DiagramHandle {
  let cy: Core | null = null;
  let currentNet: NetworkDto | null = null;

  function buildElements(network: NetworkDto): ElementDefinition[] {
    const genBusIds = new Set(network.generators.map((g) => g.busId));
    const nodes: ElementDefinition[] = network.buses.map((b) => ({
      group: 'nodes',
      data: { id: `b${b.id}`, label: String(b.id), busId: b.id, busType: b.type, hasGen: genBusIds.has(b.id) },
    }));
    // Include out-of-service branches too — they render dashed so the user
    // can tell what was tripped, and the edge can be toggled back without
    // rebuilding the graph and losing positions.
    const edges: ElementDefinition[] = network.branches.map((br) => ({
      group: 'edges',
      data: {
        id: `e${br.index}`,
        source: `b${br.fromBusId}`,
        target: `b${br.toBusId}`,
        branchIndex: br.index,
        oos: !br.isInService,
        isTx: br.tapRatio !== 0,
        flowDir: 0,
      },
    }));
    return [...nodes, ...edges];
  }

  function render(network: NetworkDto): void {
    if (cy) {
      try {
        cy.destroy();
      } catch {
        // Old container already gone — destroying the dead instance can throw;
        // safe to swallow because we're about to replace it anyway.
      }
      cy = null;
    }
    if (!container.isConnected) return;

    const n = network.buses.length;
    const dark = matchMedia('(prefers-color-scheme: dark)').matches;
    const layout = pickLayout(n);
    const tier = pickTier(n);

    cy = cytoscape({
      container,
      elements: buildElements(network),
      layout,
      textureOnViewport: n > 200,
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
            width: tier.nodeSize,
            height: tier.nodeSize,
            'background-color': (ele) => nodeColor(ele.data('vm'), ele.data('vmTone')),
            label: n <= 57 ? 'data(label)' : '',
            'font-size': n <= 14 ? 10 : n <= 57 ? 9 : 8,
            color: dark ? '#94a3b8' : '#334155',
            'text-background-color': dark ? '#162035' : '#ffffff',
            'text-background-opacity': dark ? 0.65 : 0,
            'text-background-padding': '2px',
            'text-valign': 'center',
            'text-halign': 'right',
            'text-margin-x': 4,
          },
        },
        {
          selector: 'node:selected',
          style: {
            'border-width': n > 300 ? 2 : 3,
            'border-color': '#2563eb',
            'border-opacity': 1,
          },
        },
        {
          selector: 'node[?hasGen]',
          style: {
            'border-width': n > 300 ? 1 : 2,
            'border-color': '#8b5cf6',
            'border-opacity': 1,
          },
        },
        {
          selector: 'node[?hasGen]:selected',
          style: { 'border-color': '#2563eb' },
        },
        {
          selector: 'edge',
          style: {
            width: tier.edgeWidth,
            'line-color': (ele) => loadingColor(ele.data('loadingPct')),
            'curve-style': tier.curveStyle,
            opacity: tier.edgeOpacity,
          },
        },
        {
          selector: 'edge:selected',
          style: {
            width: Math.max(tier.edgeWidth * 2.3, 2.5),
            opacity: 1,
            'line-color': '#2563eb',
          },
        },
        {
          selector: 'edge[?isTx]',
          style: {
            width: tier.edgeWidth * 1.6,
            'line-color': (ele) => {
              const pct = ele.data('loadingPct') as number | null | undefined;
              return typeof pct === 'number' && !Number.isNaN(pct) ? loadingColor(pct) : '#60a5fa';
            },
          },
        },
        {
          selector: 'edge[?oos]',
          style: {
            'line-style': 'dashed',
            'line-color': '#475569',
            opacity: 0.5,
          },
        },
        {
          selector: 'edge[flowDir = 1]',
          style: {
            'mid-target-arrow-shape': 'triangle',
            'mid-target-arrow-color': (ele) => loadingColor(ele.data('loadingPct')),
            'arrow-scale': 0.9,
          },
        },
        {
          selector: 'edge[flowDir = -1]',
          style: {
            'mid-source-arrow-shape': 'triangle',
            'mid-source-arrow-color': (ele) => loadingColor(ele.data('loadingPct')),
            'arrow-scale': 0.9,
          },
        },
      ],
    });

    cy.on('tap', 'node', (evt) => {
      const node = evt.target;
      cy!.elements().unselect();
      node.select();
      onSelect({ kind: 'bus', busId: node.data('busId') as number });
    });
    cy.on('tap', 'edge', (evt) => {
      const edge = evt.target;
      cy!.elements().unselect();
      edge.select();
      onSelect({ kind: 'branch', branchIndex: edge.data('branchIndex') as number });
    });
    cy.on('tap', (evt) => {
      if (evt.target === cy) {
        cy!.elements().unselect();
        onSelect({ kind: 'none' });
      }
    });
  }

  // Pushes the solve result's vm/loadingPct (and DC tone) onto the existing
  // graph, then restyles in place. Avoids rebuilding the layout, so the user
  // doesn't lose their pan/zoom or watch nodes jitter into new positions.
  function applyResult(result: SolveResultDto): void {
    if (!cy) return;
    const isDc = result.mode === 'DC';

    const busVm = new Map<number, number | null>();
    let minVa = Number.POSITIVE_INFINITY;
    let maxVa = Number.NEGATIVE_INFINITY;
    for (const b of result.buses) {
      busVm.set(b.busId, b.vm);
      if (b.va < minVa) minVa = b.va;
      if (b.va > maxVa) maxVa = b.va;
    }
    // DC: bucket Va deviation into 0/1/2 severity. Threshold ratios chosen
    // empirically to keep most case14 buses at "fine" and stress only the
    // tail. AC ignores this and uses vm directly.
    const vaSpan = Math.max(1e-6, maxVa - minVa);
    const busTone = new Map<number, number>();
    if (isDc) {
      for (const b of result.buses) {
        const frac = Math.abs(b.va - (maxVa + minVa) / 2) / (vaSpan / 2);
        busTone.set(b.busId, frac > 0.66 ? 2 : frac > 0.33 ? 1 : 0);
      }
    }

    cy.nodes().forEach((node) => {
      const id = node.data('busId') as number;
      node.data('vm', isDc ? null : (busVm.get(id) ?? null));
      node.data('vmTone', busTone.get(id) ?? 0);
    });

    const edgeLoading = new Map<number, number | null>();
    const edgeFlowDir = new Map<number, number>();
    for (const br of result.branches) {
      edgeLoading.set(br.branchIndex, br.loadingPct);
      edgeFlowDir.set(br.branchIndex, br.pij > 0 ? 1 : br.pij < 0 ? -1 : 0);
    }
    cy.edges().forEach((edge) => {
      const idx = edge.data('branchIndex') as number;
      edge.data('loadingPct', edgeLoading.get(idx) ?? null);
      edge.data('flowDir', edgeFlowDir.get(idx) ?? 0);
    });

    cy.style().update();
  }

  function clearResult(): void {
    if (!cy) return;
    cy.nodes().forEach((node) => {
      node.data('vm', null);
      node.data('vmTone', 0);
    });
    cy.edges().forEach((edge) => {
      edge.data('loadingPct', null);
      edge.data('flowDir', 0);
    });
    cy.style().update();
  }

  return {
    setNetwork(network) {
      currentNet = network;
      if (network) render(network);
      else if (cy) {
        cy.destroy();
        cy = null;
      }
    },
    // Patch existing nodes/edges in place. Caller guarantees the topology
    // (bus IDs, branch indices) is unchanged from setNetwork — we only walk
    // the existing elements and refresh their data fields. Avoids re-running
    // the layout so the user's edit doesn't reshuffle the whole diagram.
    applyEdit(net) {
      currentNet = net;
      if (!cy) {
        render(net);
        return;
      }
      const genBusIds = new Set(net.generators.map((g) => g.busId));
      const branchById = new Map(net.branches.map((b) => [b.index, b]));
      const busById = new Map(net.buses.map((b) => [b.id, b]));
      cy.nodes().forEach((node) => {
        const b = busById.get(node.data('busId') as number);
        if (b) {
          node.data('busType', b.type);
          node.data('hasGen', genBusIds.has(b.id));
        }
      });
      cy.edges().forEach((edge) => {
        const br = branchById.get(edge.data('branchIndex') as number);
        if (br) edge.data('oos', !br.isInService);
      });
      cy.style().update();
    },
    setSolveResult(result) {
      if (!cy && currentNet) render(currentNet);
      if (!cy) return;
      if (result) applyResult(result);
      else clearResult();
    },
    focusBus(busId) {
      if (!cy) return;
      const node = cy.$(`#b${busId}`);
      if (node.empty()) return;
      cy.elements().unselect();
      node.select();
      cy.animate({ center: { eles: node }, duration: 250 });
      onSelect({ kind: 'bus', busId });
    },
    focusBranch(branchIndex) {
      if (!cy) return;
      const edge = cy.$(`#e${branchIndex}`);
      if (edge.empty()) return;
      cy.elements().unselect();
      edge.select();
      cy.animate({ center: { eles: edge }, duration: 250 });
      onSelect({ kind: 'branch', branchIndex });
    },
    // Topology changed (element added/removed). Re-render to refresh element
    // sets — layout re-runs, so positions reshuffle. Acceptable for v1 since
    // the alternative (incremental add/remove with preserved layout) requires
    // significant cytoscape plumbing.
    applyTopology(net) {
      currentNet = net;
      render(net);
    },
    resetView() {
      if (cy) cy.fit(undefined, 30);
    },
    resize() {
      if (cy) cy.resize();
    },
    zoomIn() {
      if (cy) cy.zoom({ level: cy.zoom() * 1.3, renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 } });
    },
    zoomOut() {
      if (cy) cy.zoom({ level: cy.zoom() / 1.3, renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 } });
    },
    destroy() {
      if (cy) {
        cy.destroy();
        cy = null;
      }
      currentNet = null;
    },
  };
}

// ── Layout & visual tiering ─────────────────────────────────────────────────

function pickLayout(n: number): cytoscape.LayoutOptions {
  // Above ~600 buses cose is too slow on first paint, so fall back to a
  // concentric layout that at least groups slack/PV/PQ buses into rings.
  if (n <= 100)
    return { name: 'cose', animate: false, randomize: true, nodeRepulsion: () => 4096, idealEdgeLength: () => 50, numIter: 500 };
  if (n <= 300)
    return { name: 'cose', animate: false, randomize: true, nodeRepulsion: () => 8192, idealEdgeLength: () => 40, numIter: 250, gravity: 0.6 };
  if (n <= 600)
    return { name: 'cose', animate: false, randomize: true, nodeRepulsion: () => 12000, idealEdgeLength: () => 30, numIter: 120, gravity: 0.8 };
  return {
    name: 'concentric',
    animate: false,
    padding: 30,
    spacingFactor: 0.7,
    concentric: (ele) => {
      const t = ele.data('busType') as string;
      return t === 'Slack' ? 3 : t === 'PV' ? 2 : 1;
    },
    levelWidth: () => 1,
  };
}

interface VisualTier {
  nodeSize: number;
  edgeWidth: number;
  edgeOpacity: number;
  curveStyle: 'haystack' | 'bezier';
}

function pickTier(n: number): VisualTier {
  // edgeOpacity / curveStyle break at 200 while node sizes step at 50/100;
  // intentional — haystack edges are the cheapest perf win and kick in earlier.
  if (n > 500) return { nodeSize: 5, edgeWidth: 0.6, edgeOpacity: 0.25, curveStyle: 'haystack' };
  if (n > 300) return { nodeSize: 7, edgeWidth: 0.9, edgeOpacity: 0.4, curveStyle: 'haystack' };
  if (n > 200) return { nodeSize: 10, edgeWidth: 1.1, edgeOpacity: 0.4, curveStyle: 'haystack' };
  if (n > 100) return { nodeSize: 10, edgeWidth: 1.1, edgeOpacity: 0.55, curveStyle: 'bezier' };
  if (n > 50) return { nodeSize: 14, edgeWidth: 1.5, edgeOpacity: 0.7, curveStyle: 'bezier' };
  return { nodeSize: 18, edgeWidth: 1.5, edgeOpacity: 0.7, curveStyle: 'bezier' };
}

// ── Colour helpers ──────────────────────────────────────────────────────────

function nodeColor(vm: number | null | undefined, vmTone: number | undefined): string {
  if (typeof vm === 'number') {
    if (vm < 0.95) return '#ef4444';
    if (vm > 1.05) return '#f59e0b';
    return '#22c55e';
  }
  // DC path or pre-solve. Tone undefined / 0 ⇒ neutral.
  if (vmTone === 2) return '#ef4444';
  if (vmTone === 1) return '#f59e0b';
  return '#94a3b8';
}

function loadingColor(pct: number | null | undefined): string {
  if (typeof pct !== 'number' || Number.isNaN(pct)) return '#94a3b8';
  if (pct >= 90) return '#ef4444';
  if (pct >= 70) return '#f59e0b';
  return '#22c55e';
}
