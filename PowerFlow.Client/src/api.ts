import type {
  CaseMetaDto,
  ContingencyResultDto,
  NetworkDto,
  SolveRequestDto,
  SolveResultDto,
  StreamEvent,
  ValidationResultDto,
} from './types.js';

// The API returns 422 with a ValidationResultDto body when the network fails
// structural checks. We surface that as a typed error so callers can render
// the error list inline instead of throwing into a generic catch.
export class ApiValidationError extends Error {
  constructor(public readonly result: ValidationResultDto) {
    super(result.errors.map((e) => `${e.code}: ${e.message}`).join('; '));
    this.name = 'ApiValidationError';
  }
}

// ── Cases ─────────────────────────────────────────────────────────────────

export async function listCases(): Promise<CaseMetaDto[]> {
  return getJson<CaseMetaDto[]>('/api/cases');
}

export async function loadCase(id: string): Promise<NetworkDto> {
  return getJson<NetworkDto>(`/api/cases/${encodeURIComponent(id)}`);
}

export async function parseCase(content: string): Promise<NetworkDto> {
  return postJson<NetworkDto>('/api/cases/parse', { content });
}

// ── Validate ──────────────────────────────────────────────────────────────

export async function validate(network: NetworkDto): Promise<ValidationResultDto> {
  // /api/validate always returns 200 — even structural errors come back in the
  // result body, not as an HTTP error. So we don't need the 422 path here.
  return postJson<ValidationResultDto>('/api/validate', network);
}

// ── Solve ─────────────────────────────────────────────────────────────────

export async function solve(request: SolveRequestDto): Promise<SolveResultDto> {
  return postJsonExpectingValidation<SolveResultDto>('/api/solve', request);
}

// ── Contingency ───────────────────────────────────────────────────────────

export async function contingency(request: SolveRequestDto): Promise<ContingencyResultDto[]> {
  return postJsonExpectingValidation<ContingencyResultDto[]>('/api/contingency', request);
}

// ── Solve stream (SSE) ────────────────────────────────────────────────────

// POSTs the solve request and parses the resulting SSE stream. EventSource
// can't issue POST, so we use fetch + ReadableStream + manual frame parsing.
// `onEvent` fires for each `iter` / `result` / `error` event. The returned
// promise resolves when the stream closes.
export async function solveStream(
  request: SolveRequestDto,
  onEvent: (event: StreamEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const init: RequestInit = {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
    body: JSON.stringify(request),
  };
  if (signal) init.signal = signal;
  const response = await fetch('/api/solve/stream', init);

  if (response.status === 422) {
    const body = (await response.json()) as ValidationResultDto;
    throw new ApiValidationError(body);
  }
  if (!response.ok || !response.body) {
    throw new Error(`solve/stream failed: ${response.status} ${response.statusText}`);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  // SSE frames are separated by a blank line. Each frame holds one `data: …`
  // line containing the JSON payload.
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let sep: number;
    while ((sep = buffer.indexOf('\n\n')) !== -1) {
      const frame = buffer.slice(0, sep);
      buffer = buffer.slice(sep + 2);
      const payload = extractDataPayload(frame);
      if (payload !== null) onEvent(JSON.parse(payload) as StreamEvent);
    }
  }
}

function extractDataPayload(frame: string): string | null {
  // A frame may contain comment lines (starting with ":") or multi-line data.
  // We only emit single-line JSON payloads, so concatenating "data:" lines is
  // enough — no need to handle multi-line bodies.
  const lines = frame.split('\n');
  const parts: string[] = [];
  for (const line of lines) {
    if (line.startsWith('data:')) parts.push(line.slice(5).trimStart());
  }
  return parts.length > 0 ? parts.join('\n') : null;
}

// ── HTTP helpers ──────────────────────────────────────────────────────────

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url, { headers: { Accept: 'application/json' } });
  if (!response.ok) throw new Error(`GET ${url} failed: ${response.status}`);
  return (await response.json()) as T;
}

async function postJson<T>(url: string, body: unknown): Promise<T> {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`POST ${url} failed: ${response.status}`);
  return (await response.json()) as T;
}

// Variant for endpoints that return 422 + ValidationResultDto on structural
// errors. Lets callers `try { … } catch (e) { if (e instanceof ApiValidationError) … }`.
async function postJsonExpectingValidation<T>(url: string, body: unknown): Promise<T> {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  });
  if (response.status === 422) {
    const result = (await response.json()) as ValidationResultDto;
    throw new ApiValidationError(result);
  }
  if (!response.ok) throw new Error(`POST ${url} failed: ${response.status}`);
  return (await response.json()) as T;
}
