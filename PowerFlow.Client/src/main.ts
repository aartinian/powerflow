import { listCases, loadCase } from './api.js';
import { network } from './network.js';

const app = document.getElementById('app');
if (!app) throw new Error('#app not found');

// Bootstrap: pull the bundled case list, load the first one, and dump a
// one-line summary to the page. Real UI lands in subsequent commits.
async function bootstrap(): Promise<void> {
  const cases = await listCases();
  if (cases.length === 0) {
    app!.textContent = 'No cases available.';
    return;
  }

  const net = await loadCase(cases[0]!.id);
  network.set(net);
  app!.textContent =
    `Loaded ${net.name ?? cases[0]!.label}: ` +
    `${net.buses.length} buses, ${net.branches.length} branches, ` +
    `${net.generators.length} generators.`;
}

bootstrap().catch((err: unknown) => {
  app.textContent = `Failed to load: ${String(err)}`;
});
