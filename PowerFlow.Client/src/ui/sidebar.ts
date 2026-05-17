import type { CaseMetaDto, SolveOptionsDto } from '../types.js';

export interface SidebarCallbacks {
  onLoadCase: (id: string) => void | Promise<void>;
  onSolve: (options: SolveOptionsDto) => void | Promise<void>;
  onContingency: (options: SolveOptionsDto) => void | Promise<void>;
}

// Builds the sidebar DOM into `container`. Returns a small control object so
// the orchestrator (main.ts) can swap the case list once it has loaded from
// the API and toggle the solve button while a solve is in flight.
export function mountSidebar(container: HTMLElement, callbacks: SidebarCallbacks) {
  container.innerHTML = '';

  // ── Case section ────────────────────────────────────────────────────────
  const caseSection = document.createElement('section');
  caseSection.innerHTML = `
    <h2>Case</h2>
    <div class="field">
      <label for="case-select">Bundled case</label>
      <select id="case-select" disabled>
        <option>Loading…</option>
      </select>
    </div>
    <button id="load-btn" disabled>Load</button>
  `;
  const caseSelect = caseSection.querySelector<HTMLSelectElement>('#case-select')!;
  const loadBtn = caseSection.querySelector<HTMLButtonElement>('#load-btn')!;
  loadBtn.addEventListener('click', () => {
    if (caseSelect.value) void callbacks.onLoadCase(caseSelect.value);
  });

  // ── Load scaling section ────────────────────────────────────────────────
  // Multiplies every bus's Pd / Qd by the slider value at solve time. The
  // canonical network in the store stays at the originally-loaded values so
  // dragging from 120% back to 90% scales the *base*, not the previous scale.
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

  function refreshReadout() {
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
      <label for="mode-select">Mode</label>
      <select id="mode-select">
        <option value="AC" selected>AC (Newton-Raphson)</option>
        <option value="DC">DC (linear)</option>
      </select>
    </div>
    <div class="field-row">
      <input type="checkbox" id="opt-flat" checked />
      <label for="opt-flat">Flat start</label>
    </div>
    <div class="field-row">
      <input type="checkbox" id="opt-enforce" checked />
      <label for="opt-enforce">Enforce Q-limits</label>
    </div>
    <div class="field-row">
      <input type="checkbox" id="opt-slack" />
      <label for="opt-slack">Distributed slack</label>
    </div>
    <div class="field-row">
      <input type="checkbox" id="opt-warm" />
      <label for="opt-warm">Warm-start from DC</label>
    </div>
    <div class="field">
      <label for="opt-tol">Tolerance (pu)</label>
      <input type="number" id="opt-tol" value="0.000001" step="0.0000001" min="0" />
    </div>
    <div class="field">
      <label for="opt-iter">Max iterations</label>
      <input type="number" id="opt-iter" value="50" step="1" min="1" />
    </div>
    <button id="solve-btn" disabled>Solve</button>
  `;
  const solveBtn = solveSection.querySelector<HTMLButtonElement>('#solve-btn')!;
  solveBtn.addEventListener('click', () => {
    void callbacks.onSolve(readOptions(solveSection));
  });

  // ── Contingency section ─────────────────────────────────────────────────
  // Re-uses the Solve form's options — same network / mode / tolerance the
  // user just spent time configuring. The sweep solves every in-service
  // branch trip in parallel on the server and returns severity-ranked rows.
  const contingencySection = document.createElement('section');
  contingencySection.innerHTML = `
    <h2>Contingency</h2>
    <button id="ctg-btn" class="secondary" disabled>Run N-1 sweep</button>
  `;
  const ctgBtn = contingencySection.querySelector<HTMLButtonElement>('#ctg-btn')!;
  ctgBtn.addEventListener('click', () => {
    void callbacks.onContingency(readOptions(solveSection));
  });

  container.append(caseSection, stressSection, solveSection, contingencySection);

  return {
    setCases(cases: CaseMetaDto[]) {
      caseSelect.innerHTML = '';
      for (const c of cases) {
        const opt = document.createElement('option');
        opt.value = c.id;
        opt.textContent = `${c.label} (${c.buses} bus, ${c.branches} brn)`;
        caseSelect.append(opt);
      }
      caseSelect.disabled = cases.length === 0;
      loadBtn.disabled = cases.length === 0;
    },
    setSolveEnabled(enabled: boolean) {
      solveBtn.disabled = !enabled;
      ctgBtn.disabled = !enabled;
    },
    setLoadBusy(busy: boolean) {
      loadBtn.disabled = busy;
      loadBtn.textContent = busy ? 'Loading…' : 'Load';
    },
    setSolveBusy(busy: boolean) {
      solveBtn.disabled = busy;
      solveBtn.textContent = busy ? 'Solving…' : 'Solve';
    },
    setContingencyBusy(busy: boolean) {
      ctgBtn.disabled = busy;
      ctgBtn.textContent = busy ? 'Running…' : 'Run N-1 sweep';
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
  };
}

function readOptions(section: HTMLElement): SolveOptionsDto {
  const $ = <T extends Element>(s: string) => section.querySelector<T>(s)!;
  return {
    mode: $<HTMLSelectElement>('#mode-select').value as 'AC' | 'DC',
    flatStart: $<HTMLInputElement>('#opt-flat').checked,
    enforceLimits: $<HTMLInputElement>('#opt-enforce').checked,
    distributedSlack: $<HTMLInputElement>('#opt-slack').checked,
    warmStartFromDc: $<HTMLInputElement>('#opt-warm').checked,
    tolerance: parseFloat($<HTMLInputElement>('#opt-tol').value),
    maxIterations: parseInt($<HTMLInputElement>('#opt-iter').value, 10),
  };
}
