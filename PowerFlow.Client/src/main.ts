import './style.css';
import { ApiValidationError, listCases, loadCase, solve, solveStream, validate } from './api.js';
import { network } from './network.js';
import type { SolveOptionsDto, SolveResultDto } from './types.js';
import { mountDiagram } from './ui/diagram.js';
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
const resultsEl = document.createElement('div');
resultsEl.id = 'results-panel';
mainEl.append(diagramEl, resultsEl);
app.append(sidebarEl, mainEl);

const results = mountResults(resultsEl);
const diagram = mountDiagram(diagramEl, (selection) => {
  switch (selection.kind) {
    case 'bus':
      results.selectBus(selection.busId);
      break;
    case 'branch':
      results.selectBranch(selection.branchIndex);
      break;
    case 'none':
      results.clearSelection();
      break;
  }
});
let lastResult: SolveResultDto | null = null;

const sidebar = mountSidebar(sidebarEl, {
  onLoadCase: handleLoadCase,
  onSolve: handleSolve,
});

network.subscribe((net) => {
  sidebar.setSolveEnabled(net !== null);
  diagram.setNetwork(net);
  lastResult = null;
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
  const net = network.get();
  if (!net) return;
  sidebar.setSolveBusy(true);
  results.setStatus('Solving…');
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

// Streams AC iterations into the convergence chart and returns the final
// SolveResultDto. A server-emitted `error` event is rethrown so the catch
// block in handleSolve renders it like any other failure.
async function runAcStream(
  net: ReturnType<typeof network.get> & object,
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

// Suppress unused warning until lastResult feeds the editing/contingency commits.
void lastResult;
