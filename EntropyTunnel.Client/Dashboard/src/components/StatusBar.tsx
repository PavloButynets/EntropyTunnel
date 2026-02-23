import type { AgentInfo, TunnelStatus } from '../types'
import styles from './StatusBar.module.css'

interface Props {
  status: TunnelStatus | null
  agents: AgentInfo[]
  selectedAgentUrl: string
  onSelectAgent: (apiUrl: string) => void
}

function formatUptime(seconds: number): string {
  if (seconds < 60) return `${seconds}s`
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${seconds % 60}s`
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  return `${h}h ${m}m`
}

export function StatusBar({ status, agents, selectedAgentUrl, onSelectAgent }: Props) {
  const connected = status?.isConnected ?? false
  // The primary agent has apiUrl matching the same origin; value '' targets primary.
  const showSwitcher = agents.length > 1

  return (
    <header className={styles.bar}>
      <div className={styles.logo}>
        <span className={styles.logoIcon}>⚡</span>
        <span className={styles.logoText}>EntropyTunnel</span>
        <span className={styles.logoSub}>Inspector</span>
      </div>

      <div className={styles.center}>
        {connected && status?.publicUrl && (
          <a
            href={status.publicUrl}
            target="_blank"
            rel="noreferrer"
            className={styles.publicUrl}
          >
            {status.publicUrl}
          </a>
        )}
      </div>

      <div className={styles.right}>
        {/* Agent switcher — only shown when multiple agents are running */}
        {showSwitcher && (
          <div className={styles.agentSwitcher}>
            <span className={styles.agentLabel}>Agent:</span>
            <select
              className={styles.agentSelect}
              value={selectedAgentUrl}
              onChange={e => onSelectAgent(e.target.value)}
            >
              {agents.map(a => (
                <option key={a.clientId} value={a.isPrimary ? '' : a.apiUrl}>
                  {a.clientId}
                  {a.isPrimary ? ' (primary)' : ''}
                  {a.isConnected ? ' ●' : ' ○'}
                </option>
              ))}
            </select>
          </div>
        )}

        {connected && status && (
          <span className={styles.uptime}>
            ↑ {formatUptime(status.uptimeSeconds)}
          </span>
        )}
        <span className={`${styles.dot} ${connected ? styles.connected : styles.disconnected}`} />
        <span className={styles.statusText}>
          {connected ? 'Connected' : 'Disconnected'}
        </span>
      </div>
    </header>
  )
}
