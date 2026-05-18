import './style.css';
import {
  ApiValidationError,
  contingencyStream,
  listCases,
  loadCase,
  parseCase,
  solve,
  solveStream,
  validate,
} from './api.js';
import type { ContingencyResultDto } from './types.js';
import {
  addBranch,
  addBus,
  addGenerator,
  network,
  removeBranch,
  removeBus,
  removeGenerator,
  scaleLoads,
  totalLoadMw,
  updateBranch,
  updateBus,
  updateGenerator,
} from './network.js';
import type {
  BranchDto,
  BusDto,
  GeneratorDto,
  NetworkDto,
  SolveOptionsDto,
  SolveResultDto,
} from './types.js';
import { mountDiagram } from './ui/diagram.js';
import { mountEditor } from './ui/editor.js';
import { mountHeader } from './ui/header.js';
import { mountKpi } from './ui/kpi.js';
import { mountResults } from './ui/results.js';
import { mountSidebar } from './ui/sidebar.js';

const CLIENT_VERSION = '2.0';

const app = document.getElementById('app');
if (!app) throw new Error('#app not found');

// ── Shell ────────────────────────────────────────────────────────────────────
const headerEl = document.createElement('header');
headerEl.id = 'header';
const bodyEl = document.createElement('div');
bodyEl.id = 'app-body';
const sidebarEl = document.createElement('aside');
sidebarEl.id = 'sidebar';
const mainEl = document.createElement('main');
mainEl.id = 'main';
const footerEl = document.createElement('footer');
footerEl.id = 'footer';

// Main pane: KPI bar / solve strip / diagram (with fit+legend overlays) / editor / results
const kpiEl = document.createElement('div');
kpiEl.id = 'kpi-bar';
const solveStripEl = document.createElement('div');
solveStripEl.id = 'solve-strip';
solveStripEl.hidden = true;
solveStripEl.innerHTML = `
  <span class="strip-badge" id="strip-badge"></span>
  <span class="strip-detail" id="strip-detail"></span>
`;
const stripBadgeEl = solveStripEl.querySelector<HTMLElement>('#strip-badge')!;
const stripDetailEl = solveStripEl.querySelector<HTMLElement>('#strip-detail')!;
const diagramWrap = document.createElement('div');
diagramWrap.id = 'diagram-wrap';
const diagramEl = document.createElement('div');
diagramEl.id = 'diagram';
diagramEl.innerHTML = `<div class="placeholder">Load a case to render the network.</div>`;
const fitBtn = document.createElement('button');
fitBtn.id = 'diagram-fit';
fitBtn.className = 'secondary mini';
fitBtn.title = 'Fit network to view';
fitBtn.textContent = '⤢ Fit';
fitBtn.hidden = true;
const legendEl = document.createElement('div');
legendEl.id = 'diagram-legend';
legendEl.hidden = true;
legendEl.innerHTML = `
  <div class="legend-row"><span class="legend-key">BUSES</span>
    <span class="swatch ok"></span>0.95–1.05 pu
    <span class="swatch warn"></span>over-voltage
    <span class="swatch err"></span>under-voltage
  </div>
  <div class="legend-row"><span class="legend-key">BRANCHES</span>
    <span class="swatch ok"></span>&lt;70%
    <span class="swatch warn"></span>70–90%
    <span class="swatch err"></span>≥90%
  </div>
`;
diagramWrap.append(diagramEl, fitBtn, legendEl);
const editorEl = document.createElement('div');
editorEl.id = 'editor';
const resultsEl = document.createElement('div');
resultsEl.id = 'results-panel';
mainEl.append(kpiEl, solveStripEl, diagramWrap, editorEl, resultsEl);

bodyEl.append(sidebarEl, mainEl);
app.append(headerEl, bodyEl, footerEl);

footerEl.innerHTML = `
  <span class="footer-attribution">Built by <a href="https://github.com/aartinian" target="_blank" rel="noreferrer">aart</a></span>
`;

mountHeader(headerEl, CLIENT_VERSION);

