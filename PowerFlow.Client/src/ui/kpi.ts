import type { NetworkDto, SolveResultDto } from '../types.js';

export interface KpiHandle {
  reset(): void;
  setNetwork(net: NetworkDto | null): void;
  setResult(result: SolveResultDto | null): void;
  setStale(stale: boolean): void;
}

// Compact at-a-glance stat bar above the diagram. Stays meaningful both
// before a solve (just bus-type counts and total load) and after (full
// generation / load / losses / voltage range / max loading).
export function mountKpi(container: HTMLElement): KpiHandle {
  container.innerHTML = `
    <div class="kpi" data-kpi="gen">
      <div class="kpi-value">—</div>
      <div class="kpi-label">Generation</div>
    </div>
    <div class="kpi" data-kpi="load">
      <div class="kpi-value">—</div>
      <div class="kpi-label">Load</div>
    </div>
    <div class="kpi" data-kpi="losses">
      <div class="kpi-value">—</div>
      <div class="kpi-label">Losses</div>
    </div>
    <div class="kpi" data-kpi="vrange">
      <div class="kpi-value">—</div>
      <div class="kpi-label">Voltage range</div>
    </div>
    <div class="kpi" data-kpi="maxload">
      <div class="kpi-value">—</div>
      <div class="kpi-label">Max loading</div>
    </div>
    <div class="kpi" data-kpi="buses">
      <div class="kpi-value">—</div>
      <div class="kpi-label">Buses</div>
    </div>
  `;

  function val(key: string): HTMLElement {
    return container.querySelector<HTMLElement>(`[data-kpi="${key}"] .kpi-value`)!;
  }

  function setText(key: string, text: string, tone: 'normal' | 'warn' | 'error' = 'normal'): void {
    const el = val(key);
    el.textContent = text;
    el.classList.toggle('warn', tone === 'warn');
    el.classList.toggle('error', tone === 'error');
  }

  function setHtml(key: string, html: string): void {
    val(key).innerHTML = html;
  }

  function reset(): void {
    for (const k of ['gen', 'load', 'losses', 'vrange', 'maxload', 'buses']) {
      setText(k, '—');
    }
  }

  function setNetwork(net: NetworkDto | null): void {
    if (!net) {
      reset();
      return;
    }
    const counts = { Slack: 0, PV: 0, PQ: 0, Isolated: 0 };
    for (const b of net.buses) counts[b.type]++;
    setHtml(
      'buses',
      `<span class="bus-pill bus-sl">${counts.Slack} SL</span>` +
        `<span class="bus-pill bus-pv">${counts.PV} PV</span>` +
        `<span class="bus-pill bus-pq">${counts.PQ} PQ</span>`,
    );
    let load = 0;
    for (const b of net.buses) load += b.pd;
    setText('load', `${load.toFixed(1)} MW`);
  }

  function setResult(r: SolveResultDto | null): void {
    if (!r) {
      // Result-derived KPIs go back to '—' so the previous solve's numbers
      // don't linger after a new case load or a network edit.
      for (const k of ['gen', 'losses', 'vrange', 'maxload']) setText(k, '—');
      return;
    }
    if (r.balance) {
      setText('gen', `${r.balance.totalGenerationMw.toFixed(1)} MW`);
      setText('load', `${r.balance.totalLoadMw.toFixed(1)} MW`);
      setText(
        'losses',
        `${r.balance.totalLossesMw.toFixed(1)} MW · ${r.balance.lossPct.toFixed(1)}%`,
      );
    }
    let minVm = Number.POSITIVE_INFINITY;
    let maxVm = Number.NEGATIVE_INFINITY;
    for (const b of r.buses) {
      if (b.vm !== null) {
        if (b.vm < minVm) minVm = b.vm;
        if (b.vm > maxVm) maxVm = b.vm;
      }
    }
    if (Number.isFinite(minVm) && Number.isFinite(maxVm)) {
      const lowTone = minVm < 0.95 ? 'error' : minVm < 0.97 ? 'warn' : 'normal';
      const hiTone = maxVm > 1.05 ? 'error' : maxVm > 1.03 ? 'warn' : 'normal';
      const minSpan = `<span class="vm-edge ${lowTone}">${minVm.toFixed(3)}</span>`;
      const maxSpan = `<span class="vm-edge ${hiTone}">${maxVm.toFixed(3)}</span>`;
      setHtml('vrange', `${minSpan} – ${maxSpan} pu`);
    } else {
      setText('vrange', '— pu');
    }

    let maxLoad: number | null = null;
    for (const br of r.branches) {
      if (br.loadingPct !== null && (maxLoad === null || br.loadingPct > maxLoad)) {
        maxLoad = br.loadingPct;
      }
    }
    if (maxLoad === null) {
      setText('maxload', 'no rated capacity');
    } else {
      const tone = maxLoad >= 100 ? 'error' : maxLoad >= 90 ? 'warn' : 'normal';
      setText('maxload', `${maxLoad.toFixed(1)}%`, tone);
    }
  }

  reset();
  return {
    reset,
    setNetwork,
    setResult,
    setStale(stale) {
      container.classList.toggle('stale', stale);
    },
  };
}
