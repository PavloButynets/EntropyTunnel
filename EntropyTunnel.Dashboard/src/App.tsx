import { useCallback, useEffect, useState } from "react";
import { StatusBar } from "./components/StatusBar";
import { ChaosRules } from "./components/ChaosRules";
import { MockRules } from "./components/MockRules";
import { RoutingRules } from "./components/RoutingRules";
import { RequestLog } from "./components/RequestLog";
import { LoginForm } from "./components/LoginForm";
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

function getRouteClientId(): string | null {
  const match = window.location.pathname.match(/^\/dashboard\/([^/?]+)/);
  return match ? match[1] : null;
}

function getTokenParam(): string {
  return new URLSearchParams(window.location.search).get("token") ?? "";
}

type AuthState = "checking" | "unauthenticated" | "authenticated";

export default function App() {
  const [tab, setTab] = useState<Tab>("chaos");
  const [chaos, setChaos] = useState<ChaosRule[]>([]);
  const [mocks, setMocks] = useState<MockRule[]>([]);
  const [routing, setRouting] = useState<RoutingRule[]>([]);
  const [log, setLog] = useState<RequestLogEntry[]>([]);

  const [routeClientId] = useState<string | null>(getRouteClientId);
  const [initialToken] = useState<string>(getTokenParam);

  const [authState, setAuthState] = useState<AuthState>(
    routeClientId ? "checking" : "authenticated",
  );

  const [agents, setAgents] = useState<AgentInfo[]>([]);
  const [selectedClientId, setSelectedClientId] = useState<string>(
    routeClientId ?? "",
  );

  const selectAgent = useCallback((clientId: string) => {
    setSelectedClientId(clientId);
    api.setActiveAgent(clientId);
    setChaos([]);
    setMocks([]);
    setRouting([]);
    setLog([]);
  }, []);

  useEffect(() => {
    if (!routeClientId) return;
    api.setActiveAgent(routeClientId);
    api.checkAuth(routeClientId).then((ok) => {
      setAuthState(ok ? "authenticated" : "unauthenticated");
    });
  }, [routeClientId]);

  async function handleLogin(password: string) {
    await api.login(routeClientId!, password);
    setAuthState("authenticated");
    selectAgent(routeClientId!);
  }

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

  // Load rules for the active tab whenever tab or selected agent changes
  useEffect(() => {
    if (!selectedClientId || authState !== "authenticated") return;
    if (tab === "chaos") refreshChaos();
    if (tab === "mocks") refreshMocks();
    if (tab === "routing") refreshRouting();
    if (tab === "log") refreshLog();
  }, [
    tab,
    selectedClientId,
    authState,
    refreshChaos,
    refreshMocks,
    refreshRouting,
    refreshLog,
  ]);

  // Agent list polling: every 5 s (only in multi-agent mode)
  useEffect(() => {
    if (routeClientId) return; // locked mode uses only one agent
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
  }, [routeClientId]);

  // Auto-select the first agent on initial load (multi-agent mode only)
  useEffect(() => {
    if (routeClientId) return;
    if (agents.length > 0 && !selectedClientId) {
      selectAgent(agents[0].clientId);
    }
  }, [agents, selectedClientId, routeClientId, selectAgent]);

  // SSE: live log stream for the selected agent (only when authenticated)
  useEffect(() => {
    if (!selectedClientId || authState !== "authenticated") return;

    const es = new EventSource(api.getEventsUrl(), { withCredentials: true });

    es.onmessage = (ev) => {
      try {
        const entry: RequestLogEntry = JSON.parse(ev.data as string);
        setLog((prev) => [entry, ...prev].slice(0, 1_000));
      } catch {
        /* ignore malformed events */
      }
    };

    return () => es.close();
  }, [selectedClientId, authState]);

  const selectedAgent =
    agents.find((a) => a.clientId === selectedClientId) ?? null;

  if (routeClientId && authState !== "authenticated") {
    if (authState === "checking") return null; // brief flicker prevention

    return (
      <LoginForm
        clientId={routeClientId}
        initialPassword={initialToken}
        onLogin={handleLogin}
      />
    );
  }

  return (
    <div className={styles.app}>
      <StatusBar
        selectedAgent={selectedAgent}
        agents={routeClientId ? [] : agents}
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
