import type { CaseMetaDto, SolveResultDto, SolveOptionsDto } from '../types.js';

export interface SidebarCallbacks {
  onLoadCase: (id: string) => void | Promise<void>;
  onUploadCase: (filename: string, content: string) => void | Promise<void>;
  onSolve: (options: SolveOptionsDto) => void | Promise<void>;
  onCancelSolve: () => void;
  onContingency: (options: SolveOptionsDto) => void | Promise<void>;
  onCancelContingency: () => void;
  onAddBus: () => void;
  onAddBranch: (fromBusId: number, toBusId: number) => void;
  onAddGenerator: (busId: number) => void;
}

// One-line hints attached to each solver option as the native title=""
// tooltip. Brief, mechanism-oriented — the goal is to disambiguate the
// label, not to teach power flow.
const HINTS = {
  flatStart: 'Reset Vm=1, Va=0 before iterating instead of using stored estimates',
  enforceLimits: 'Enforce reactive limits via PV→PQ switching (outer loop)',
  distributedSlack: 'Spread slack mismatch across all generators by participation',
  warmStartFromDc: 'Initialise AC with a DC solve — usually faster for ill-conditioned cases',
  tolerance: 'Convergence threshold on the infinity-norm of the power mismatch (pu)',
  maxIterations: 'Newton-Raphson iterations before declaring divergence',
};

