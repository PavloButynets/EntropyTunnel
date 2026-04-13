export type LatencyDistribution = "Uniform" | "Gaussian" | "Bimodal" | "Exponential";
export type ErrorDistribution = "Random" | "Poisson";

export interface ChaosRule {
  id: string;
  name: string;
  pathPattern: string;
  method: string | null;
  isEnabled: boolean;
  latencyMs: number;
  jitterMs: number;
  latencyDistribution: LatencyDistribution;
  bimodalMean2: number;
  bimodalStdDev2: number;
  bimodalWeight1: number;
  exponentialLambda: number;
  errorRate: number; // 0.0 – 1.0
  errorStatusCode: number;
  errorBody: string;
  errorDistribution: ErrorDistribution;
  poissonLambda: number;
  poissonBurstDurationMs: number;
}

export interface MockRule {
  id: string;
  name: string;
  pathPattern: string;
  method: string | null;
  isEnabled: boolean;
  statusCode: number;
  contentType: string;
  responseBody: string;
}

export interface RoutingRule {
  id: string;
  name: string;
  pathPattern: string;
  targetBaseUrl: string;
  isEnabled: boolean;
  priority: number;
}

export interface RequestLogEntry {
  requestId: string;
  timestamp: string;
  method: string;
  path: string;
  statusCode: number;
  durationMs: number;
  appliedChaosRule: string | null;
  appliedMockRule: string | null;
  resolvedTargetUrl: string | null;
  // Full detail — populated by the agent for the Inspector UI
  requestHeaders: Record<string, string> | null;
  requestBodyPreview: string | null; // first 2 KB, UTF-8
  requestContentLength: number | null; // total bytes
  responseHeaders: Record<string, string> | null;
}

export interface AgentInfo {
  clientId: string;
  isConnected: boolean;
  publicUrl: string;
  connectedAt: string | null;
}

export type Tab = "chaos" | "mocks" | "routing" | "log";
