# EntropyTunnel

A self-hosted reverse tunnel with a built-in chaos engineering engine, request mocking, dynamic routing, and a real-time web dashboard. Expose any local service to the internet through a central relay server — and intercept every request on the way.

---

## Idea

Most tunnel tools (ngrok, Cloudflare Tunnel) are black boxes: they forward traffic and that's it. EntropyTunnel adds a programmable middleware layer between the public internet and your local service. Every incoming request passes through a configurable pipeline where you can:

- **Mock** — return a canned response without touching the real service
- **Inject chaos** — add latency, jitter, or synthetic errors at a configurable rate
- **Route** — redirect different URL patterns to different local services
- **Inspect** — see every request with full headers, body preview, and timing in a live dashboard

The primary use case is testing and development: simulate slow networks, intermittent failures, or third-party API responses without modifying application code.

---

## Architecture

```
Browser / API Client
        │  HTTP
        ▼
┌───────────────────┐
│  EntropyTunnel    │  Central relay server
│     .Server       │  Accepts WebSocket connections from agents.
│                   │  Routes incoming HTTP requests to the right
│  :8080            │  agent by reading the first DNS label of the
└────────┬──────────┘  host (e.g. app1.example.com → clientId=app1).
         │  WebSocket (binary protocol)
         ▼
┌───────────────────┐
│  EntropyTunnel    │  Agent — runs on the developer's machine.
│     .Client       │  Owns a pipeline of middleware stages.
│                   │
│  Dashboard :4040  │────► MockEngine ──► ChaosEngine ──► RequestRouter ──► LocalForwarder
└───────────────────┘                                                              │
                                                                                   │ HTTP
                                                                                   ▼
                                                                         Local service :5173
```

### Components

| Component | Role |
|---|---|
| **EntropyTunnel.Server** | Central WebSocket relay. Accepts agent connections on `/tunnel?clientId=<id>`, proxies public HTTP traffic to the matching agent using a binary framing protocol. |
| **EntropyTunnel.Client** | Agent process. Maintains a WebSocket to the server, runs the request pipeline, serves the dashboard on `localhost:4040`. |
| **EntropyTunnel.Core** | Shared protocol utilities and data structures. |
| **Dashboard** | React + TypeScript SPA embedded into the Client binary. Manages rules, shows live request logs, supports request replay. |

### Request Pipeline

Each request travels through four stages in order. Any stage can short-circuit the chain by setting `IsHandled = true`.

```
MockEngine → ChaosEngine → RequestRouter → LocalForwarder
```

| Stage | Behaviour |
|---|---|
| **MockEngine** | Checks mock rules. On match: returns the configured response and stops the chain. |
| **ChaosEngine** | Checks chaos rules. Injects latency (`latencyMs ± jitterMs`). With probability `errorRate` returns a synthetic error and stops the chain. |
| **RequestRouter** | Resolves `TargetUrl`. Checks routing rules ordered by priority; falls back to `http://localhost:<localPort>`. Never short-circuits. |
| **LocalForwarder** | Makes the real HTTP request. Forwards all headers using `TryAddWithoutValidation` — .NET's own type system handles the request/content header split with no hardcoded lists. Returns 502 on unreachable service. |

### Wire Protocol

The Server ↔ Client link is a single persistent WebSocket carrying multiplexed binary frames. Every frame starts with a 16-byte request ID so concurrent requests do not interfere.

| Direction | Type | Byte | Payload |
|---|---|---|---|
| Server → Client | Request Header | `0x10` | `[4B len][JSON meta]` |
| Server → Client | Request Body Chunk | `0x11` | `[N bytes]` |
| Server → Client | Request EOF | `0x12` | _(empty)_ |
| Client → Server | Response Header | `0x01` | `[4B status][4B typeLen][contentType][4B headersLen][headersJSON]` |
| Client → Server | Response Body Chunk | `0x02` | `[N bytes]` |
| Client → Server | Response EOF | `0x03` | _(empty)_ |
| Client → Server | Ping | `0x00` | _(empty)_ |

### Multi-Agent Dashboard

Multiple agents on the same machine auto-discover each other. The first agent to bind port 4040 becomes **primary** and serves the React SPA. Subsequent agents bind 4041, 4042, … and register themselves with the primary. The dashboard shows all agents in a single view.

---

## Key Approaches

