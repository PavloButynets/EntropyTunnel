import { useCallback, useEffect, useRef, useState } from 'react'
import { StatusBar } from './components/StatusBar'
import { ChaosRules } from './components/ChaosRules'
import { MockRules } from './components/MockRules'
import { RoutingRules } from './components/RoutingRules'
import { RequestLog } from './components/RequestLog'
import * as api from './api/client'
import type { AgentInfo, ChaosRule, MockRule, RequestLogEntry, RoutingRule, Tab, TunnelStatus } from './types'
import styles from './App.module.css'

const TABS: { id: Tab; label: string; icon: string }[] = [
  { id: 'chaos',   label: 'Chaos Rules',  icon: '⚡' },
  { id: 'mocks',   label: 'Mocks',        icon: '🎭' },
  { id: 'routing', label: 'Routing',      icon: '🔀' },
  { id: 'log',     label: 'Request Log',  icon: '📋' },
]

export default function App() {
  const [tab, setTab] = useState<Tab>('chaos')
  const [status, setStatus] = useState<TunnelStatus | null>(null)
  const [chaos, setChaos] = useState<ChaosRule[]>([])
  const [mocks, setMocks] = useState<MockRule[]>([])
  const [routing, setRouting] = useState<RoutingRule[]>([])
  const [log, setLog] = useState<RequestLogEntry[]>([])

  // ── Agent switching ─────────────────────────────────────────────────────────
  const [agents, setAgents] = useState<AgentInfo[]>([])
  // '' means "primary / same-origin"
  const [selectedAgentUrl, setSelectedAgentUrl] = useState<string>('')

  const selectAgent = useCallback((apiUrl: string) => {
    setSelectedAgentUrl(apiUrl)
    api.setAgentBase(apiUrl)
    // Immediately refresh everything for the newly selected agent
    setStatus(null)
    setChaos([])
    setMocks([])
    setRouting([])
    setLog([])
  }, [])

  // ── Data fetching ───────────────────────────────────────────────────────────

  const refreshStatus = useCallback(async () => {
    try { setStatus(await api.getStatus()) } catch { /* server not ready */ }
  }, [])

  const refreshChaos = useCallback(async () => {
    try { setChaos(await api.getChaosRules()) } catch { /* ignore */ }
  }, [])

  const refreshMocks = useCallback(async () => {
    try { setMocks(await api.getMockRules()) } catch { /* ignore */ }
  }, [])

  const refreshRouting = useCallback(async () => {
    try { setRouting(await api.getRoutingRules()) } catch { /* ignore */ }
  }, [])

  const refreshLog = useCallback(async () => {
    try { setLog(await api.getRequestLog()) } catch { /* ignore */ }
  }, [])

  // Load rules for the active tab whenever the tab changes
  useEffect(() => {
    if (tab === 'chaos')   refreshChaos()
    if (tab === 'mocks')   refreshMocks()
    if (tab === 'routing') refreshRouting()
    if (tab === 'log')     refreshLog()
  }, [tab, refreshChaos, refreshMocks, refreshRouting, refreshLog])

  // Status polling: every 2 s
  useEffect(() => {
    refreshStatus()
    const id = setInterval(refreshStatus, 2_000)
    return () => clearInterval(id)
  }, [refreshStatus])

  // Agent list polling: every 5 s (always from primary)
  useEffect(() => {
    const refresh = async () => {
      try { setAgents(await api.getAgents()) } catch { /* primary not ready */ }
    }
    refresh()
    const id = setInterval(refresh, 5_000)
    return () => clearInterval(id)
  }, [])

  // Request log auto-refresh when on the log tab
  const logIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  useEffect(() => {
    if (tab === 'log') {
      logIntervalRef.current = setInterval(refreshLog, 2_000)
    }
    return () => {
      if (logIntervalRef.current) clearInterval(logIntervalRef.current)
    }
  }, [tab, refreshLog])

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className={styles.app}>
      <StatusBar
        status={status}
        agents={agents}
        selectedAgentUrl={selectedAgentUrl}
        onSelectAgent={selectAgent}
      />

      <nav className={styles.tabs}>
        {TABS.map(t => (
          <button
            key={t.id}
            className={`${styles.tab} ${tab === t.id ? styles.active : ''}`}
            onClick={() => setTab(t.id)}
          >
            <span>{t.icon}</span>
            <span>{t.label}</span>
          </button>
        ))}
      </nav>

      <main className={styles.main}>
        {tab === 'chaos' && (
          <ChaosRules rules={chaos} onRefresh={refreshChaos} />
        )}
        {tab === 'mocks' && (
          <MockRules rules={mocks} onRefresh={refreshMocks} />
        )}
        {tab === 'routing' && (
          <RoutingRules rules={routing} onRefresh={refreshRouting} />
        )}
        {tab === 'log' && (
          <RequestLog entries={log} onRefresh={refreshLog} />
        )}
      </main>
    </div>
  )
}
