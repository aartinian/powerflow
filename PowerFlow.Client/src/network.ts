import type { NetworkDto } from './types.js';

// Single mutable holder for the currently-loaded network. The client owns
// the network state; the API is stateless compute. UI components subscribe
// to be notified when the user loads a new case or edits the network.
//
// Mutations always replace the whole network (or a top-level array) rather
// than patching in-place — keeps subscribers from missing nested changes,
// and matches the immutable shape of the DTOs on the wire.
export class NetworkStore {
  private current: NetworkDto | null = null;
  private readonly listeners = new Set<(net: NetworkDto | null) => void>();

  get(): NetworkDto | null {
    return this.current;
  }

  set(network: NetworkDto | null): void {
    this.current = network;
    for (const listener of this.listeners) listener(network);
  }

  // Returns an unsubscribe function. Listener is called immediately with
  // the current value so subscribers can initialize without a separate read.
  subscribe(listener: (net: NetworkDto | null) => void): () => void {
    this.listeners.add(listener);
    listener(this.current);
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