// ── UI components ────────────────────────────────────────────────────────────
const kpi = mountKpi(kpiEl);
const results = mountResults(resultsEl, {
  onContingencyRowClick: (branchIndex) => diagram.focusBranch(branchIndex),
  onGeneratorRowClick: (index) => {
    const net = network.get();
    const gen = net?.generators.find((g) => g.index === index);
    if (gen) editor.showGenerator(gen as GeneratorDto);
  },
});
const editor = mountEditor(editorEl, {
  onBusApply: (busId, patch) => {
    const net = network.get();
    if (!net) return;
    network.set(updateBus(net, busId, patch), 'edit');
    results.setStatus(`Bus ${busId} updated — solve to refresh results.`);
  },
  onBusDelete: (busId) => {
    const net = network.get();
    if (!net) return;
    network.set(removeBus(net, busId), 'topology');
    editor.hide();
    results.setStatus(`Bus ${busId} removed (and linked branches/gens) — solve to refresh.`);
  },
  onBranchApply: (branchIndex, patch) => {
    const net = network.get();
    if (!net) return;
    network.set(updateBranch(net, branchIndex, patch), 'edit');
    const verb = patch.isInService === false ? 'tripped' : 'updated';
    results.setStatus(`Branch #${branchIndex} ${verb} — solve to refresh results.`);
  },
  onBranchDelete: (branchIndex) => {
    const net = network.get();
    if (!net) return;
    network.set(removeBranch(net, branchIndex), 'topology');
    editor.hide();
    results.setStatus(`Branch #${branchIndex} removed — solve to refresh.`);
  },
  onGeneratorApply: (index, patch) => {
    const net = network.get();
    if (!net) return;
    network.set(updateGenerator(net, index, patch), 'edit');
    results.setStatus(`Generator #${index} updated — solve to refresh results.`);
  },
  onGeneratorDelete: (index) => {
    const net = network.get();
    if (!net) return;
    network.set(removeGenerator(net, index), 'topology');
    editor.hide();
    results.setStatus(`Generator #${index} removed — solve to refresh.`);
  },
  onClose: () => {
    /* keep diagram selection as is */
  },
});

const diagram = mountDiagram(diagramEl, (selection) => {
  const net = network.get();
  if (!net) return;
  switch (selection.kind) {
    case 'bus': {
      const bus = net.buses.find((b) => b.id === selection.busId);
      if (bus) editor.showBus(bus as BusDto, bus.type === 'Slack');
      results.selectBus(selection.busId);
      break;
    }
    case 'branch': {
      const branch = net.branches.find((b) => b.index === selection.branchIndex);
      if (branch) editor.showBranch(branch as BranchDto);
      results.selectBranch(selection.branchIndex);
      break;
    }
    case 'none':
      editor.hide();
      results.clearSelection();
      break;
  }
});
fitBtn.addEventListener('click', () => diagram.resetView());

let lastResult: SolveResultDto | null = null;
let solveController: AbortController | null = null;
let contingencyController: AbortController | null = null;

const sidebar = mountSidebar(sidebarEl, {
  onLoadCase: handleLoadCase,
  onUploadCase: handleUploadCase,
  onSolve: handleSolve,
  onCancelSolve: () => solveController?.abort(),
  onContingency: handleContingency,
  onCancelContingency: () => contingencyController?.abort(),
  onAddBus: () => {
    const net = network.get();
    if (!net) return;
    const { net: next, busId } = addBus(net);
    network.set(next, 'topology');
    const newBus = next.buses.find((b) => b.id === busId)!;
    editor.showBus(newBus, false);
    diagram.focusBus(busId);
    results.setStatus(`Added bus ${busId} (PQ, zero load) — edit and solve.`);
  },
  onAddBranch: (fromBusId, toBusId) => {
    const net = network.get();
    if (!net) return;
    const validIds = new Set(net.buses.map((b) => b.id));
    if (!validIds.has(fromBusId) || !validIds.has(toBusId)) {
      results.setStatus(`Branch not added — bus ${validIds.has(fromBusId) ? toBusId : fromBusId} doesn't exist.`, 'error');
      return;
    }
    const { net: next, index } = addBranch(net, fromBusId, toBusId);
    network.set(next, 'topology');
    const newBranch = next.branches.find((b) => b.index === index)!;
    editor.showBranch(newBranch);
    diagram.focusBranch(index);
    results.setStatus(`Added branch ${fromBusId}→${toBusId} (default R/X) — tune and solve.`);
  },
  onAddGenerator: (busId) => {
    const net = network.get();
    if (!net) return;
    if (!net.buses.some((b) => b.id === busId)) {
      results.setStatus(`Generator not added — bus ${busId} doesn't exist.`, 'error');
      return;
    }
    const { net: next, index } = addGenerator(net, busId);
    network.set(next, 'topology');
    const newGen = next.generators.find((g) => g.index === index)!;
    editor.showGenerator(newGen);
    results.setStatus(`Added generator at bus ${busId} — tune and solve.`);
  },
});