- **Pipeline pattern** — stages are independent, composable, and testable. Adding a new behaviour means adding a new `IPipelineStage` implementation.
- **No hardcoded header filter lists** — `HttpRequestMessage.Headers.TryAddWithoutValidation` returns `false` for content-level headers (Content-Type, Content-Encoding, …) per .NET's type system, so the request/content-headers split is automatic.
- **Multi-value header safety** — response headers use `Dictionary<string, string[]>` and are written with `StringValues(string[])` on the server side, so `Set-Cookie` lines are never comma-joined.
- **WebSocket frame accumulation** — both server and client use a `do { ReceiveAsync } while (!EndOfMessage)` loop, which handles transport-layer fragmentation of large header payloads (e.g. JWT tokens).
- **Lock-free concurrency** — rule stores use `ConcurrentDictionary`; status fields use `volatile`; the single WebSocket send path uses `SemaphoreSlim(1,1)` to serialize frames without blocking reads.
- **MSBuild-integrated frontend** — `npm install` and `npm run build` run automatically as MSBuild targets before the C# compiler runs, so `dotnet build` produces a self-contained binary with the dashboard already embedded in `wwwroot`.

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/) (for building the dashboard)
- A publicly reachable server to run EntropyTunnel.Server (or use `localhost` for local testing)

---

## Local Setup

### 1. Clone

```bash
git clone <repo-url>
cd EntropyTunnel
```

### 2. Start the Server

```bash
dotnet run --project EntropyTunnel.Server
```

The server listens on `http://0.0.0.0:8080` by default. To change the port, set the `ASPNETCORE_URLS` environment variable or edit `EntropyTunnel.Server/appsettings.json`.

For local testing without a real domain, the server works on `localhost` — requests are routed by the `clientId` query parameter instead of DNS.

### 3. Configure the Client

Edit `EntropyTunnel.Client/appsettings.json`:

```json
{
  "TunnelSettings": {
    "ServerUrl": "ws://localhost:8080/tunnel",
    "PublicDomain": "localhost:8080",
    "ClientId": "myapp",
    "LocalPort": 3000,
    "DashboardPort": 4040
  }
}
```

| Field | Description |
|---|---|
| `ServerUrl` | WebSocket URL of the server (`ws://` or `wss://`) |
| `PublicDomain` | Domain shown in the dashboard public URL label |
| `ClientId` | Unique ID for this agent — becomes the first DNS label in the public URL |
| `LocalPort` | Port of the local service to tunnel traffic to |
| `DashboardPort` | Starting port for the dashboard (auto-increments if taken) |

You can also pass `LocalPort` and `ClientId` as positional CLI arguments to run multiple agents without editing config:

```bash
dotnet run --project EntropyTunnel.Client -- 3000 myapp
dotnet run --project EntropyTunnel.Client -- 4000 otherapp
```

### 4. Start the Client

```bash
dotnet run --project EntropyTunnel.Client
```

On first run, MSBuild runs `npm install` and `npm run build` automatically. The dashboard opens at `http://localhost:4040`.

---

## Dashboard UI Development

To iterate on the React frontend without rebuilding the entire .NET project on every change:

```bash
cd EntropyTunnel.Client/Dashboard
npm install          # first time only
npm run dev
```

Vite starts on `http://localhost:5173` with HMR. API calls to `/api/*` are proxied to the running Client backend at `http://localhost:4040` (configured in `vite.config.ts`).

When you're done, `dotnet build` will pick up the changes automatically.

---

## Building a Self-Contained Executable

To produce a single `.exe` file with the dashboard embedded (no .NET runtime required on the target machine):

```bash
dotnet publish EntropyTunnel.Client \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/client
```

For Linux:

```bash
dotnet publish EntropyTunnel.Client \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/client
```

The MSBuild targets run `npm run build` as part of the publish, so the React dashboard is always up to date in the output binary.

Similarly for the server:

```bash
dotnet publish EntropyTunnel.Server \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/server
```

---

## Project Structure

```
EntropyTunnel/
├── EntropyTunnel.Server/          # Central relay server
│   └── Program.cs
├── EntropyTunnel.Client/
│   ├── Configuration/             # TunnelSettings
│   ├── Dashboard/                 # React + TypeScript SPA
│   │   ├── src/
│   │   │   ├── components/        # ChaosRules, MockRules, RoutingRules, RequestLog, StatusBar
│   │   │   ├── api/client.ts      # HTTP API client
│   │   │   └── types/index.ts     # Shared TypeScript types
│   │   └── vite.config.ts
│   ├── Models/                    # ChaosRule, MockRule, RoutingRule, AgentInfo, RequestLogEntry
│   ├── Pipeline/                  # IPipelineStage, RequestPipeline, TunnelContext, PathMatcher
│   ├── Stages/                    # MockEngine, ChaosEngine, RequestRouter, LocalForwarder
│   ├── Multiplexer/               # TunnelMultiplexer (WebSocket framing)
│   ├── Services/                  # TunnelService, TunnelStatusService, AgentRegistrationService
│   ├── Dashboard/                 # RuleStore, AgentRegistry (in-process state)
│   ├── wwwroot/                   # Built dashboard (generated by MSBuild)
│   └── Program.cs
└── EntropyTunnel.Core/            # Shared protocol utilities
```
