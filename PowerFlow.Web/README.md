# PowerFlow.Web

Blazor Server front-end for the PowerFlow AC/DC steady-state power-flow solver. Users upload a MATPOWER `.m` case file (or pick a bundled sample), choose solver options, and see the results (bus voltages, branch flows, and a colour-coded network diagram) in the browser without any client-side install.

## What the UI does

```
┌─────────────────── Top bar ────────────────────────┐
│ PowerFlow logo          GitHub link   theme toggle │
├──────────┬─────────────────────────────────────────┤
│          │ ┌──── Diagram ────┬──── Data tabs ────┐ │
│ Sidebar  │ │  Cytoscape.js   │ Buses / Branches  │ │
│          │ │  network graph  │ scrollable tables │ │
│ • Cases  │ └─────────────────┴───────────────────┘ │
│ • Options│   ↑ drag-resizable split pane           │
│ • Solve  │                                         │
├──────────┴─────────────────────────────────────────┤
│ Footer: built by aart · tech stack chips           │
└────────────────────────────────────────────────────┘
```

**Sidebar** — case selection (IEEE 14/30/57/118/300-bus samples + file upload), solver mode (AC / DC), AC options (flat start, Q-limits, distributed slack, DC warm-start), and the *Run solver* button.

**Canvas** — a drag-resizable split pane. Left pane: Cytoscape.js force-directed network diagram. Nodes are coloured by voltage (green = normal, red = under, amber = over for AC; angle-deviation severity for DC). Edges are coloured by branch loading. Right pane: Buses and Branches result tables.

## Project structure

```
PowerFlow.Web/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor          # Top bar, main scroll area, footer
│   │   ├── MainLayout.razor.css      # Shell / topbar / footer styles
│   │   └── ReconnectModal.razor      # Blazor circuit-loss overlay
│   └── Pages/
│       ├── Home.razor                # The entire application (single page)
│       └── Home.razor.css            # Sidebar, canvas, table, diagram styles
├── Middleware/
│   └── SimpleAuthMiddleware.cs       # Optional single-password gate
├── wwwroot/
│   ├── app.css                       # CSS custom properties (theme tokens)
│   ├── app.js                        # Theme toggle + scroll helper
│   ├── diagram.js                    # Cytoscape.js wrapper (ES module)
│   ├── splitter.js                   # Drag-resizable pane divider (ES module)
│   ├── cytoscape.min.js              # Bundled Cytoscape.js (no CDN dependency)
│   └── cases/                        # Bundled IEEE sample .m files
│       └── case14.m  case30.m  case57.m  case118.m  case300.m
└── Program.cs                        # ASP.NET Core pipeline configuration
```

## Key files

### `Program.cs`
Configures the pipeline in deployment order: forwarded headers (Fly.io edge proxy), liveness endpoint (`/healthz`), password middleware, static files, antiforgery, Blazor.

### `Middleware/SimpleAuthMiddleware.cs`
No-op unless `PF_PASSWORD` is set. Redirects every unauthenticated request to `/pf-login`, which renders a minimal inline HTML login form. Authenticated state is stored in an `HttpOnly` cookie whose value is a salted SHA-256 hash of the password. `/healthz` is always bypassed.

### `Components/Pages/Home.razor`
The whole application lives here. It:
- Reads `.m` files via `InputFile` or by fetching bundled samples with `HttpClient`
- Calls `PowerFlow.Core` synchronously on the thread pool (`Task.Run`) so the UI stays responsive
- Renders results into two reactive tables and passes graph data to `diagram.js` via JS interop
- Manages the split-pane lifecycle: imports `splitter.js` and `diagram.js` as ES modules, attaches/re-attaches after Blazor rebuilds the DOM on each new solve

### `wwwroot/diagram.js`
ES module wrapping Cytoscape.js. `render(container, buses, branches, dotNetRef, mode)` tears down any previous instance, picks a layout algorithm (cose for ≤600 buses, concentric rings above that), scales node/edge visuals by graph density, and wires tap handlers that call back into Blazor via `dotNetRef.invokeMethodAsync`. `destroy()` cleans up the Cytoscape instance and removes the `pf-resize` window listener.

### `wwwroot/splitter.js`
ES module for the drag-resizable divider. Stores the split ratio in `localStorage` so it survives page reloads. Re-attach safe: tracks the active handle element reference so it can remove stale listeners when Blazor rebuilds the DOM after a new solve.

### `wwwroot/app.css`
CSS custom properties for both dark and light themes (`[data-theme=dark]` / `[data-theme=light]`). All component stylesheets reference these tokens (`--pf-bg`, `--pf-accent`, etc.) rather than hard-coded colours.

## Running locally

```bash
cd PowerFlow.Web
dotnet run
# → https://localhost:5032
```

No environment variables are required for local development. `PF_PASSWORD` is optional; omitting it leaves the app fully open.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `PF_PASSWORD` | *(unset — open)* | Shared access password for the deployed app |

In production (Fly.io) the app expects a volume mounted at `/data` for Data Protection key persistence. If `/data` doesn't exist, keys are stored in `.keys/` under the app root.
