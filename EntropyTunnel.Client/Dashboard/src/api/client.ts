import type { AgentInfo, ChaosRule, MockRule, RoutingRule, RequestLogEntry, TunnelStatus } from '../types'

// Mutable base that switches when the user selects a different agent.
// Defaults to same-origin so the primary always works without configuration.
let _agentBase = '/api'

/** Switch all subsequent API calls to target a different agent. Pass '' to reset to primary. */
export function setAgentBase(apiUrl: string) {
  _agentBase = apiUrl ? `${apiUrl}/api` : '/api'
}

async function req<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${_agentBase}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
  if (res.status === 204) return undefined as T
  if (!res.ok) {
    // Try to extract a meaningful server-side error message
    const text = await res.text().catch(() => res.statusText)
    throw new Error(`${res.status}: ${text}`)
  }
  return res.json() as Promise<T>
}

// ── Agents — always fetched from primary (same origin) ────────────────────────
// Uses a direct fetch so it bypasses _agentBase and always hits the primary registry.
export const getAgents = (): Promise<AgentInfo[]> =>
  fetch('/api/agents', { headers: { 'Content-Type': 'application/json' } })
    .then(r => r.json())

// ── Status ────────────────────────────────────────────────────────────────────
export const getStatus = () => req<TunnelStatus>('/status')

// ── Chaos Rules ───────────────────────────────────────────────────────────────
export const getChaosRules = () => req<ChaosRule[]>('/rules/chaos')

export const addChaosRule = (rule: Omit<ChaosRule, 'id'>) =>
  req<ChaosRule>('/rules/chaos', { method: 'POST', body: JSON.stringify(rule) })

export const updateChaosRule = (id: string, rule: ChaosRule) =>
  req<ChaosRule>(`/rules/chaos/${id}`, { method: 'PUT', body: JSON.stringify(rule) })

export const deleteChaosRule = (id: string) =>
  req<void>(`/rules/chaos/${id}`, { method: 'DELETE' })

export const toggleChaosRule = (id: string) =>
  req<ChaosRule>(`/rules/chaos/${id}/toggle`, { method: 'PATCH' })

// ── Mock Rules ────────────────────────────────────────────────────────────────
export const getMockRules = () => req<MockRule[]>('/rules/mocks')

export const addMockRule = (rule: Omit<MockRule, 'id'>) =>
  req<MockRule>('/rules/mocks', { method: 'POST', body: JSON.stringify(rule) })

export const updateMockRule = (id: string, rule: MockRule) =>
  req<MockRule>(`/rules/mocks/${id}`, { method: 'PUT', body: JSON.stringify(rule) })

export const deleteMockRule = (id: string) =>
  req<void>(`/rules/mocks/${id}`, { method: 'DELETE' })

// ── Routing Rules ─────────────────────────────────────────────────────────────
export const getRoutingRules = () => req<RoutingRule[]>('/rules/routing')

export const addRoutingRule = (rule: Omit<RoutingRule, 'id'>) =>
  req<RoutingRule>('/rules/routing', { method: 'POST', body: JSON.stringify(rule) })

export const updateRoutingRule = (id: string, rule: RoutingRule) =>
  req<RoutingRule>(`/rules/routing/${id}`, { method: 'PUT', body: JSON.stringify(rule) })

export const deleteRoutingRule = (id: string) =>
  req<void>(`/rules/routing/${id}`, { method: 'DELETE' })

// ── Request Log ───────────────────────────────────────────────────────────────
export const getRequestLog = () => req<RequestLogEntry[]>('/log')

export const clearRequestLog = () => req<void>('/log', { method: 'DELETE' })

// ── Replay ────────────────────────────────────────────────────────────────────

export interface ReplayPayload {
  method: string
  path: string
  headers: Record<string, string>
  body: string | null
}

export interface ReplayResponse {
  statusCode: number
  durationMs: number
  headers: Record<string, string>
  body: string
}

export const replayRequest = (payload: ReplayPayload) =>
  req<ReplayResponse>('/replay', { method: 'POST', body: JSON.stringify(payload) })
