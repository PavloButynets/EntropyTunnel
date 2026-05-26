import type {
  AgentInfo,
  ChaosRule,
  MockRule,
  RoutingRule,
  RequestLogEntry,
} from "../types";

// Base URL of the server. Empty string means same-origin (nginx proxy in Docker,
// Vite dev proxy otherwise). Set VITE_API_URL in .env for standalone dev.
const _apiBase = (import.meta.env.VITE_API_URL ?? "").replace(/\/$/, "");

// Currently selected agent clientId — switched via setActiveAgent().
let _clientId = "";

/** Switch all subsequent API calls to target a different agent. */
export function setActiveAgent(clientId: string) {
  _clientId = clientId;
}

/** Build a URL scoped to the active agent. */
function agentUrl(path: string) {
  return `${_apiBase}/api/agents/${_clientId}${path}`;
}

async function req<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    ...init,
  });
  if (res.status === 204) return undefined as T;
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(`${res.status}: ${text}`);
  }
  return res.json() as Promise<T>;
}

// Auth
export async function login(clientId: string, password: string): Promise<void> {
  const res = await fetch(`${_apiBase}/api/auth/login`, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ clientId, password }),
  });
  if (!res.ok) throw new Error(`${res.status}`);
}

export async function checkAuth(): Promise<boolean> {
  const res = await fetch(`${_apiBase}/api/auth/me`, {
    credentials: "include",
  });
  return res.ok;
}

// Agents
export const getAgents = (): Promise<AgentInfo[]> =>
  req<AgentInfo[]>(`${_apiBase}/api/agents`);

//  Chaos Rules
export const getChaosRules = () => req<ChaosRule[]>(agentUrl("/rules/chaos"));

export const addChaosRule = (rule: Omit<ChaosRule, "id">) =>
  req<ChaosRule>(agentUrl("/rules/chaos"), {
    method: "POST",
    body: JSON.stringify(rule),
  });

export const updateChaosRule = (id: string, rule: ChaosRule) =>
  req<ChaosRule>(agentUrl(`/rules/chaos/${id}`), {
    method: "PUT",
    body: JSON.stringify(rule),
  });

export const deleteChaosRule = (id: string) =>
  req<void>(agentUrl(`/rules/chaos/${id}`), { method: "DELETE" });

export const toggleChaosRule = (id: string) =>
  req<ChaosRule>(agentUrl(`/rules/chaos/${id}/toggle`), { method: "PATCH" });

// Mock Rules
export const getMockRules = () => req<MockRule[]>(agentUrl("/rules/mocks"));

export const addMockRule = (rule: Omit<MockRule, "id">) =>
  req<MockRule>(agentUrl("/rules/mocks"), {
    method: "POST",
    body: JSON.stringify(rule),
  });

export const updateMockRule = (id: string, rule: MockRule) =>
  req<MockRule>(agentUrl(`/rules/mocks/${id}`), {
    method: "PUT",
    body: JSON.stringify(rule),
  });

export const deleteMockRule = (id: string) =>
  req<void>(agentUrl(`/rules/mocks/${id}`), { method: "DELETE" });

export const toggleMockRule = (id: string) =>
  req<MockRule>(agentUrl(`/rules/mocks/${id}/toggle`), { method: "PATCH" });

// Routing Rules
export const getRoutingRules = () =>
  req<RoutingRule[]>(agentUrl("/rules/routing"));

export const addRoutingRule = (rule: Omit<RoutingRule, "id">) =>
  req<RoutingRule>(agentUrl("/rules/routing"), {
    method: "POST",
    body: JSON.stringify(rule),
  });

export const updateRoutingRule = (id: string, rule: RoutingRule) =>
  req<RoutingRule>(agentUrl(`/rules/routing/${id}`), {
    method: "PUT",
    body: JSON.stringify(rule),
  });

export const deleteRoutingRule = (id: string) =>
  req<void>(agentUrl(`/rules/routing/${id}`), { method: "DELETE" });

// Request Log
export const getRequestLog = () => req<RequestLogEntry[]>(agentUrl("/log"));

export const clearRequestLog = () =>
  req<void>(agentUrl("/log"), { method: "DELETE" });

// Server-Sent Events - live log stream
/** Returns the SSE URL for the currently active agent. */
export const getEventsUrl = () => agentUrl("/events");

export interface ReplayPayload {
  method: string;
  path: string;
  headers: Record<string, string>;
  body: string | null;
}

export interface ReplayResponse {
  statusCode: number;
  durationMs: number;
  headers: Record<string, string>;
  body: string;
}

export const replayRequest = (payload: ReplayPayload) =>
  req<ReplayResponse>(agentUrl("/replay"), {
    method: "POST",
    body: JSON.stringify(payload),
  });
