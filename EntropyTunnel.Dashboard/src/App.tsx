import { useCallback, useEffect, useState } from "react";
import {
  Zap,
  Drama,
  GitFork,
  ClipboardList,
  type LucideIcon,
} from "lucide-react";
import { StatusBar } from "./components/StatusBar";
import { ChaosRules } from "./components/ChaosRules";
import { MockRules } from "./components/MockRules";
import { RoutingRules } from "./components/RoutingRules";
import { RequestLog } from "./components/RequestLog";
import { LoginForm } from "./components/LoginForm";
import { AccountOverview } from "./components/AccountOverview";
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

const TABS: { id: Tab; label: string; icon: LucideIcon }[] = [
  { id: "chaos", label: "Chaos Rules", icon: Zap },
  { id: "mocks", label: "Mocks", icon: Drama },
  { id: "routing", label: "Routing", icon: GitFork },
  { id: "log", label: "Request Log", icon: ClipboardList },
];

const routeClientId: string | null = (() => {
  const m = window.location.pathname.match(/^\/dashboard\/([^/?#]+)/);
  return m ? m[1] : null;
})();

const initialToken: string =
  new URLSearchParams(window.location.search).get("token") ?? "";

if (routeClientId) {
  api.setActiveAgent(routeClientId);
}

type AuthState = "checking" | "unauthenticated" | "authenticated";

export default function App() {
  const [authState, setAuthState] = useState<AuthState>("checking");

  const [agents, setAgents] = useState<AgentInfo[]>([]);

  const [tab, setTab] = useState<Tab>("chaos");
  const [selectedClientId, setSelectedClientId] = useState<string>(
    routeClientId ?? "",
  );
  const [chaos, setChaos] = useState<ChaosRule[]>([]);
  const [mocks, setMocks] = useState<MockRule[]>([]);
  const [routing, setRouting] = useState<RoutingRule[]>([]);
  const [log, setLog] = useState<RequestLogEntry[]>([]);

  useEffect(() => {
    const establish = async () => {
      if (initialToken) {
        try {
          await api.login(initialToken);
          setAuthState("authenticated");
          return;
        } catch {
          // Token invalid or expired - fall through to cookie check.
        }
      }
      const ok = await api.checkAuth();
      setAuthState(ok ? "authenticated" : "unauthenticated");
    };
    establish();
  }, []);

  async function handleLogin(password: string) {
    await api.login(password);
    setAuthState("authenticated");
  }

  useEffect(() => {
    if (authState !== "authenticated") return;

    const load = async () => {
      try {
        const data = await api.getAgents();
        setAgents(Array.isArray(data) ? data : []);
      } catch (err) {
        console.error("[agents] load failed:", err);
      }
    };

    load();
    const id = setInterval(() => load(), 5_000);
    return () => clearInterval(id);
  }, [authState]);

  const selectAgent = useCallback((clientId: string) => {
    setSelectedClientId(clientId);
    api.setActiveAgent(clientId);
    setChaos([]);
    setMocks([]);
    setRouting([]);
    setLog([]);
  }, []);
  // Auto-select first agent when agents are available
  useEffect(() => {
    if (!selectedClientId && agents.length > 0) {
      selectAgent(agents[0].clientId);
    }
  }, [agents, selectedClientId, selectAgent]);

  const refreshChaos = useCallback(async () => {
    try {
      const data = await api.getChaosRules();
      setChaos(Array.isArray(data) ? data : []);
    } catch {
      /* ignore */
    }
  }, []);
  const refreshMocks = useCallback(async () => {
    try {
      const data = await api.getMockRules();
      setMocks(Array.isArray(data) ? data : []);
    } catch {
      /* ignore */
    }
  }, []);
  const refreshRouting = useCallback(async () => {
    try {
      const data = await api.getRoutingRules();
      setRouting(Array.isArray(data) ? data : []);
    } catch {
      /* ignore */
    }
  }, []);
  const refreshLog = useCallback(async () => {
    try {
      const data = await api.getRequestLog();
      setLog(Array.isArray(data) ? data : []);
    } catch {
      /* ignore */
    }
  }, []);

  useEffect(() => {
    if (authState !== "authenticated" || !selectedClientId) return;
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

  // SSE live log
  useEffect(() => {
    if (authState !== "authenticated" || !selectedClientId) return;
    const es = new EventSource(api.getEventsUrl(), { withCredentials: true });
    es.onmessage = (ev) => {
      try {
        const entry: RequestLogEntry = JSON.parse(ev.data as string);
        setLog((prev) => [entry, ...prev].slice(0, 1_000));
      } catch {
        /* ignore */
      }
    };
    return () => es.close();
  }, [authState, selectedClientId]);

  if (authState === "checking") return null;

  if (authState === "unauthenticated") {
    return <LoginForm onLogin={handleLogin} />;
  }

  if (!routeClientId) {
    return <AccountOverview agents={agents} />;
  }

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
        {TABS.map((t) => {
          const Icon = t.icon;
          return (
            <button
              key={t.id}
              className={`${styles.tab} ${tab === t.id ? styles.active : ""}`}
              onClick={() => setTab(t.id)}
            >
              <Icon size={16} strokeWidth={1.5} />
              <span>{t.label}</span>
            </button>
          );
        })}
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