function showSolveStrip(r: SolveResultDto | null): void {
  if (!r) {
    solveStripEl.hidden = true;
    return;
  }
  solveStripEl.hidden = false;
  solveStripEl.classList.remove('stale');
  stripBadgeEl.textContent = r.converged ? 'Converged' : 'Diverged';
  stripBadgeEl.className = `strip-badge ${r.converged ? 'ok' : 'error'}`;
  const iterLabel = r.mode === 'DC' ? 'DC' : `${r.iterations} iter`;
  stripDetailEl.textContent = `${iterLabel} · max |F| ${r.maxMismatch.toExponential(2)} pu`;
}

function setStale(stale: boolean): void {
  kpi.setStale(stale);
  if (!solveStripEl.hidden) solveStripEl.classList.toggle('stale', stale);
}

network.subscribe((net, kind) => {
  sidebar.setSolveEnabled(net !== null);
  sidebar.setBaseLoad(net ? totalLoadMw(net) : null);
  kpi.setNetwork(net);
  if (kind === 'load') {
    diagram.setNetwork(net);
    lastResult = null;
    editor.hide();
    showSolveStrip(null);
    kpi.setResult(null);
    setStale(false);
  } else if (kind === 'topology' && net) {
    diagram.applyTopology(net);
    if (lastResult) setStale(true);
  } else if (net) {
    diagram.applyEdit(net);
    // Any edit invalidates the displayed result — flag stale so the user
    // can tell at a glance that KPIs / Result block reflect old numbers.
    if (lastResult) setStale(true);
  }
  fitBtn.hidden = net === null;
  legendEl.hidden = net === null;
  if (net === null) {
    results.clear();
    diagramEl.querySelector('.placeholder')?.removeAttribute('hidden');
  } else {
    const ph = diagramEl.querySelector('.placeholder');
    if (ph) ph.setAttribute('hidden', '');
  }
});

window.addEventListener('resize', () => diagram.resize());

bootstrap().catch((err: unknown) => {
  results.setStatus(`Init failed: ${String(err)}`, 'error');
});

async function bootstrap(): Promise<void> {
  const cases = await listCases();
  sidebar.setCases(cases);
}

async function handleLoadCase(id: string): Promise<void> {
  results.setStatus(`Loading ${id}…`);
  try {
    const net = await loadCase(id);
    network.set(net);
    sidebar.setActiveCase(id);
    afterNetworkLoad(net, id);
  } catch (err) {
    results.setStatus(`Load failed: ${String(err)}`, 'error');
  }
}

async function handleUploadCase(filename: string, content: string): Promise<void> {
  results.setStatus(`Parsing ${filename}…`);
  try {
    const net = await parseCase(content);
    network.set(net);
    sidebar.setActiveCase(null);
    afterNetworkLoad(net, filename);
  } catch (err) {
    results.setStatus(`Parse failed: ${String(err)}`, 'error');
  }
}

async function afterNetworkLoad(net: NetworkDto, label: string): Promise<void> {
  const validation = await validate(net);
  if (!validation.isValid) {
    results.setStatus(`${label} loaded with ${validation.errors.length} error(s)`, 'error');
    results.showValidation(validation);
    return;
  }
  results.setStatus(
    `${net.name ?? label} — ${net.buses.length} buses, ${net.branches.length} branches, ${net.generators.length} generators`,
    'ok',
  );
}

