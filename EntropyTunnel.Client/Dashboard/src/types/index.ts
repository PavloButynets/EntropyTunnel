// ── Domain models — must match the C# records (camelCase via STJ) ────────────

export interface ChaosRule {
  id: string
  name: string
  pathPattern: string
  method: string | null
  isEnabled: boolean
  latencyMs: number
  jitterMs: number
  errorRate: number       // 0.0 – 1.0
  errorStatusCode: number
  errorBody: string
}

export interface MockRule {
  id: string
  name: string
  pathPattern: string
  method: string | null
  isEnabled: boolean
  statusCode: number
  contentType: string
  responseBody: string
}

export interface RoutingRule {
  id: string
  name: string
  pathPattern: string
  targetBaseUrl: string
  isEnabled: boolean
  priority: number
}

export interface RequestLogEntry {
  requestId: string
  timestamp: string                         // ISO-8601
  method: string
  path: string
  statusCode: number
  durationMs: number
  appliedChaosRule: string | null
  appliedMockRule: string | null
  resolvedTargetUrl: string | null
  // Full detail — populated by the agent for the Inspector UI
  requestHeaders: Record<string, string> | null
  requestBodyPreview: string | null         // first 4 KB, UTF-8
  requestContentLength: number | null       // total bytes
  responseHeaders: Record<string, string> | null
}

export interface TunnelStatus {
  isConnected: boolean
  publicUrl: string
  uptimeSeconds: number
}

export interface AgentInfo {
  clientId: string
  localPort: number
  apiUrl: string
  isPrimary: boolean
  isConnected: boolean
  publicUrl: string
  lastSeen: string   // ISO-8601
}

export type Tab = 'chaos' | 'mocks' | 'routing' | 'log'
