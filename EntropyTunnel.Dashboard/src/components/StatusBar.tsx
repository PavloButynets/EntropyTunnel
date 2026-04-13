import { Zap } from "lucide-react";
import type { AgentInfo } from "../types";
import styles from "./StatusBar.module.css";

interface Props {
  selectedAgent: AgentInfo | null;
  agents: AgentInfo[];
  selectedClientId: string;
  onSelectAgent: (clientId: string) => void;
}

export function StatusBar({
  selectedAgent,
  agents,
  selectedClientId,
  onSelectAgent,
}: Props) {
  const connected = selectedAgent?.isConnected ?? false;
  const showSwitcher = agents.length > 1;

  return (
    <header className={styles.bar}>
      <div className={styles.logo}>
        <span className={styles.logoIcon}>
          <Zap />
        </span>
        <span className={styles.logoText}>EntropyTunnel</span>
        <span className={styles.logoSub}>Inspector</span>
      </div>

      <div className={styles.center}>
        {connected && selectedAgent?.publicUrl && (
          <a
            href={selectedAgent.publicUrl}
            target="_blank"
            rel="noreferrer"
            className={styles.publicUrl}
          >
            {selectedAgent.publicUrl}
          </a>
        )}
      </div>

      <div className={styles.right}>
        {/* Agent switcher — only shown when multiple agents are registered */}
        {showSwitcher && (
          <div className={styles.agentSwitcher}>
            <span className={styles.agentLabel}>Agent:</span>
            <select
              className={styles.agentSelect}
              value={selectedClientId}
              onChange={(e) => onSelectAgent(e.target.value)}
            >
              {agents.map((a) => (
                <option key={a.clientId} value={a.clientId}>
                  {a.clientId}
                  {a.isConnected ? " ●" : " ○"}
                </option>
              ))}
            </select>
          </div>
        )}

        <span
          className={`${styles.dot} ${connected ? styles.connected : styles.disconnected}`}
        />
        <span className={styles.statusText}>
          {connected ? "Connected" : "Disconnected"}
        </span>
      </div>
    </header>
  );
}
