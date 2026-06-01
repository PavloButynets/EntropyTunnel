import { Zap, Sun, Moon } from "lucide-react";
import type { AgentInfo } from "../types";
import { useT } from "../i18n";
import { useTheme } from "../theme";
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
  const { lang, t, toggle: toggleLang } = useT();
  const { theme, toggle: toggleTheme } = useTheme();
  const connected = selectedAgent?.isConnected ?? false;
  const showSwitcher = agents.length > 1;

  return (
    <header className={styles.bar}>
      <div className={styles.logo}>
        <span className={styles.logoIcon}>
          <Zap />
        </span>
        <span className={styles.logoText}>EntropyTunnel</span>
        <span className={styles.logoSub}>{t.statusInspector}</span>
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
        {showSwitcher && (
          <div className={styles.agentSwitcher}>
            <span className={styles.agentLabel}>{t.statusAgent}</span>
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

        <button
          onClick={toggleTheme}
          className={styles.iconToggle}
          title={theme === "dark" ? "Light mode" : "Dark mode"}
        >
          {theme === "dark" ? <Sun size={14} /> : <Moon size={14} />}
        </button>

        <button
          onClick={toggleLang}
          className={styles.langToggle}
          title={lang === "en" ? "Switch to Ukrainian" : "Перемкнути на англійську"}
        >
          {lang === "en" ? "УК" : "EN"}
        </button>

        <span
          className={`${styles.dot} ${connected ? styles.connected : styles.disconnected}`}
        />
        <span className={styles.statusText}>
          {connected ? t.statusConnected : t.statusDisconnected}
        </span>
      </div>
    </header>
  );
}
