import type { AgentInfo } from "../types";
import styles from "./AccountOverview.module.css";

interface Props {
  agents: AgentInfo[];
}

function formatRelative(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60_000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins}m ago`;
  return `${Math.floor(mins / 60)}h ago`;
}

export function AccountOverview({ agents }: Props) {
  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <span className={styles.headerIcon}>⚡</span>
        <div>
          <h1 className={styles.title}>EntropyTunnel</h1>
          <p className={styles.subtitle}>Your active tunnels</p>
        </div>
      </header>

      {agents.length === 0 ? (
        <div className={styles.empty}>
          <p>No agents connected.</p>
          <p className={styles.emptyHint}>
            Start an agent with{" "}
            <code className={styles.code}>dotnet run --project EntropyTunnel.Client</code>{" "}
            and it will appear here.
          </p>
        </div>
      ) : (
        <div className={styles.grid}>
          {agents.map((agent) => (
            <a
              key={agent.clientId}
              href={`/dashboard/${agent.clientId}`}
              className={styles.card}
            >
              <div className={styles.cardTop}>
                <span
                  className={`${styles.dot} ${
                    agent.isConnected ? styles.connected : styles.disconnected
                  }`}
                />
                <span className={styles.clientId}>{agent.clientId}</span>
              </div>
              {agent.publicUrl && (
                <div className={styles.publicUrl}>{agent.publicUrl}</div>
              )}
              {agent.connectedAt && (
                <div className={styles.since}>
                  Connected {formatRelative(agent.connectedAt)}
                </div>
              )}
              <div className={styles.arrow}>→</div>
            </a>
          ))}
        </div>
      )}
    </div>
  );
}
