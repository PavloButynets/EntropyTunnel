import { useCallback, useEffect, useState } from "react";
import { StatusBar } from "./components/StatusBar";
import { ChaosRules } from "./components/ChaosRules";
import { MockRules } from "./components/MockRules";
import { RoutingRules } from "./components/RoutingRules";
import { RequestLog } from "./components/RequestLog";
import * as api from "./api/client";
import type {
  AgentInfo,
  ChaosRule,
  MockRule,
  RequestLogEntry,
  RoutingRule,
  Tab,
} from "./types";
import styles from "./App.module.css";

const TABS: { id: Tab; label: string; icon: string }[] = [
  { id: "chaos", label: "Chaos Rules", icon: "⚡" },
  { id: "mocks", label: "Mocks", icon: "🎭" },
  { id: "routing", label: "Routing", icon: "🔀" },
  { id: "log", label: "Request Log", icon: "📋" },
];

export default function App() {
  const [tab, setTab] = useState<Tab>("chaos");
  const [chaos, setChaos] = useState<ChaosRule[]>([]);
  const [mocks, setMocks] = useState<MockRule[]>([]);
  const [routing, setRouting] = useState<RoutingRule[]>([]);
  const [log, setLog] = useState<RequestLogEntry[]>([]);

  // ── Agent switching ─────────────────────────────────────────────────────────
  const [agents, setAgents] = useState<AgentInfo[]>([]);
  const [selectedClientId, setSelectedClientId] = useState<string>("");

  const selectAgent = useCallback((clientId: string) => {
    setSelectedClientId(clientId);
    api.setActiveAgent(clientId);
    setChaos([]);
    setMocks([]);
    setRouting([]);
    setLog([]);
  }, []);

  // Data fetching
  const refreshChaos = useCallback(async () => {
    try {
      setChaos(await api.getChaosRules());
    } catch {
      /* ignore */
    }
  }, []);

  const refreshMocks = useCallback(async () => {
    try {
      setMocks(await api.getMockRules());
    } catch {
      /* ignore */
    }
  }, []);

  const refreshRouting = useCallback(async () => {
    try {
      setRouting(await api.getRoutingRules());
    } catch {
      /* ignore */
    }
  }, []);

  const refreshLog = useCallback(async () => {
    try {
      setLog(await api.getRequestLog());
    } catch {
      /* ignore */
    }
  }, []);

  // Load rules for the active tab whenever the tab or selected agent changes
  useEffect(() => {
    if (!selectedClientId) return;
    if (tab === "chaos") refreshChaos();
    if (tab === "mocks") refreshMocks();
    if (tab === "routing") refreshRouting();
    if (tab === "log") refreshLog();
  }, [
    tab,
    selectedClientId,
    refreshChaos,
    refreshMocks,
    refreshRouting,
    refreshLog,
  ]);

  // Agent list polling: every 5 s
  useEffect(() => {
    const refresh = async () => {
      try {
        setAgents(await api.getAgents());
      } catch {
        /* server not ready */
      }
    };
    refresh();
    const id = setInterval(refresh, 5_000);
    return () => clearInterval(id);
  }, []);

  // Auto-select the first agent on initial load
  useEffect(() => {
    if (agents.length > 0 && !selectedClientId) {
      selectAgent(agents[0].clientId);
    }
  }, [agents, selectedClientId, selectAgent]);

  // SSE: live log stream for the selected agent
  useEffect(() => {
    if (!selectedClientId) return;

    const es = new EventSource(api.getEventsUrl());

    es.onmessage = (ev) => {
      try {
        const entry: RequestLogEntry = JSON.parse(ev.data as string);
        setLog((prev) => [entry, ...prev].slice(0, 1_000));
      } catch {
        /* ignore malformed events */
      }
    };

    return () => es.close();
  }, [selectedClientId]);

  const selectedAgent =
    agents.find((a) => a.clientId === selectedClientId) ?? null;

  return (
    <div className={styles.app}>
      <StatusBar
        selectedAgent={selectedAgent}
        agents={agents}
        selectedClientId={selectedClientId}
        onSelectAgent={selectAgent}
      />

      <nav className={styles.tabs}>
        {TABS.map((t) => (
          <button
            key={t.id}
            className={`${styles.tab} ${tab === t.id ? styles.active : ""}`}
            onClick={() => setTab(t.id)}
          >
            <span>{t.icon}</span>
            <span>{t.label}</span>
          </button>
        ))}
      </nav>

      <main className={styles.main}>
        {tab === "chaos" && (
          <ChaosRules rules={chaos} onRefresh={refreshChaos} />
        )}
        {tab === "mocks" && (
          <MockRules rules={mocks} onRefresh={refreshMocks} />
        )}
        {tab === "routing" && (
          <RoutingRules rules={routing} onRefresh={refreshRouting} />
        )}
        {tab === "log" && <RequestLog entries={log} onRefresh={refreshLog} />}
      </main>
    </div>
  );
}
