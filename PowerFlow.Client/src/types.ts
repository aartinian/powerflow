// TypeScript mirror of PowerFlow.Api DTOs. Field names are camelCase to match
// the API's JsonNamingPolicy.CamelCase serializer. Keep in sync with
// PowerFlow.Api/Dtos/*.cs — the SPA and the API are versioned together.

// ── Network ───────────────────────────────────────────────────────────────

export type BusType = 'PQ' | 'PV' | 'Slack' | 'Isolated';

export interface BusDto {
  id: number;
  type: BusType;
  pd: number;
  qd: number;
  gs: number;
  bs: number;
  vm: number;
  va: number;
  baseKv: number;
  vmax: number;
  vmin: number;
}

export interface BranchDto {
  index: number;
  fromBusId: number;
  toBusId: number;
  r: number;
  x: number;
  b: number;
  tapRatio: number;
  phaseShift: number;
  rateA: number;
  rateB: number;
  rateC: number;
  angmin: number;
  angmax: number;
  isInService: boolean;
}

export interface GeneratorDto {
  index: number;
  busId: number;
  pg: number;
  qg: number;
  qmax: number;
  qmin: number;
  vg: number;
  pmax: number;
  pmin: number;
  isInService: boolean;
  mBase: number;
}

export interface NetworkDto {
  name: string | null;
  baseMva: number;
  buses: BusDto[];
  branches: BranchDto[];
  generators: GeneratorDto[];
}

// ── Solve request / result ────────────────────────────────────────────────

export type SolveMode = 'AC' | 'DC';

export interface SolveOptionsDto {
  mode: SolveMode;
  flatStart: boolean;
  enforceLimits: boolean;
  distributedSlack: boolean;
  warmStartFromDc: boolean;
  tolerance: number;
  maxIterations: number;
}

export interface SolveRequestDto {
  network: NetworkDto;
  options: SolveOptionsDto;
}

export interface SolvedBusDto {
  busId: number;
  vm: number | null;
  va: number;
  pg: number;
  qg: number | null;
  pd: number;
  qd: number | null;
}

export interface SolvedBranchDto {
  branchIndex: number;
  fromBusId: number;
  toBusId: number;
  pij: number;
  qij: number | null;
  pji: number | null;
  qji: number | null;
  lossMw: number | null;
  loadingPct: number | null;
}

export interface SolvedGeneratorDto {
  index: number;
  busId: number;
  pg: number;
  qg: number | null;
  isAtQmax: boolean;
  isAtQmin: boolean;
}

export interface VoltageViolationDto {
  busId: number;
  vm: number;
  isOverVoltage: boolean;
  vmin: number;
  vmax: number;
  baseKv: number;
  vmKv: number;
}

export interface SystemBalanceDto {
  totalGenerationMw: number;
  totalLoadMw: number;
  totalLossesMw: number;
  lossPct: number;
  totalGenerationMvar: number;
  totalLoadMvar: number;
  totalShuntMvar: number;
  totalLossesMvar: number;
}

export interface SolveResultDto {
  mode: SolveMode;
  converged: boolean;
  iterations: number;
  outerIterations: number;
  maxMismatch: number;
  lambda: number | null;
  buses: SolvedBusDto[];
  branches: SolvedBranchDto[];
  generators: SolvedGeneratorDto[];
  violations: VoltageViolationDto[];
  balance: SystemBalanceDto | null;
}

// ── Validation ────────────────────────────────────────────────────────────

export type Severity = 'Error' | 'Warning';

export interface ValidationErrorDto {
  code: string;
  message: string;
  severity: Severity;
}

export interface ValidationResultDto {
  isValid: boolean;
  errors: ValidationErrorDto[];
}

// ── Cases ─────────────────────────────────────────────────────────────────

export interface CaseMetaDto {
  id: string;
  label: string;
  buses: number;
  branches: number;
  generators: number;
}

// ── Contingency ───────────────────────────────────────────────────────────

export interface OverloadDto {
  fromBusId: number;
  toBusId: number;
  loadingPct: number;
}

export interface ContingencyResultDto {
  branchIndex: number;
  fromBusId: number;
  toBusId: number;
  converged: boolean;
  maxLoadingPct: number | null;
  voltageViolationCount: number;
  branchOverloadCount: number;
  overloadedBranches: OverloadDto[];
  voltageViolations: VoltageViolationDto[];
}

// ── SSE events ────────────────────────────────────────────────────────────

export interface BusTypeChangeDto {
  busId: number;
  from: 'PV' | 'PQ';
  to: 'PV' | 'PQ';
  reason: 'Qmax' | 'Qmin' | 'Restored';
}

export interface IterEventDto {
  type: 'iter';
  iter: number;
  mismatch: number;
  busTypeChanges: BusTypeChangeDto[];
}

export interface ResultEventDto {
  type: 'result';
  result: SolveResultDto;
}

export interface ErrorEventDto {
  type: 'error';
  message: string;
}

export type StreamEvent = IterEventDto | ResultEventDto | ErrorEventDto;
