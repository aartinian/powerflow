import type { SolveResultDto, ValidationResultDto } from '../types.js';

// Minimal results panel — solve summary + status line. Per-bus / per-branch
// tables and the convergence chart land in later commits.
export function mountResults(container: HTMLElement) {
  container.innerHTML = `
    <div class="status muted" id="status">No network loaded.</div>
    <div class="summary" id="summary" hidden></div>
    <ul class="errors" id="errors" hidden></ul>
  `;
  const status = container.querySelector<HTMLDivElement>('#status')!;
  const summary = container.querySelector<HTMLDivElement>('#summary')!;
  const errors = container.querySelector<HTMLUListElement>('#errors')!;

  function clearErrors() {
    errors.hidden = true;
    errors.innerHTML = '';
  }

  return {
    setStatus(message: string, kind: 'muted' | 'ok' | 'error' = 'muted') {
      status.className = `status ${kind}`;
      status.textContent = message;
    },
    showSolve(result: SolveResultDto) {
      clearErrors();
      summary.hidden = false;
      summary.textContent = formatSummary(result);
    },
    showValidation(result: ValidationResultDto) {
      summary.hidden = true;
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
      summary.hidden = true;
      clearErrors();
    },
  };
}

function formatSummary(r: SolveResultDto): string {
  const lines: string[] = [];
  lines.push(`mode             ${r.mode}`);
  lines.push(`converged        ${r.converged}`);
  lines.push(`iterations       ${r.iterations}` + (r.outerIterations > 0 ? ` (${r.outerIterations} outer)` : ''));
  lines.push(`max mismatch     ${r.maxMismatch.toExponential(3)} pu`);
  if (r.lambda !== null) lines.push(`lambda           ${r.lambda.toFixed(4)} pu`);

  if (r.balance) {
    const b = r.balance;
    lines.push('');
    lines.push(`gen total        ${b.totalGenerationMw.toFixed(2)} MW   ${b.totalGenerationMvar.toFixed(2)} MVAr`);
    lines.push(`load total       ${b.totalLoadMw.toFixed(2)} MW   ${b.totalLoadMvar.toFixed(2)} MVAr`);
    lines.push(`losses           ${b.totalLossesMw.toFixed(2)} MW   ${b.totalLossesMvar.toFixed(2)} MVAr  (${b.lossPct.toFixed(2)}%)`);
    if (b.totalShuntMvar !== 0) lines.push(`shunt            ${b.totalShuntMvar.toFixed(2)} MVAr`);
  }

  if (r.violations.length > 0) {
    lines.push('');
    lines.push(`voltage violations: ${r.violations.length}`);
  }

  return lines.join('\n');
}
