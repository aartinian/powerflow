import type {
  BusTypeChangeDto,
  ContingencyResultDto,
  SolvedBranchDto,
  SolvedBusDto,
  SolvedGeneratorDto,
  SolveResultDto,
  ValidationResultDto,
  VoltageViolationDto,
} from '../types.js';
import { mountConvergence, type ConvergenceHandle } from './convergence.js';

export type TabId =
  | 'summary'
  | 'convergence'
  | 'buses'
  | 'branches'
  | 'generators'
  | 'violations'
  | 'contingency';

export interface ResultsCallbacks {
  onContingencyRowClick: (branchIndex: number) => void;
}

export interface ResultsHandle {
  setStatus(message: string, kind?: 'muted' | 'ok' | 'error'): void;
  showSolve(result: SolveResultDto): void;
  showValidation(result: ValidationResultDto): void;
  clear(): void;
  selectBus(busId: number): void;
  selectBranch(branchIndex: number): void;
  clearSelection(): void;
  // Streaming hooks for /api/solve/stream
  beginStream(tolerance: number): void;
  pushIteration(iter: number, mismatch: number, changes: BusTypeChangeDto[]): void;
  // N-1 sweep results
  showContingency(results: ContingencyResultDto[]): void;
}

// Results panel: status line + tab strip + content area. The convergence
// chart lives in its own permanently-mounted view so iter events keep
// landing on the canvas even when the user has switched to another tab.
// The other tabs render lazily from the last SolveResultDto on tab activation.
export function mountResults(container: HTMLElement, callbacks: ResultsCallbacks): ResultsHandle {
  container.innerHTML = `
    <div class="status muted" id="status">No network loaded.</div>
    <ul class="errors" id="errors" hidden></ul>
    <div class="tabs" id="tabs" hidden>
      <button class="tab active" data-tab="summary">Summary</button>
      <button class="tab" data-tab="convergence" hidden>Convergence</button>
      <button class="tab" data-tab="buses">Buses</button>
      <button class="tab" data-tab="branches">Branches</button>
      <button class="tab" data-tab="generators">Generators</button>
      <button class="tab" data-tab="violations" hidden>Violations</button>
      <button class="tab" data-tab="contingency" hidden>Contingency</button>
    </div>
    <div class="tab-panel" id="panel" hidden>
      <div class="conv-host" id="conv-host" hidden></div>
      <div class="dyn-view" id="dyn-view"></div>
    </div>
  `;
  const status = container.querySelector<HTMLDivElement>('#status')!;
  const errors = container.querySelector<HTMLUListElement>('#errors')!;
  const tabs = container.querySelector<HTMLDivElement>('#tabs')!;
  const convTab = tabs.querySelector<HTMLButtonElement>('[data-tab="convergence"]')!;
  const violationsTab = tabs.querySelector<HTMLButtonElement>('[data-tab="violations"]')!;
  const ctgTab = tabs.querySelector<HTMLButtonElement>('[data-tab="contingency"]')!;
  const panel = container.querySelector<HTMLDivElement>('#panel')!;
  const convHost = container.querySelector<HTMLDivElement>('#conv-host')!;
  const dynView = container.querySelector<HTMLDivElement>('#dyn-view')!;

  const convergence: ConvergenceHandle = mountConvergence(convHost);
  let lastResult: SolveResultDto | null = null;
  let lastContingency: ContingencyResultDto[] | null = null;
  let activeTab: TabId = 'summary';

  tabs.addEventListener('click', (ev) => {
    const btn = (ev.target as HTMLElement).closest<HTMLButtonElement>('.tab');
    if (!btn || btn.hidden) return;
    showTab(btn.dataset['tab'] as TabId);
  });

  function showTab(tab: TabId): void {
    activeTab = tab;
    for (const t of tabs.querySelectorAll<HTMLButtonElement>('.tab')) {
      t.classList.toggle('active', t.dataset['tab'] === tab);
    }
    if (tab === 'convergence') {
      convHost.hidden = false;
      dynView.hidden = true;
      // Container is freshly visible — force a redraw so the canvas
      // picks up its real pixel size.
      convergence.resize();
      return;
    }
    convHost.hidden = true;
    dynView.hidden = false;
    if (tab === 'contingency') {
      if (lastContingency) {
        dynView.replaceChildren(renderContingency(lastContingency, callbacks.onContingencyRowClick));
      } else {
        dynView.replaceChildren();
      }
      return;
    }
    if (!lastResult) {
      dynView.replaceChildren();
      return;
    }
    dynView.replaceChildren(renderPanel(tab, lastResult));
  }

  function renderPanel(
    tab: Exclude<TabId, 'convergence' | 'contingency'>,
    result: SolveResultDto,
  ): Node {
    switch (tab) {
      case 'summary':
        return renderSummary(result);
      case 'buses':
        return renderBuses(result.buses);
      case 'branches':
        return renderBranches(result.branches);
      case 'generators':
        return renderGenerators(result.generators);
      case 'violations':
        return renderViolations(result.violations);
    }
  }

  function clearErrors(): void {
    errors.hidden = true;
    errors.innerHTML = '';
  }

  function focusRow(rowId: string): void {
    const row = dynView.querySelector<HTMLTableRowElement>(`#${rowId}`);
    if (!row) return;
    for (const tr of dynView.querySelectorAll('tr.selected')) tr.classList.remove('selected');
    row.classList.add('selected');
    row.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  return {
    setStatus(message, kind = 'muted') {
      status.className = `status ${kind}`;
      status.textContent = message;
    },
    showSolve(result) {
      clearErrors();
      lastResult = result;
      tabs.hidden = false;
      panel.hidden = false;
      // Convergence tab is only meaningful for AC — DC is a single LU step.
      convTab.hidden = result.mode !== 'AC';
      violationsTab.hidden = result.violations.length === 0;
      violationsTab.textContent =
        result.violations.length > 0 ? `Violations (${result.violations.length})` : 'Violations';
      // Snap back to Summary on every fresh solve.
      showTab('summary');
    },
    showValidation(result) {
      lastResult = null;
      tabs.hidden = true;
      panel.hidden = true;
      dynView.replaceChildren();
      errors.hidden = false;
      errors.innerHTML = '';
      for (const err of result.errors) {
        const li = document.createElement('li');
        if (err.severity === 'Warning') li.classList.add('warn');
        li.textContent = `[${err.code}] ${err.message}`;
        errors.append(li);
      }
    },
    clear() {
      lastResult = null;
      lastContingency = null;
      ctgTab.hidden = true;
      clearErrors();
      tabs.hidden = true;
      panel.hidden = true;
      dynView.replaceChildren();
    },
    selectBus(busId) {
      if (!lastResult) return;
      if (activeTab !== 'buses') showTab('buses');
      focusRow(`bus-${busId}`);
    },
    selectBranch(branchIndex) {
      if (!lastResult) return;
      if (activeTab !== 'branches') showTab('branches');
      focusRow(`brn-${branchIndex}`);
    },
    clearSelection() {
      for (const tr of dynView.querySelectorAll('tr.selected')) tr.classList.remove('selected');
    },
    beginStream(tolerance) {
      // AC stream starting — clear chart, expose tab, and switch to it so
      // the user watches iterations live instead of staring at empty space.
      convergence.reset(tolerance);
      tabs.hidden = false;
      panel.hidden = false;
      convTab.hidden = false;
      lastResult = null;
      showTab('convergence');
    },
    pushIteration(iter, mismatch, changes) {
      convergence.addPoint(iter, mismatch, changes);
    },
    showContingency(rs) {
      lastContingency = rs;
      tabs.hidden = false;
      panel.hidden = false;
      ctgTab.hidden = false;
      ctgTab.textContent = rs.length > 0 ? `Contingency (${rs.length})` : 'Contingency';
      showTab('contingency');
    },
  };
}

// ── Renderers ───────────────────────────────────────────────────────────────

function renderSummary(r: SolveResultDto): Node {
  const lines: string[] = [];
  lines.push(`mode             ${r.mode}`);
  lines.push(`converged        ${r.converged}`);
  lines.push(
    `iterations       ${r.iterations}` +
      (r.outerIterations > 0 ? ` (${r.outerIterations} outer)` : ''),
  );
  lines.push(`max mismatch     ${r.maxMismatch.toExponential(3)} pu`);
  if (r.lambda !== null) lines.push(`lambda           ${r.lambda.toFixed(4)} pu`);

  if (r.balance) {
    const b = r.balance;
    lines.push('');
    lines.push(`gen total        ${fmt(b.totalGenerationMw)} MW   ${fmt(b.totalGenerationMvar)} MVAr`);
    lines.push(`load total       ${fmt(b.totalLoadMw)} MW   ${fmt(b.totalLoadMvar)} MVAr`);
    lines.push(
      `losses           ${fmt(b.totalLossesMw)} MW   ${fmt(b.totalLossesMvar)} MVAr  (${b.lossPct.toFixed(2)}%)`,
    );
    if (b.totalShuntMvar !== 0) lines.push(`shunt            ${fmt(b.totalShuntMvar)} MVAr`);
  }

  const pre = document.createElement('pre');
  pre.className = 'summary';
  pre.textContent = lines.join('\n');
  return pre;
}

function renderBuses(buses: SolvedBusDto[]): Node {
  return buildTable(
    ['Bus', 'Vm (pu)', 'Va (°)', 'Pg (MW)', 'Qg (MVAr)', 'Pd (MW)', 'Qd (MVAr)'],
    buses.map((b) => ({
      id: `bus-${b.busId}`,
      cells: [
        String(b.busId),
        fmtOpt(b.vm, 4),
        b.va.toFixed(2),
        fmt(b.pg),
        fmtOpt(b.qg),
        fmt(b.pd),
        fmtOpt(b.qd),
      ],
    })),
  );
}

function renderBranches(branches: SolvedBranchDto[]): Node {
  return buildTable(
    ['#', 'From → To', 'Pij (MW)', 'Qij (MVAr)', 'Loss (MW)', 'Loading %'],
    branches.map((br) => ({
      id: `brn-${br.branchIndex}`,
      cells: [
        String(br.branchIndex),
        `${br.fromBusId} → ${br.toBusId}`,
        fmt(br.pij),
        fmtOpt(br.qij),
        fmtOpt(br.lossMw, 3),
        loadingCell(br.loadingPct),
      ],
    })),
  );
}

function renderGenerators(gens: SolvedGeneratorDto[]): Node {
  return buildTable(
    ['#', 'Bus', 'Pg (MW)', 'Qg (MVAr)', 'Limit'],
    gens.map((g) => ({
      id: `gen-${g.index}`,
      cells: [
        String(g.index),
        String(g.busId),
        fmt(g.pg),
        fmtOpt(g.qg),
        g.isAtQmax ? 'Qmax' : g.isAtQmin ? 'Qmin' : '',
      ],
    })),
  );
}

function renderContingency(
  results: ContingencyResultDto[],
  onRowClick: (branchIndex: number) => void,
): Node {
  const table = buildTable(
    ['#', 'From → To', 'Converged', 'Max Loading %', 'Overloads', 'V-Viol'],
    results.map((c) => ({
      id: `ctg-${c.branchIndex}`,
      cells: [
        String(c.branchIndex),
        `${c.fromBusId} → ${c.toBusId}`,
        c.converged ? 'yes' : { text: 'no', cls: 'cell-red' },
        contingencyLoadingCell(c.maxLoadingPct),
        c.branchOverloadCount > 0
          ? { text: String(c.branchOverloadCount), cls: 'cell-red' }
          : '0',
        c.voltageViolationCount > 0
          ? { text: String(c.voltageViolationCount), cls: 'cell-amber' }
          : '0',
      ],
    })),
  );
  table.classList.add('clickable');
  table.addEventListener('click', (ev) => {
    const row = (ev.target as HTMLElement).closest<HTMLTableRowElement>('tr');
    if (!row || !row.id.startsWith('ctg-')) return;
    onRowClick(parseInt(row.id.slice(4), 10));
  });
  return table;
}

function contingencyLoadingCell(
  pct: number | null,
): string | { text: string; cls: string } {
  if (pct === null) return '—';
  const cls = pct >= 100 ? 'cell-red' : pct >= 90 ? 'cell-amber' : 'cell-green';
  return { text: pct.toFixed(1), cls };
}

function renderViolations(violations: VoltageViolationDto[]): Node {
  return buildTable(
    ['Bus', 'Vm (pu)', 'Vm (kV)', 'Type', 'Bound (pu)'],
    violations.map((v) => ({
      id: `vio-${v.busId}`,
      cells: [
        String(v.busId),
        v.vm.toFixed(4),
        v.vmKv.toFixed(2),
        v.isOverVoltage ? 'over' : 'under',
        (v.isOverVoltage ? v.vmax : v.vmin).toFixed(4),
      ],
    })),
  );
}

// ── Table primitive ─────────────────────────────────────────────────────────

interface TableRow {
  id: string;
  cells: (string | { text: string; cls?: string })[];
}

function buildTable(headers: string[], rows: TableRow[]): HTMLElement {
  const table = document.createElement('table');
  table.className = 'datatable';
  const thead = table.createTHead();
  const tr = thead.insertRow();
  for (const h of headers) {
    const th = document.createElement('th');
    th.textContent = h;
    tr.append(th);
  }
  const tbody = table.createTBody();
  for (const row of rows) {
    const r = tbody.insertRow();
    r.id = row.id;
    for (const cell of row.cells) {
      const td = r.insertCell();
      if (typeof cell === 'string') {
        td.textContent = cell;
      } else {
        td.textContent = cell.text;
        if (cell.cls) td.className = cell.cls;
      }
    }
  }
  return table;
}

// ── Cell formatters ─────────────────────────────────────────────────────────

function fmt(n: number, decimals = 2): string {
  return n.toFixed(decimals);
}

function fmtOpt(n: number | null | undefined, decimals = 2): string {
  return n === null || n === undefined ? '—' : n.toFixed(decimals);
}

function loadingCell(pct: number | null): string | { text: string; cls: string } {
  if (pct === null) return '—';
  const cls = pct >= 90 ? 'cell-red' : pct >= 70 ? 'cell-amber' : 'cell-green';
  return { text: pct.toFixed(1), cls };
}
