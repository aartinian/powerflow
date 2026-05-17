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
import { downloadCsv } from './csv.js';

export type TabId =
  | 'summary'
  | 'convergence'
  | 'buses'
  | 'branches'
  | 'generators'
  | 'violations'
  | 'contingency'
  | 'log';

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
  beginStream(tolerance: number): void;
  pushIteration(iter: number, mismatch: number, changes: BusTypeChangeDto[]): void;
  showContingency(results: ContingencyResultDto[]): void;
}

export function mountResults(container: HTMLElement, callbacks: ResultsCallbacks): ResultsHandle {
  container.innerHTML = `
    <div class="status muted" id="status">No network loaded.</div>
    <ul class="errors" id="errors" hidden></ul>
    <div class="tab-bar" id="tab-bar" hidden>
      <div class="tabs" id="tabs">
        <button class="tab active" data-tab="summary">Summary</button>
        <button class="tab" data-tab="convergence" hidden>Convergence</button>
        <button class="tab" data-tab="buses">Buses</button>
        <button class="tab" data-tab="branches">Branches</button>
        <button class="tab" data-tab="generators">Generators</button>
        <button class="tab" data-tab="violations" hidden>Violations</button>
        <button class="tab" data-tab="log" hidden>Log</button>
        <button class="tab" data-tab="contingency" hidden>Contingency</button>
      </div>
      <button class="csv-btn" id="csv-btn" hidden title="Download active tab as CSV">↓ CSV</button>
    </div>
    <div class="tab-panel" id="panel" hidden>
      <div class="conv-host" id="conv-host" hidden></div>
      <div class="dyn-view" id="dyn-view"></div>
    </div>
  `;
  const status = container.querySelector<HTMLDivElement>('#status')!;
  const errors = container.querySelector<HTMLUListElement>('#errors')!;
  const tabBar = container.querySelector<HTMLDivElement>('#tab-bar')!;
  const tabs = container.querySelector<HTMLDivElement>('#tabs')!;
  const convTab = tabs.querySelector<HTMLButtonElement>('[data-tab="convergence"]')!;
  const busesTab = tabs.querySelector<HTMLButtonElement>('[data-tab="buses"]')!;
  const branchesTab = tabs.querySelector<HTMLButtonElement>('[data-tab="branches"]')!;
  const generatorsTab = tabs.querySelector<HTMLButtonElement>('[data-tab="generators"]')!;
  const violationsTab = tabs.querySelector<HTMLButtonElement>('[data-tab="violations"]')!;
  const logTab = tabs.querySelector<HTMLButtonElement>('[data-tab="log"]')!;
  const ctgTab = tabs.querySelector<HTMLButtonElement>('[data-tab="contingency"]')!;
  const csvBtn = container.querySelector<HTMLButtonElement>('#csv-btn')!;
  const panel = container.querySelector<HTMLDivElement>('#panel')!;
  const convHost = container.querySelector<HTMLDivElement>('#conv-host')!;
  const dynView = container.querySelector<HTMLDivElement>('#dyn-view')!;

  const convergence: ConvergenceHandle = mountConvergence(convHost);
  let lastResult: SolveResultDto | null = null;
  let lastContingency: ContingencyResultDto[] | null = null;
  let activeTab: TabId = 'summary';
  let logLines: string[] = [];

  // CSV is meaningful for tabular tabs only.
  const csvCapable: TabId[] = ['buses', 'branches', 'generators', 'violations', 'contingency', 'log'];

  tabs.addEventListener('click', (ev) => {
    const btn = (ev.target as HTMLElement).closest<HTMLButtonElement>('.tab');
    if (!btn || btn.hidden) return;
    showTab(btn.dataset['tab'] as TabId);
  });

  csvBtn.addEventListener('click', () => exportActiveTab());

  function setTabBadge(btn: HTMLButtonElement, base: string, count: number | null): void {
    btn.textContent = count !== null ? `${base} ${count}` : base;
  }

  function showTab(tab: TabId): void {
    activeTab = tab;
    for (const t of tabs.querySelectorAll<HTMLButtonElement>('.tab')) {
      t.classList.toggle('active', t.dataset['tab'] === tab);
    }
    csvBtn.hidden = !csvCapable.includes(tab);
    if (tab === 'convergence') {
      convHost.hidden = false;
      dynView.hidden = true;
      convergence.resize();
      return;
    }
    convHost.hidden = true;
    dynView.hidden = false;
    if (tab === 'contingency') {
      dynView.replaceChildren(
        lastContingency
          ? renderContingency(lastContingency, callbacks.onContingencyRowClick)
          : document.createElement('div'),
      );
      return;
    }
    if (tab === 'log') {
      dynView.replaceChildren(renderLog(logLines));
      return;
    }
    if (!lastResult) {
      dynView.replaceChildren();
      return;
    }
    dynView.replaceChildren(renderPanel(tab, lastResult));
  }

  function renderPanel(
    tab: Exclude<TabId, 'convergence' | 'contingency' | 'log'>,
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

  function exportActiveTab(): void {
    if (activeTab === 'log') {
      const blob = new Blob([logLines.join('\n')], { type: 'text/plain;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'powerflow-log.txt';
      a.click();
      URL.revokeObjectURL(url);
      return;
    }
    if (activeTab === 'contingency' && lastContingency) {
      downloadCsv(
        'powerflow-contingency.csv',
        ['BranchIndex', 'FromBusId', 'ToBusId', 'Converged', 'MaxLoadingPct', 'BranchOverloadCount', 'VoltageViolationCount'],
        lastContingency.map((c) => [
          c.branchIndex,
          c.fromBusId,
          c.toBusId,
          c.converged,
          c.maxLoadingPct,
          c.branchOverloadCount,
          c.voltageViolationCount,
        ]),
      );
      return;
    }
    if (!lastResult) return;
    if (activeTab === 'buses') {
      downloadCsv(
        'powerflow-buses.csv',
        ['BusId', 'Vm', 'Va', 'Pg', 'Qg', 'Pd', 'Qd'],
        lastResult.buses.map((b) => [b.busId, b.vm, b.va, b.pg, b.qg, b.pd, b.qd]),
      );
    } else if (activeTab === 'branches') {
      downloadCsv(
        'powerflow-branches.csv',
        ['BranchIndex', 'FromBusId', 'ToBusId', 'Pij', 'Qij', 'Pji', 'Qji', 'LossMw', 'LoadingPct'],
        lastResult.branches.map((b) => [
          b.branchIndex,
          b.fromBusId,
          b.toBusId,
          b.pij,
          b.qij,
          b.pji,
          b.qji,
          b.lossMw,
          b.loadingPct,
        ]),
      );
    } else if (activeTab === 'generators') {
      downloadCsv(
        'powerflow-generators.csv',
        ['Index', 'BusId', 'Pg', 'Qg', 'IsAtQmax', 'IsAtQmin'],
        lastResult.generators.map((g) => [g.index, g.busId, g.pg, g.qg, g.isAtQmax, g.isAtQmin]),
      );
    } else if (activeTab === 'violations') {
      downloadCsv(
        'powerflow-violations.csv',
        ['BusId', 'Vm', 'VmKv', 'IsOverVoltage', 'Vmin', 'Vmax', 'BaseKv'],
        lastResult.violations.map((v) => [
          v.busId,
          v.vm,
          v.vmKv,
          v.isOverVoltage,
          v.vmin,
          v.vmax,
          v.baseKv,
        ]),
      );
    }
  }

  function appendLog(line: string): void {
    logLines.push(line);
    if (activeTab === 'log') {
      const pre = dynView.querySelector<HTMLPreElement>('pre.log');
      if (pre) {
        pre.textContent = logLines.join('\n');
        pre.scrollTop = pre.scrollHeight;
      }
    }
    setTabBadge(logTab, 'Log', logLines.length);
  }

  return {
    setStatus(message, kind = 'muted') {
      status.className = `status ${kind}`;
      status.textContent = message;
    },
    showSolve(result) {
      clearErrors();
      lastResult = result;
      tabBar.hidden = false;
      panel.hidden = false;
      convTab.hidden = result.mode !== 'AC';
      setTabBadge(busesTab, 'Buses', result.buses.length);
      setTabBadge(branchesTab, 'Branches', result.branches.length);
      setTabBadge(generatorsTab, 'Generators', result.generators.length);
      violationsTab.hidden = result.violations.length === 0;
      setTabBadge(violationsTab, '⚠ Violations', result.violations.length);
      appendLog(
        result.converged
          ? `converged in ${result.iterations} iter, max mismatch ${result.maxMismatch.toExponential(3)} pu`
          : `did NOT converge after ${result.iterations} iter, max mismatch ${result.maxMismatch.toExponential(3)} pu`,
      );
      showTab('summary');
    },
    showValidation(result) {
      lastResult = null;
      tabBar.hidden = true;
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
      logLines = [];
      ctgTab.hidden = true;
      logTab.hidden = true;
      clearErrors();
      tabBar.hidden = true;
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
      convergence.reset(tolerance);
      logLines = [];
      logTab.hidden = false;
      setTabBadge(logTab, 'Log', 0);
      appendLog(`Starting AC solve, tol=${tolerance.toExponential(0)}`);
      tabBar.hidden = false;
      panel.hidden = false;
      convTab.hidden = false;
      lastResult = null;
      showTab('convergence');
    },
    pushIteration(iter, mismatch, changes) {
      convergence.addPoint(iter, mismatch, changes);
      for (const c of changes) {
        appendLog(`Q-limit: bus ${c.busId} ${c.from}→${c.to} (${c.reason})`);
      }
      appendLog(`iter ${String(iter).padStart(3)}  mismatch ${mismatch.toExponential(3)} pu`);
    },
    showContingency(rs) {
      lastContingency = rs;
      tabBar.hidden = false;
      panel.hidden = false;
      ctgTab.hidden = false;
      setTabBadge(ctgTab, 'Contingency', rs.length);
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

function renderLog(lines: string[]): Node {
  const pre = document.createElement('pre');
  pre.className = 'log';
  pre.textContent = lines.join('\n');
  // Scroll to bottom on render so new entries are visible.
  queueMicrotask(() => {
    pre.scrollTop = pre.scrollHeight;
  });
  return pre;
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

function contingencyLoadingCell(
  pct: number | null,
): string | { text: string; cls: string } {
  if (pct === null) return '—';
  const cls = pct >= 100 ? 'cell-red' : pct >= 90 ? 'cell-amber' : 'cell-green';
  return { text: pct.toFixed(1), cls };
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
