import './style.css';
import {
  ApiValidationError,
  contingency,
  listCases,
  loadCase,
  parseCase,
  solve,
  solveStream,
  validate,
} from './api.js';
import {
  network,
  scaleLoads,
  totalLoadMw,
  updateBranch,
  updateBus,
} from './network.js';
import type { BranchDto, BusDto, NetworkDto, SolveOptionsDto, SolveResultDto } from './types.js';
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

// Main pane: KPI bar / diagram (with fit+legend overlays) / editor / results
const kpiEl = document.createElement('div');
kpiEl.id = 'kpi-bar';
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
mainEl.append(kpiEl, diagramWrap, editorEl, resultsEl);

bodyEl.append(sidebarEl, mainEl);
app.append(headerEl, bodyEl, footerEl);

footerEl.innerHTML = `
  <div class="footer-left">Built by <a href="https://github.com/aartinian" target="_blank" rel="noreferrer">aart</a> · <a href="https://github.com/aartinian/powerflow" target="_blank" rel="noreferrer">github.com/aartinian/powerflow</a></div>
  <div class="footer-right">
    <span class="tech-pill">C# · .NET 10</span>
    <span class="tech-pill">ASP.NET Minimal API</span>
    <span class="tech-pill">CSparse (sparse LU)</span>
    <span class="tech-pill">TypeScript + Vite</span>
    <span class="tech-pill">Cytoscape.js</span>
  </div>
`;

mountHeader(headerEl, CLIENT_VERSION);

// ── UI components ────────────────────────────────────────────────────────────
const kpi = mountKpi(kpiEl);
const results = mountResults(resultsEl, {
  onContingencyRowClick: (branchIndex) => diagram.focusBranch(branchIndex),
});
const editor = mountEditor(editorEl, {
  onBusApply: (busId, patch) => {
    const net = network.get();
    if (!net) return;
    network.set(updateBus(net, busId, patch), 'edit');
    results.setStatus(`Bus ${busId} updated — solve to refresh results.`);
  },
  onBranchApply: (branchIndex, patch) => {
    const net = network.get();
    if (!net) return;
    network.set(updateBranch(net, branchIndex, patch), 'edit');
    const verb = patch.isInService === false ? 'tripped' : 'updated';
    results.setStatus(`Branch #${branchIndex} ${verb} — solve to refresh results.`);
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
      if (bus) editor.showBus(bus as BusDto);
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

const sidebar = mountSidebar(sidebarEl, {
  onLoadCase: handleLoadCase,
  onUploadCase: handleUploadCase,
  onSolve: handleSolve,
  onContingency: handleContingency,
});

network.subscribe((net, kind) => {
  sidebar.setSolveEnabled(net !== null);
  sidebar.setBaseLoad(net ? totalLoadMw(net) : null);
  kpi.setNetwork(net);
  if (kind === 'load') {
    diagram.setNetwork(net);
    lastResult = null;
    editor.hide();
    sidebar.showResult(null);
    kpi.setResult(null);
  } else if (net) {
    diagram.applyEdit(net);
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
  sidebar.setSolveBusy(true);
  results.setStatus(scale === 1 ? 'Solving…' : `Solving at ${Math.round(scale * 100)}% load…`);
  try {
    const result =
      options.mode === 'AC'
        ? await runAcStream(net, options)
        : await solve({ network: net, options });
    lastResult = result;
    diagram.setSolveResult(result);
    results.showSolve(result);
    kpi.setResult(result);
    sidebar.showResult(result);
    results.setStatus(
      result.converged
        ? `Converged in ${result.iterations} iteration(s).`
        : `Did not converge after ${result.iterations} iteration(s).`,
      result.converged ? 'ok' : 'error',
    );
  } catch (err) {
    if (err instanceof ApiValidationError) {
      results.showValidation(err.result);
      results.setStatus(`Validation failed (${err.result.errors.length} error(s))`, 'error');
    } else {
      results.setStatus(`Solve failed: ${String(err)}`, 'error');
    }
  } finally {
    sidebar.setSolveBusy(false);
  }
}

async function handleContingency(options: SolveOptionsDto): Promise<void> {
  const base = network.get();
  if (!base) return;
  const scale = sidebar.getLoadScale();
  const net = scaleLoads(base, scale);
  sidebar.setContingencyBusy(true);
  const inSvc = net.branches.filter((b) => b.isInService).length;
  results.setStatus(`Running N-1 sweep over ${inSvc} branch(es)…`);
  try {
    const rs = await contingency({ network: net, options });
    results.showContingency(rs);
    const worst = rs[0];
    if (!worst) {
      results.setStatus('Sweep returned no contingencies.', 'muted');
    } else if (!worst.converged) {
      results.setStatus(
        `Sweep complete: ${rs.filter((r) => !r.converged).length} contingency(s) diverged — most severe at top.`,
        'error',
      );
    } else {
      results.setStatus(
        `Sweep complete: ${rs.length} contingencies, worst max loading ${worst.maxLoadingPct?.toFixed(1) ?? '—'}%.`,
        'ok',
      );
    }
  } catch (err) {
    if (err instanceof ApiValidationError) {
      results.showValidation(err.result);
      results.setStatus(`Validation failed (${err.result.errors.length} error(s))`, 'error');
    } else {
      results.setStatus(`Sweep failed: ${String(err)}`, 'error');
    }
  } finally {
    sidebar.setContingencyBusy(false);
  }
}

async function runAcStream(
  net: NetworkDto,
  options: SolveOptionsDto,
): Promise<SolveResultDto> {
  results.beginStream(options.tolerance);
  return new Promise<SolveResultDto>((resolve, reject) => {
    let settled = false;
    solveStream({ network: net, options }, (event) => {
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
    }).then(
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
