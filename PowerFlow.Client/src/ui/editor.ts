import type { BranchDto, BusDto } from '../types.js';

export interface EditorCallbacks {
  onBusApply: (busId: number, patch: Partial<BusDto>) => void;
  onBranchApply: (branchIndex: number, patch: Partial<BranchDto>) => void;
  onClose: () => void;
}

export interface EditorHandle {
  showBus(bus: BusDto): void;
  showBranch(branch: BranchDto): void;
  hide(): void;
}

// Floating edit card pinned under the diagram. Shows the editable subset of
// the currently-selected element — not every DTO field is exposed, just the
// ones a student is likely to want to play with (loads, voltage bounds,
// branch trip + impedance). Apply is explicit; closing without applying
// discards form input.
export function mountEditor(container: HTMLElement, cb: EditorCallbacks): EditorHandle {
  container.classList.add('editor');
  container.hidden = true;
  container.innerHTML = '';

  function clearForm(): void {
    container.innerHTML = '';
  }

  function header(title: string, subtitle: string): HTMLElement {
    const head = document.createElement('div');
    head.className = 'editor-head';
    head.innerHTML = `
      <div>
        <div class="editor-title"></div>
        <div class="editor-sub"></div>
      </div>
      <button class="secondary mini editor-close" title="Close">×</button>
    `;
    head.querySelector<HTMLDivElement>('.editor-title')!.textContent = title;
    head.querySelector<HTMLDivElement>('.editor-sub')!.textContent = subtitle;
    head.querySelector<HTMLButtonElement>('.editor-close')!.addEventListener('click', () => {
      container.hidden = true;
      cb.onClose();
    });
    return head;
  }

  function actions(onApply: () => void): HTMLElement {
    const row = document.createElement('div');
    row.className = 'editor-actions';
    const apply = document.createElement('button');
    apply.textContent = 'Apply';
    apply.addEventListener('click', onApply);
    row.append(apply);
    return row;
  }

  function showBus(bus: BusDto): void {
    clearForm();
    container.append(header(`Bus ${bus.id}`, `${bus.baseKv.toFixed(1)} kV — ${bus.type}`));

    const grid = document.createElement('div');
    grid.className = 'editor-grid';
    grid.innerHTML = `
      <label>Type</label>
      <select data-field="type">
        <option value="PQ">PQ</option>
        <option value="PV">PV</option>
        <option value="Slack">Slack</option>
      </select>
      <label>Pd (MW)</label>
      <input type="number" step="0.1" data-field="pd" />
      <label>Qd (MVAr)</label>
      <input type="number" step="0.1" data-field="qd" />
      <label>Vmin (pu)</label>
      <input type="number" step="0.01" data-field="vmin" />
      <label>Vmax (pu)</label>
      <input type="number" step="0.01" data-field="vmax" />
    `;
    setVal(grid, 'type', bus.type);
    setVal(grid, 'pd', bus.pd);
    setVal(grid, 'qd', bus.qd);
    setVal(grid, 'vmin', bus.vmin);
    setVal(grid, 'vmax', bus.vmax);
    container.append(grid);

    container.append(
      actions(() => {
        const patch: Partial<BusDto> = {
          type: readSelect<BusDto['type']>(grid, 'type'),
          pd: readNum(grid, 'pd'),
          qd: readNum(grid, 'qd'),
          vmin: readNum(grid, 'vmin'),
          vmax: readNum(grid, 'vmax'),
        };
        cb.onBusApply(bus.id, patch);
      }),
    );
    container.hidden = false;
  }

  function showBranch(branch: BranchDto): void {
    clearForm();
    container.append(
      header(`Branch #${branch.index}`, `${branch.fromBusId} → ${branch.toBusId}`),
    );

    const grid = document.createElement('div');
    grid.className = 'editor-grid';
    grid.innerHTML = `
      <label>In service</label>
      <div class="cell-row"><input type="checkbox" data-field="isInService" /></div>
      <label>R (pu)</label>
      <input type="number" step="0.0001" data-field="r" />
      <label>X (pu)</label>
      <input type="number" step="0.0001" data-field="x" />
      <label>B (pu)</label>
      <input type="number" step="0.001" data-field="b" />
      <label>RateA (MVA)</label>
      <input type="number" step="1" data-field="rateA" />
    `;
    (grid.querySelector('[data-field="isInService"]') as HTMLInputElement).checked = branch.isInService;
    setVal(grid, 'r', branch.r);
    setVal(grid, 'x', branch.x);
    setVal(grid, 'b', branch.b);
    setVal(grid, 'rateA', branch.rateA);
    container.append(grid);

    container.append(
      actions(() => {
        const patch: Partial<BranchDto> = {
          isInService: (grid.querySelector('[data-field="isInService"]') as HTMLInputElement)
            .checked,
          r: readNum(grid, 'r'),
          x: readNum(grid, 'x'),
          b: readNum(grid, 'b'),
          rateA: readNum(grid, 'rateA'),
        };
        cb.onBranchApply(branch.index, patch);
      }),
    );
    container.hidden = false;
  }

  return {
    showBus,
    showBranch,
    hide() {
      container.hidden = true;
      clearForm();
    },
  };
}

// ── Small DOM helpers ──────────────────────────────────────────────────────

function setVal(root: HTMLElement, field: string, value: string | number): void {
  const el = root.querySelector(`[data-field="${field}"]`) as
    | HTMLInputElement
    | HTMLSelectElement
    | null;
  if (el) el.value = String(value);
}

function readNum(root: HTMLElement, field: string): number {
  const el = root.querySelector(`[data-field="${field}"]`) as HTMLInputElement;
  return parseFloat(el.value);
}

function readSelect<T extends string>(root: HTMLElement, field: string): T {
  const el = root.querySelector(`[data-field="${field}"]`) as HTMLSelectElement;
  return el.value as T;
}
