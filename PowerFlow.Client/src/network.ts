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
