// Top app bar: branding, version, GitHub link, and a manual theme override
// that wins over the OS preference. We persist the override in localStorage
// so the choice survives reloads.

const THEME_KEY = 'powerflow.theme';
type ThemeOverride = 'light' | 'dark' | null;

function readOverride(): ThemeOverride {
  const v = localStorage.getItem(THEME_KEY);
  return v === 'light' || v === 'dark' ? v : null;
}

function writeOverride(theme: ThemeOverride): void {
  if (theme) localStorage.setItem(THEME_KEY, theme);
  else localStorage.removeItem(THEME_KEY);
}

function effectiveTheme(): 'light' | 'dark' {
  const override = readOverride();
  if (override) return override;
  return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function applyTheme(): void {
  const t = effectiveTheme();
  document.documentElement.setAttribute('data-theme', t);
}

export function mountHeader(container: HTMLElement, version: string): void {
  // Apply persisted preference before paint so the user doesn't see a flash
  // of the wrong theme on reload.
  applyTheme();
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (!readOverride()) applyTheme();
  });

  container.innerHTML = `
    <div class="brand">
      <svg class="brand-mark" width="16" height="16" viewBox="0 0 24 24" fill="none"
           stroke="currentColor" stroke-width="2.5" stroke-linecap="round"
           stroke-linejoin="round" aria-hidden="true">
        <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/>
      </svg>
      <span class="brand-name" title="PowerFlow v${version}">PowerFlow</span>
    </div>
    <nav class="header-nav">
      <a href="https://github.com/aartinian/powerflow" target="_blank" rel="noreferrer" class="icon-btn" title="View on GitHub">
        GitHub
      </a>
      <button class="icon-btn" id="theme-toggle" title="Toggle light / dark theme">
        <span id="theme-icon">${effectiveTheme() === 'dark' ? '☀' : '☾'}</span>
      </button>
    </nav>
  `;

  const toggle = container.querySelector<HTMLButtonElement>('#theme-toggle')!;
  const icon = container.querySelector<HTMLSpanElement>('#theme-icon')!;
  toggle.addEventListener('click', () => {
    const next = effectiveTheme() === 'dark' ? 'light' : 'dark';
    writeOverride(next);
    applyTheme();
    icon.textContent = next === 'dark' ? '☀' : '☾';
  });
}
