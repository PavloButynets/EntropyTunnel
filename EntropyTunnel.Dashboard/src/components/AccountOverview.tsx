import type { AgentInfo } from "../types";
import { useT } from "../i18n";
import styles from "./AccountOverview.module.css";

interface Props {
  agents: AgentInfo[];
}

export function AccountOverview({ agents }: Props) {
  const { t } = useT();

  function formatRelative(iso: string): string {
    const diff = Date.now() - new Date(iso).getTime();
    const mins = Math.floor(diff / 60_000);
    if (mins < 1) return t.overviewJustNow;
    if (mins < 60) return t.overviewMinAgo(mins);
    return t.overviewHourAgo(Math.floor(mins / 60));
  }

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <span className={styles.headerIcon}>⚡</span>
        <div>
          <h1 className={styles.title}>EntropyTunnel</h1>
          <p className={styles.subtitle}>{t.overviewSubtitle}</p>
        </div>
      </header>

      {agents.length === 0 ? (
        <div className={styles.empty}>
          <p>{t.overviewNoAgents}</p>
          <p className={styles.emptyHint}>
            {t.overviewHint}{" "}
            <code className={styles.code}>dotnet run --project EntropyTunnel.Client</code>{" "}
            {t.overviewHintEnd}
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
                  {t.overviewConnected} {formatRelative(agent.connectedAt)}
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
