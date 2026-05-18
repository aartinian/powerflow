import type { BranchDto, BusDto, GeneratorDto } from '../types.js';

export interface EditorCallbacks {
  onBusApply: (busId: number, patch: Partial<BusDto>) => void;
  onBusDelete: (busId: number) => void;
  onBranchApply: (branchIndex: number, patch: Partial<BranchDto>) => void;
  onBranchDelete: (branchIndex: number) => void;
  onGeneratorApply: (index: number, patch: Partial<GeneratorDto>) => void;
  onGeneratorDelete: (index: number) => void;
  onClose: () => void;
}

export interface EditorHandle {
  showBus(bus: BusDto, isSlack: boolean): void;
  showBranch(branch: BranchDto): void;
  showGenerator(gen: GeneratorDto): void;
  hide(): void;
}

// Floating edit card pinned under the diagram. Shows the editable subset of
// the currently-selected element. Apply writes back to the store; delete
// removes the element (with cascade for buses). Close discards form input.
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
      <button class="secondary mini editor-close" title="Close"><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>
    `;
    head.querySelector<HTMLDivElement>('.editor-title')!.textContent = title;
    head.querySelector<HTMLDivElement>('.editor-sub')!.textContent = subtitle;
    head.querySelector<HTMLButtonElement>('.editor-close')!.addEventListener('click', () => {
      container.hidden = true;
      cb.onClose();
    });
    return head;
  }

  function actions(
    onApply: () => void,
    deleteHandler: { onDelete: () => void; disabled?: boolean; title?: string } | null,
  ): HTMLElement {
    const row = document.createElement('div');
    row.className = 'editor-actions';
    if (deleteHandler) {
      const del = document.createElement('button');
      del.textContent = 'Delete';
      del.className = 'secondary danger-text';
      del.disabled = !!deleteHandler.disabled;
      if (deleteHandler.title) del.title = deleteHandler.title;
      del.addEventListener('click', () => {
        if (confirm('Delete this element? Linked items will be removed too.')) {
          deleteHandler.onDelete();
        }
      });
      row.append(del);
    }
    const apply = document.createElement('button');
    apply.textContent = 'Apply';
    apply.addEventListener('click', onApply);
    row.append(apply);
    return row;
  }

  function showBus(bus: BusDto, isSlack: boolean): void {
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

    if (isSlack) {
      const note = document.createElement('p');
      note.className = 'editor-slack-note';
      note.textContent =
        'This is the slack (reference) bus. It fixes the system voltage angle reference and cannot be deleted.';
      container.append(note);
    }

    container.append(
      actions(
        () => {
          cb.onBusApply(bus.id, {
            type: readSelect<BusDto['type']>(grid, 'type'),
            pd: readNum(grid, 'pd'),
            qd: readNum(grid, 'qd'),
            vmin: readNum(grid, 'vmin'),
            vmax: readNum(grid, 'vmax'),
          });
        },
        {
          onDelete: () => cb.onBusDelete(bus.id),
          disabled: isSlack,
          title: isSlack ? 'Cannot delete the slack bus' : 'Delete bus and its branches/generators',
        },
      ),
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
      actions(
        () => {
          cb.onBranchApply(branch.index, {
            isInService: (grid.querySelector('[data-field="isInService"]') as HTMLInputElement).checked,
            r: readNum(grid, 'r'),
            x: readNum(grid, 'x'),
            b: readNum(grid, 'b'),
            rateA: readNum(grid, 'rateA'),
          });
        },
        { onDelete: () => cb.onBranchDelete(branch.index) },
      ),
    );
    container.hidden = false;
  }

  function showGenerator(gen: GeneratorDto): void {
    clearForm();
    container.append(header(`Generator #${gen.index}`, `bus ${gen.busId}`));

    const grid = document.createElement('div');
    grid.className = 'editor-grid';
    grid.innerHTML = `
      <label>In service</label>
      <div class="cell-row"><input type="checkbox" data-field="isInService" /></div>
      <label>Pg (MW)</label>
      <input type="number" step="1" data-field="pg" />
      <label>Vg (pu)</label>
      <input type="number" step="0.01" data-field="vg" />
      <label>Pmax (MW)</label>
      <input type="number" step="1" data-field="pmax" />
      <label>Pmin (MW)</label>
      <input type="number" step="1" data-field="pmin" />
      <label>Qmax (MVAr)</label>
      <input type="number" step="1" data-field="qmax" />
      <label>Qmin (MVAr)</label>
      <input type="number" step="1" data-field="qmin" />
    `;
    (grid.querySelector('[data-field="isInService"]') as HTMLInputElement).checked = gen.isInService;
    setVal(grid, 'pg', gen.pg);
    setVal(grid, 'vg', gen.vg);
    setVal(grid, 'pmax', gen.pmax);
    setVal(grid, 'pmin', gen.pmin);
    setVal(grid, 'qmax', gen.qmax);
    setVal(grid, 'qmin', gen.qmin);
    container.append(grid);

    container.append(
      actions(
        () => {
          cb.onGeneratorApply(gen.index, {
            isInService: (grid.querySelector('[data-field="isInService"]') as HTMLInputElement).checked,
            pg: readNum(grid, 'pg'),
            vg: readNum(grid, 'vg'),
            pmax: readNum(grid, 'pmax'),
            pmin: readNum(grid, 'pmin'),
            qmax: readNum(grid, 'qmax'),
            qmin: readNum(grid, 'qmin'),
          });
        },
        { onDelete: () => cb.onGeneratorDelete(gen.index) },
      ),
    );
    container.hidden = false;
  }

  return {
    showBus,
    showBranch,
    showGenerator,
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
