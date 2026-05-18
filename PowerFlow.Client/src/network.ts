import type { BranchDto, BusDto, GeneratorDto, NetworkDto } from './types.js';

// 'load'     = fresh network from the API; topology changed, any previous solve
//              result is meaningless, diagram should re-run its layout.
// 'edit'     = scalar values changed on the existing topology — buses / branches
//              still match by id+index, diagram can patch in place.
// 'topology' = elements added or removed; bus / branch / gen sets differ but
//              we'd like to keep positions if possible. In practice the diagram
//              treats this the same as 'load' and re-runs the layout.
export type NetworkChangeKind = 'load' | 'edit' | 'topology';

// Single mutable holder for the currently-loaded network. The client owns
// the network state; the API is stateless compute. UI components subscribe
// to be notified when the user loads a new case or edits the network.
//
// Mutations always replace the whole network (or a top-level array) rather
// than patching in-place — keeps subscribers from missing nested changes,
// and matches the immutable shape of the DTOs on the wire.
export class NetworkStore {
  private current: NetworkDto | null = null;
  private readonly listeners = new Set<
    (net: NetworkDto | null, kind: NetworkChangeKind) => void
  >();

  get(): NetworkDto | null {
    return this.current;
  }

  set(network: NetworkDto | null, kind: NetworkChangeKind = 'load'): void {
    this.current = network;
    for (const listener of this.listeners) listener(network, kind);
  }

  // Returns an unsubscribe function. Listener is called immediately with
  // the current value so subscribers can initialize without a separate read.
  subscribe(
    listener: (net: NetworkDto | null, kind: NetworkChangeKind) => void,
  ): () => void {
    this.listeners.add(listener);
    listener(this.current, 'load');
    return () => this.listeners.delete(listener);
  }
}

// Module-level singleton — there's only one network loaded at a time.
export const network = new NetworkStore();

// Returns a copy of `net` with every bus's Pd / Qd multiplied by `factor`.
// Used by the load-scaling slider — kept as a pure function so the canonical
// network in the store stays at the originally-loaded values and re-scaling
// from 120% to 90% is relative to the base, not compounded.
export function scaleLoads(net: NetworkDto, factor: number): NetworkDto {
  if (factor === 1) return net;
  return {
    ...net,
    buses: net.buses.map((b) => ({ ...b, pd: b.pd * factor, qd: b.qd * factor })),
  };
}

// Total active load (sum of Pd across all buses), in MW.
export function totalLoadMw(net: NetworkDto): number {
  let sum = 0;
  for (const b of net.buses) sum += b.pd;
  return sum;
}

// Returns a copy of `net` with the bus at `busId` patched. Other buses (and
// the rest of the network) are shared.
export function updateBus(
  net: NetworkDto,
  busId: number,
  patch: Partial<BusDto>,
): NetworkDto {
  return {
    ...net,
    buses: net.buses.map((b) => (b.id === busId ? { ...b, ...patch } : b)),
  };
}

// Returns a copy of `net` with the branch at `branchIndex` patched.
export function updateBranch(
  net: NetworkDto,
  branchIndex: number,
  patch: Partial<BranchDto>,
): NetworkDto {
  return {
    ...net,
    branches: net.branches.map((b) =>
      b.index === branchIndex ? { ...b, ...patch } : b,
    ),
  };
}

// Returns a copy of `net` with the generator at `index` patched.
export function updateGenerator(
  net: NetworkDto,
  index: number,
  patch: Partial<GeneratorDto>,
): NetworkDto {
  return {
    ...net,
    generators: net.generators.map((g) =>
      g.index === index ? { ...g, ...patch } : g,
    ),
  };
}

// ── Topology mutations ──────────────────────────────────────────────────────
// All add* functions return both the new network and the newly-created
// id / index so callers can immediately select the new element in the editor.

export function addBus(
  net: NetworkDto,
): { net: NetworkDto; busId: number } {
  const nextId = net.buses.length === 0 ? 1 : Math.max(...net.buses.map((b) => b.id)) + 1;
  const bus: BusDto = {
    id: nextId,
    type: 'PQ',
    pd: 0,
    qd: 0,
    gs: 0,
    bs: 0,
    vm: 1,
    va: 0,
    baseKv: 0,
    vmax: 1.1,
    vmin: 0.9,
  };
  return { net: { ...net, buses: [...net.buses, bus] }, busId: nextId };
}

// Removes the bus and cascades to any branches / generators attached to it.
// The slack bus cannot be removed — that would leave the network unsolvable
// and the API would reject it anyway.
export function removeBus(net: NetworkDto, busId: number): NetworkDto {
  return {
    ...net,
    buses: net.buses.filter((b) => b.id !== busId),
    branches: net.branches.filter(
      (b) => b.fromBusId !== busId && b.toBusId !== busId,
    ),
    generators: net.generators.filter((g) => g.busId !== busId),
  };
}

export function addBranch(
  net: NetworkDto,
  fromBusId: number,
  toBusId: number,
): { net: NetworkDto; index: number } {
  const nextIndex = net.branches.length === 0 ? 0 : Math.max(...net.branches.map((b) => b.index)) + 1;
  const branch: BranchDto = {
    index: nextIndex,
    fromBusId,
    toBusId,
    r: 0.01,
    x: 0.1,
    b: 0,
    tapRatio: 1,
    phaseShift: 0,
    rateA: 0,
    rateB: 0,
    rateC: 0,
    angmin: -360,
    angmax: 360,
    isInService: true,
  };
  return { net: { ...net, branches: [...net.branches, branch] }, index: nextIndex };
}

export function removeBranch(net: NetworkDto, branchIndex: number): NetworkDto {
  return {
    ...net,
    branches: net.branches.filter((b) => b.index !== branchIndex),
  };
}

export function addGenerator(
  net: NetworkDto,
  busId: number,
): { net: NetworkDto; index: number } {
  const nextIndex = net.generators.length === 0
    ? 0
    : Math.max(...net.generators.map((g) => g.index)) + 1;
  const gen: GeneratorDto = {
    index: nextIndex,
    busId,
    pg: 0,
    qg: 0,
    qmax: 100,
    qmin: -100,
    vg: 1,
    pmax: 100,
    pmin: 0,
    isInService: true,
    mBase: 100,
  };
  return { net: { ...net, generators: [...net.generators, gen] }, index: nextIndex };
}

export function removeGenerator(net: NetworkDto, index: number): NetworkDto {
  return {
    ...net,
    generators: net.generators.filter((g) => g.index !== index),
  };
}