async function handleSolve(options: SolveOptionsDto): Promise<void> {
  const base = network.get();
  if (!base) return;
  const scale = sidebar.getLoadScale();
  const net = scaleLoads(base, scale);
  const controller = new AbortController();
  solveController = controller;
  sidebar.setSolveBusy(true);
  results.setStatus(scale === 1 ? 'Solving…' : `Solving at ${Math.round(scale * 100)}% load…`);
  try {
    const result =
      options.mode === 'AC'
        ? await runAcStream(net, options, controller.signal)
        : await solve({ network: net, options }, controller.signal);
    lastResult = result;
    diagram.setSolveResult(result);
    results.showSolve(result);
    kpi.setResult(result);
    showSolveStrip(result);
    setStale(false);
    results.setStatus(
      result.converged
        ? `Converged in ${result.iterations} iteration(s).`
        : `Did not converge after ${result.iterations} iteration(s).`,
      result.converged ? 'ok' : 'error',
    );
  } catch (err) {
    if (controller.signal.aborted) {
      results.setStatus('Solve cancelled.', 'muted');
    } else if (err instanceof ApiValidationError) {
      results.showValidation(err.result);
      results.setStatus(`Validation failed (${err.result.errors.length} error(s))`, 'error');
    } else {
      results.setStatus(`Solve failed: ${String(err)}`, 'error');
    }
  } finally {
    if (solveController === controller) solveController = null;
    sidebar.setSolveBusy(false);
  }
}

async function handleContingency(options: SolveOptionsDto): Promise<void> {
  const base = network.get();
  if (!base) return;
  const scale = sidebar.getLoadScale();
  const net = scaleLoads(base, scale);
  const controller = new AbortController();
  contingencyController = controller;
  sidebar.setContingencyBusy(true);
  const inSvc = net.branches.filter((b) => b.isInService).length;
  results.setStatus(`Running N-1 sweep over ${inSvc} branch(es)…`);

  // Accumulate rows as they arrive; render after each row so the user sees
  // progress instead of waiting for the entire sweep to finish.
  const rows: ContingencyResultDto[] = [];
  let total = inSvc;
  try {
    await contingencyStream(
      { network: net, options },
      (event) => {
        if (event.type === 'row') {
          rows.push(event.row);
          results.showContingency([...rows]);
          results.setStatus(`Sweep in progress: ${rows.length} / ${total} branches…`);
        } else if (event.type === 'complete') {
          total = event.total;
        }
      },
      controller.signal,
    );
    results.showContingency([...rows]);
    const worst = [...rows].sort((a, b) =>
      a.converged === b.converged
        ? (b.maxLoadingPct ?? 0) - (a.maxLoadingPct ?? 0)
        : Number(!a.converged) - Number(!b.converged),
    )[0];
    if (!worst) {
      results.setStatus('Sweep returned no contingencies.', 'muted');
    } else if (!worst.converged) {
      results.setStatus(
        `Sweep complete: ${rows.filter((r) => !r.converged).length} contingency(s) diverged — most severe at top.`,
        'error',
      );
    } else {
      results.setStatus(
        `Sweep complete: ${rows.length} contingencies, worst max loading ${worst.maxLoadingPct?.toFixed(1) ?? '—'}%.`,
        'ok',
      );
    }
  } catch (err) {
    if (controller.signal.aborted) {
      results.setStatus(`Sweep cancelled at ${rows.length} / ${total}.`, 'muted');
      if (rows.length > 0) results.showContingency([...rows]);
    } else if (err instanceof ApiValidationError) {
      results.showValidation(err.result);
      results.setStatus(`Validation failed (${err.result.errors.length} error(s))`, 'error');
    } else {
      results.setStatus(`Sweep failed: ${String(err)}`, 'error');
    }
  } finally {
    if (contingencyController === controller) contingencyController = null;
    sidebar.setContingencyBusy(false);
  }
}

async function runAcStream(
  net: NetworkDto,
  options: SolveOptionsDto,
  signal: AbortSignal,
): Promise<SolveResultDto> {
  results.beginStream(options.tolerance);
  return new Promise<SolveResultDto>((resolve, reject) => {
    let settled = false;
    solveStream(
      { network: net, options },
      (event) => {
        switch (event.type) {
          case 'iter':
            results.pushIteration(event.iter, event.mismatch, event.busTypeChanges);
            break;
          case 'result':
            settled = true;
            resolve(event.result);
            break;
          case 'error':
            settled = true;
            reject(new Error(event.message));
            break;
        }
      },
      signal,
    ).then(
      () => {
        if (!settled) reject(new Error('Stream ended without a result event'));
      },
      (err: unknown) => {
        if (!settled) reject(err instanceof Error ? err : new Error(String(err)));
      },
    );
  });
}

void lastResult;
