import type { BusTypeChangeDto } from '../types.js';

interface Point {
  iter: number;
  mismatch: number;
  changes: BusTypeChangeDto[];
}

export interface ConvergenceHandle {
  reset(tolerance: number): void;
  addPoint(iter: number, mismatch: number, changes: BusTypeChangeDto[]): void;
  resize(): void;
  destroy(): void;
}

// Canvas-based convergence chart. X-axis is iteration number, Y-axis is the
// base-10 log of the infinity-norm mismatch — log scale because NR typically
// spans 6+ decades from flat-start (~1 pu) to tolerance (~1e-6 pu) and a
// linear plot collapses the interesting late iterations to a flat zero.
//
// Internal point buffer is kept independently of DOM state so the chart can
// keep collecting iter events while another tab is visible; redraw happens
// on every addPoint and whenever the container resizes.
export function mountConvergence(container: HTMLElement): ConvergenceHandle {
  container.innerHTML = '<canvas></canvas>';
  const canvas = container.querySelector<HTMLCanvasElement>('canvas')!;
  const ctx = canvas.getContext('2d')!;

  let points: Point[] = [];
  let tolerance = 1e-6;

  const observer = new ResizeObserver(() => draw());
  observer.observe(container);

  function cssVar(name: string): string {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  }

  function draw(): void {
    const dpr = window.devicePixelRatio || 1;
    const rect = container.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return;

    canvas.width = Math.floor(rect.width * dpr);
    canvas.height = Math.floor(rect.height * dpr);
    canvas.style.width = `${rect.width}px`;
    canvas.style.height = `${rect.height}px`;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, rect.width, rect.height);

    if (points.length === 0) {
      ctx.fillStyle = cssVar('--muted') || '#888';
      ctx.font = '13px system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText('Solve to stream convergence…', rect.width / 2, rect.height / 2);
      return;
    }

    // log10 of every point, clamped so a zero/negative mismatch doesn't NaN
    // the axis. Real solver mismatches are non-negative but we don't trust
    // floating-point exactly — 1e-30 is well below any meaningful tolerance.
    const logsMm = points.map((p) => Math.log10(Math.max(p.mismatch, 1e-30)));
    const logTol = Math.log10(tolerance);
    const yTop = Math.ceil(Math.max(0.5, ...logsMm) + 0.2);
    const yBot = Math.floor(Math.min(logTol - 0.5, ...logsMm));

    const padL = 44;
    const padR = 14;
    const padT = 12;
    const padB = 24;
    const W = rect.width - padL - padR;
    const H = rect.height - padT - padB;
    if (W <= 0 || H <= 0) return;

    const lastIter = Math.max(1, points[points.length - 1]!.iter);
    const xFor = (i: number) => padL + (i / lastIter) * W;
    const yFor = (logMm: number) => padT + ((yTop - logMm) / (yTop - yBot)) * H;

    const border = cssVar('--border') || '#ddd';
    const muted = cssVar('--muted') || '#888';
    const accent = cssVar('--accent') || '#0a66c2';
    const warn = cssVar('--warn') || '#b35c00';
    const ok = cssVar('--ok') || '#1d7d4e';

    // Y grid + log labels (one per integer decade)
    ctx.strokeStyle = border;
    ctx.lineWidth = 1;
    ctx.font = `10px ${cssVar('--mono') || 'monospace'}`;
    ctx.fillStyle = muted;
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (let p = yBot; p <= yTop; p++) {
      const y = yFor(p);
      ctx.beginPath();
      ctx.moveTo(padL, y);
      ctx.lineTo(rect.width - padR, y);
      ctx.stroke();
      ctx.fillText(`1e${p}`, padL - 4, y);
    }

    // Tolerance line
    const tolY = yFor(logTol);
    if (tolY >= padT && tolY <= padT + H) {
      ctx.strokeStyle = ok;
      ctx.setLineDash([4, 3]);
      ctx.beginPath();
      ctx.moveTo(padL, tolY);
      ctx.lineTo(rect.width - padR, tolY);
      ctx.stroke();
      ctx.setLineDash([]);
    }

    // X labels — skip every other when iterations crowd the axis
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.fillStyle = muted;
    const step = lastIter > 20 ? Math.ceil(lastIter / 10) : 1;
    for (let i = 0; i <= lastIter; i += step) {
      ctx.fillText(String(i), xFor(i), padT + H + 4);
    }

    // Bus-type-change markers behind the line so the curve stays readable
    ctx.strokeStyle = warn;
    ctx.setLineDash([2, 3]);
    for (const p of points) {
      if (p.changes.length === 0) continue;
      const x = xFor(p.iter);
      ctx.beginPath();
      ctx.moveTo(x, padT);
      ctx.lineTo(x, padT + H);
      ctx.stroke();
    }
    ctx.setLineDash([]);

    // Connecting polyline
    ctx.strokeStyle = accent;
    ctx.lineWidth = 2;
    ctx.beginPath();
    points.forEach((p, i) => {
      const x = xFor(p.iter);
      const y = yFor(Math.log10(Math.max(p.mismatch, 1e-30)));
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });
    ctx.stroke();

    // Iteration dots
    ctx.fillStyle = accent;
    for (const p of points) {
      const x = xFor(p.iter);
      const y = yFor(Math.log10(Math.max(p.mismatch, 1e-30)));
      ctx.beginPath();
      ctx.arc(x, y, 3, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  return {
    reset(tol) {
      points = [];
      tolerance = tol;
      draw();
    },
    addPoint(iter, mismatch, changes) {
      points.push({ iter, mismatch, changes });
      draw();
    },
    resize() {
      draw();
    },
    destroy() {
      observer.disconnect();
    },
  };
}
