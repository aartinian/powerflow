import './style.css';
import { ApiValidationError, listCases, loadCase, solve, validate } from './api.js';
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
const diagram = mountDiagram(diagramEl, () => {
  // Selection wired to the results panel in a later commit; ignore for now.
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
    const result = await solve({ network: net, options });
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

// Suppress unused warning until selection wiring lands in commit 12.
void lastResult;