export function mountSidebar(container: HTMLElement, callbacks: SidebarCallbacks) {
  container.innerHTML = '';

  // ── Case section ────────────────────────────────────────────────────────
  const caseSection = document.createElement('section');
  caseSection.innerHTML = `
    <h2>Case</h2>
    <div class="case-cards" id="case-cards"></div>
    <label class="case-upload" id="case-upload">
      <input type="file" accept=".m" hidden id="case-file" />
      <span class="case-upload-icon">⤴</span>
      <span class="case-upload-text">Upload .m file</span>
    </label>
  `;
  const cards = caseSection.querySelector<HTMLDivElement>('#case-cards')!;
  const fileInput = caseSection.querySelector<HTMLInputElement>('#case-file')!;
  const uploadLabel = caseSection.querySelector<HTMLSpanElement>('.case-upload-text')!;
  fileInput.addEventListener('change', () => {
    const f = fileInput.files?.[0];
    if (!f) return;
    f.text().then((content) => {
      uploadLabel.textContent = f.name;
      void callbacks.onUploadCase(f.name, content);
    });
  });

  // ── Load scaling section ────────────────────────────────────────────────
  const stressSection = document.createElement('section');
  stressSection.innerHTML = `
    <h2>Load scaling</h2>
    <div class="field">
      <div class="slider-row">
        <input type="range" id="load-scale" min="50" max="200" step="5" value="100" disabled />
        <button id="load-reset" class="secondary mini" title="Reset to 100%" disabled>↺</button>
      </div>
      <div class="readout" id="load-readout">100% — load a case</div>
    </div>
  `;
  const scaleInput = stressSection.querySelector<HTMLInputElement>('#load-scale')!;
  const resetBtn = stressSection.querySelector<HTMLButtonElement>('#load-reset')!;
  const readout = stressSection.querySelector<HTMLDivElement>('#load-readout')!;
  let baseLoadMw: number | null = null;

  function refreshReadout(): void {
    const pct = scaleInput.valueAsNumber;
    if (baseLoadMw === null) {
      readout.textContent = `${pct}% — load a case`;
      return;
    }
    const scaled = (baseLoadMw * pct) / 100;
    readout.textContent =
      pct === 100
        ? `100% — ${baseLoadMw.toFixed(1)} MW`
        : `${pct}% — ${scaled.toFixed(1)} MW (base ${baseLoadMw.toFixed(1)} MW)`;
  }
  scaleInput.addEventListener('input', refreshReadout);
  resetBtn.addEventListener('click', () => {
    scaleInput.value = '100';
    refreshReadout();
  });

  // ── Solve options section ───────────────────────────────────────────────
  const solveSection = document.createElement('section');
  solveSection.innerHTML = `
    <h2>Solve</h2>
    <div class="field">
      <label>Mode</label>
      <div class="segmented" id="mode-seg">
        <button data-value="AC" class="active" type="button">AC</button>
        <button data-value="DC" type="button">DC</button>
      </div>
    </div>
    <div class="field-row" title="${HINTS.flatStart}">
      <input type="checkbox" id="opt-flat" checked />
      <label for="opt-flat">Flat start</label>
    </div>
    <div class="field-row" title="${HINTS.enforceLimits}">
      <input type="checkbox" id="opt-enforce" checked />
      <label for="opt-enforce">Enforce Q-limits</label>
    </div>
    <div class="field-row" title="${HINTS.distributedSlack}">
      <input type="checkbox" id="opt-slack" />
      <label for="opt-slack">Distributed slack</label>
    </div>
    <div class="field-row" title="${HINTS.warmStartFromDc}">
      <input type="checkbox" id="opt-warm" />
      <label for="opt-warm">Warm-start from DC</label>
    </div>
    <div class="field-pair">
      <div class="field" title="${HINTS.tolerance}">
        <label for="opt-tol">Tolerance</label>
        <input type="number" id="opt-tol" value="0.000001" step="0.0000001" min="0" />
      </div>
      <div class="field" title="${HINTS.maxIterations}">
        <label for="opt-iter">Max iter</label>
        <input type="number" id="opt-iter" value="50" step="1" min="1" />
      </div>
    </div>
    <button id="solve-btn" disabled>Solve</button>
  `;
  const modeSeg = solveSection.querySelector<HTMLDivElement>('#mode-seg')!;
  modeSeg.addEventListener('click', (ev) => {
    const btn = (ev.target as HTMLElement).closest<HTMLButtonElement>('button');
    if (!btn) return;
    for (const b of modeSeg.querySelectorAll<HTMLButtonElement>('button')) {
      b.classList.toggle('active', b === btn);
    }
  });
  const solveBtn = solveSection.querySelector<HTMLButtonElement>('#solve-btn')!;
  let solveBusy = false;
  solveBtn.addEventListener('click', () => {
    if (solveBusy) callbacks.onCancelSolve();
    else void callbacks.onSolve(readOptions());
  });

  // ── Result block (post-solve summary) ───────────────────────────────────
  const resultBlock = document.createElement('section');
  resultBlock.id = 'result-block';
  resultBlock.hidden = true;
  resultBlock.innerHTML = `
    <h2>Result</h2>
    <div class="result-card">
      <div class="result-badge" id="result-badge">—</div>
      <div class="result-iter"><span id="result-iter">0</span><small>iterations</small></div>
      <div class="result-mismatch" id="result-mismatch"></div>
    </div>
  `;
  const resultBadge = resultBlock.querySelector<HTMLDivElement>('#result-badge')!;
  const resultIter = resultBlock.querySelector<HTMLSpanElement>('#result-iter')!;
  const resultMismatch = resultBlock.querySelector<HTMLDivElement>('#result-mismatch')!;

  // ── Contingency section ─────────────────────────────────────────────────
  const contingencySection = document.createElement('section');
  contingencySection.innerHTML = `
    <h2>Contingency</h2>
    <button id="ctg-btn" class="secondary" disabled>Run N-1 sweep</button>
  `;
  const ctgBtn = contingencySection.querySelector<HTMLButtonElement>('#ctg-btn')!;
  let ctgBusy = false;
  ctgBtn.addEventListener('click', () => {
    if (ctgBusy) callbacks.onCancelContingency();
    else void callbacks.onContingency(readOptions());
  });

  // ── Build section ───────────────────────────────────────────────────────
  // Lightweight create-buttons for buses/branches/generators. Branch and
  // generator require a target so the form expands inline; bus creation is
  // one-click since the new bus defaults to PQ with zero load.
  const buildSection = document.createElement('section');
  buildSection.id = 'build-section';
  buildSection.hidden = true;
  buildSection.innerHTML = `
    <h2>Build</h2>
    <div class="build-row">
      <button class="secondary mini" id="add-bus-btn">+ Bus</button>
      <button class="secondary mini" id="add-branch-toggle">+ Branch</button>
      <button class="secondary mini" id="add-gen-toggle">+ Generator</button>
    </div>
    <div class="build-form" id="add-branch-form" hidden>
      <div class="field-pair">
        <div class="field">
          <label>From bus</label>
          <input type="number" id="brn-from" step="1" min="1" />
        </div>
        <div class="field">
          <label>To bus</label>
          <input type="number" id="brn-to" step="1" min="1" />
        </div>
      </div>
      <div class="editor-actions">
        <button id="add-branch-confirm">Create</button>
      </div>
    </div>
    <div class="build-form" id="add-gen-form" hidden>
      <div class="field">
        <label>Bus</label>
        <input type="number" id="gen-bus" step="1" min="1" />
      </div>
      <div class="editor-actions">
        <button id="add-gen-confirm">Create</button>
      </div>
    </div>
  `;
  const addBusBtn = buildSection.querySelector<HTMLButtonElement>('#add-bus-btn')!;
  const addBranchToggle = buildSection.querySelector<HTMLButtonElement>('#add-branch-toggle')!;
  const addBranchForm = buildSection.querySelector<HTMLDivElement>('#add-branch-form')!;
  const addBranchConfirm = buildSection.querySelector<HTMLButtonElement>('#add-branch-confirm')!;
  const brnFrom = buildSection.querySelector<HTMLInputElement>('#brn-from')!;
  const brnTo = buildSection.querySelector<HTMLInputElement>('#brn-to')!;
  const addGenToggle = buildSection.querySelector<HTMLButtonElement>('#add-gen-toggle')!;
  const addGenForm = buildSection.querySelector<HTMLDivElement>('#add-gen-form')!;
  const addGenConfirm = buildSection.querySelector<HTMLButtonElement>('#add-gen-confirm')!;
  const genBus = buildSection.querySelector<HTMLInputElement>('#gen-bus')!;

  addBusBtn.addEventListener('click', () => callbacks.onAddBus());
  addBranchToggle.addEventListener('click', () => {
    addBranchForm.hidden = !addBranchForm.hidden;
    addGenForm.hidden = true;
  });
  addGenToggle.addEventListener('click', () => {
    addGenForm.hidden = !addGenForm.hidden;
    addBranchForm.hidden = true;
  });
  addBranchConfirm.addEventListener('click', () => {
    const from = parseInt(brnFrom.value, 10);
    const to = parseInt(brnTo.value, 10);
    if (Number.isFinite(from) && Number.isFinite(to) && from !== to) {
      callbacks.onAddBranch(from, to);
      addBranchForm.hidden = true;
      brnFrom.value = '';
      brnTo.value = '';
    }
  });
  addGenConfirm.addEventListener('click', () => {
    const bus = parseInt(genBus.value, 10);
    if (Number.isFinite(bus)) {
      callbacks.onAddGenerator(bus);
      addGenForm.hidden = true;
      genBus.value = '';
    }
  });

  container.append(caseSection, stressSection, solveSection, resultBlock, contingencySection, buildSection);

  function readOptions(): SolveOptionsDto {
    const $ = <T extends Element>(s: string) => solveSection.querySelector<T>(s)!;
    const activeMode =
      modeSeg.querySelector<HTMLButtonElement>('button.active')?.dataset['value'] ?? 'AC';
    return {
      mode: activeMode as 'AC' | 'DC',
      flatStart: $<HTMLInputElement>('#opt-flat').checked,
      enforceLimits: $<HTMLInputElement>('#opt-enforce').checked,
      distributedSlack: $<HTMLInputElement>('#opt-slack').checked,
      warmStartFromDc: $<HTMLInputElement>('#opt-warm').checked,
      tolerance: parseFloat($<HTMLInputElement>('#opt-tol').value),
      maxIterations: parseInt($<HTMLInputElement>('#opt-iter').value, 10),
    };
  }

  return {
    setCases(cases: CaseMetaDto[]) {
      cards.innerHTML = '';
      for (const c of cases) {
        const btn = document.createElement('button');
        btn.className = 'case-card';
        btn.dataset['caseId'] = c.id;
        btn.innerHTML = `<strong>${c.label.replace('IEEE ', 'IEEE ')}</strong><span>${c.buses} bus</span>`;
        btn.addEventListener('click', () => {
          for (const sib of cards.querySelectorAll('.case-card'))
            sib.classList.remove('active');
          btn.classList.add('active');
          uploadLabel.textContent = 'Upload .m file';
          fileInput.value = '';
          void callbacks.onLoadCase(c.id);
        });
        cards.append(btn);
      }
    },
    setActiveCase(id: string | null) {
      for (const btn of cards.querySelectorAll<HTMLButtonElement>('.case-card')) {
        btn.classList.toggle('active', btn.dataset['caseId'] === id);
      }
      if (id === null) uploadLabel.textContent = 'Upload .m file';
    },
    setSolveEnabled(enabled: boolean) {
      solveBtn.disabled = !enabled;
      ctgBtn.disabled = !enabled;
      buildSection.hidden = !enabled;
    },
    setSolveBusy(busy: boolean) {
      solveBusy = busy;
      solveBtn.disabled = false;
      solveBtn.textContent = busy ? 'Cancel' : 'Solve';
      solveBtn.classList.toggle('cancel', busy);
    },
    setContingencyBusy(busy: boolean) {
      ctgBusy = busy;
      ctgBtn.disabled = false;
      ctgBtn.textContent = busy ? 'Cancel' : 'Run N-1 sweep';
      ctgBtn.classList.toggle('cancel', busy);
    },
    setBaseLoad(totalMw: number | null) {
      baseLoadMw = totalMw;
      const enabled = totalMw !== null;
      scaleInput.disabled = !enabled;
      resetBtn.disabled = !enabled;
      if (!enabled) scaleInput.value = '100';
      refreshReadout();
    },
    getLoadScale(): number {
      return scaleInput.valueAsNumber / 100;
    },
    showResult(r: SolveResultDto | null) {
      if (!r) {
        resultBlock.hidden = true;
        return;
      }
      resultBlock.hidden = false;
      resultBlock.classList.remove('stale');
      resultBadge.textContent = r.converged ? 'Converged' : 'Diverged';
      resultBadge.className = `result-badge ${r.converged ? 'ok' : 'error'}`;
      resultIter.textContent = String(r.iterations);
      resultMismatch.textContent = `max |F| ${r.maxMismatch.toExponential(2)} pu`;
    },
    setStale(stale: boolean) {
      resultBlock.classList.toggle('stale', stale && !resultBlock.hidden);
      if (stale && !resultBlock.hidden) {
        resultBadge.textContent = 'Stale';
        resultBadge.className = 'result-badge warn';
      }
    },
  };
}
