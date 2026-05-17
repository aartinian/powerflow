import './style.css';
import {
  ApiValidationError,
  contingency,
  listCases,
  loadCase,
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
import { mountResults } from './ui/results.js';
import { mountSidebar } from './ui/sidebar.js';

const app = document.getElementById('app');
if (!app) throw new Error('#app not found');

const sidebarEl = document.createElement('aside');
sidebarEl.id = 'sidebar';
const mainEl = document.createElement('main');
mainEl.id = 'main';
const diagramEl = document.createElement('div');
diagramEl.id = 'diagram';
diagramEl.innerHTML = `<div class="placeholder">Load a case to render the network.</div>`;
const editorEl = document.createElement('div');
editorEl.id = 'editor';
const resultsEl = document.createElement('div');
resultsEl.id = 'results-panel';
mainEl.append(diagramEl, editorEl, resultsEl);
app.append(sidebarEl, mainEl);

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
    /* nothing extra — diagram selection stays as is */
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
let lastResult: SolveResultDto | null = null;

const sidebar = mountSidebar(sidebarEl, {
  onLoadCase: handleLoadCase,
  onSolve: handleSolve,
  onContingency: handleContingency,
});

network.subscribe((net, kind) => {
  sidebar.setSolveEnabled(net !== null);
  sidebar.setBaseLoad(net ? totalLoadMw(net) : null);
  if (kind === 'load') {
    diagram.setNetwork(net);
    lastResult = null;
    editor.hide();
  } else if (net) {
    diagram.applyEdit(net);
  }
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
  sidebar.setLoadBusy(true);
  results.setStatus(`Loading ${id}…`);
  try {
    const net = await loadCase(id);
    network.set(net);
    const validation = await validate(net);
    if (!validation.isValid) {
      results.setStatus(`${id} loaded with ${validation.errors.length} error(s)`, 'error');
      results.showValidation(validation);
    } else {
      results.setStatus(
        `${net.name ?? id} — ${net.buses.length} buses, ${net.branches.length} branches, ${net.generators.length} generators`,
        'ok',
      );
    }
  } catch (err) {
    results.setStatus(`Load failed: ${String(err)}`, 'error');
  } finally {
    sidebar.setLoadBusy(false);
  }
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

// Streams AC iterations into the convergence chart and returns the final
// SolveResultDto. A server-emitted `error` event is rethrown so the catch
// block in handleSolve renders it like any other failure.
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
