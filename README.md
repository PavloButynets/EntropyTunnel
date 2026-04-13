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
| **ChaosEngine** | Checks chaos rules. Injects latency using configurable distributions (Uniform, Gaussian, Bimodal, Exponential). Injects errors randomly or in Poisson bursts. Sets `IsHandled = true` if an error is injected. |
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

To password-protect the tunnel's dashboard, pass `--password`:

```bash
dotnet run --project EntropyTunnel.Client -- --password mysecret 3000 myapp
```

On first request, you'll see a simple password page. After authenticating, a cookie is set and stored in browser local storage (for the session). All subsequent requests pass through with the cookie header.

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

## Chaos Rules & Distributions

### Latency Distribution Types

When creating a chaos rule with latency, choose a distribution to simulate realistic network conditions:

| Distribution | Configuration | Use Case |
|---|---|---|
| **Uniform** | Latency ± Jitter | Simple testing, quick validation. All delays equally likely within range. |
| **Gaussian** | Mean = Latency, StdDev = Jitter | Realistic network delays. Most requests cluster around mean, rare extremes. |
| **Bimodal** | Two Gaussians with weights | Bimodal network (e.g., fast local cache + slow remote). First mode weight default 0.95. |
| **Exponential** | Lambda, scaled by base latency | Bursty delays. High variance, tail-heavy (few very slow responses). Typical λ: 0.001–0.1. |

**Examples:**
- Simulate a slow 3G network: `Latency=500ms, Distribution=Gaussian, Jitter=200ms`
- Bimodal (95% fast, 5% slow): `Latency=10ms, Jitter=5ms` + `Mean2=500ms, StdDev2=100ms, Weight1=0.95`

### Error Distribution Types

Choose how errors cluster over time:

| Distribution | Configuration | Behavior |
|---|---|---|
| **Random** | Error Rate (0–100%) | Each request independently has N% chance of error. Errors are uniformly distributed. |
| **Poisson** | Lambda, Burst Duration | Errors cluster in bursts. When triggered, errors occur for N ms at high rate. Simulates partial outages. |

**Examples:**
- Intermittent errors: `Rate=5%, Distribution=Random`
- Simulated outage: `Rate=50%, Distribution=Poisson, Lambda=0.1, BurstDuration=3000ms` (bursts ~3 seconds, 10% chance per second)

### Real-time Metrics Dashboard

The dashboard now streams real-time metrics from the tunnel server:

- **Requests/sec (RPS)** — throughput over the last 60 seconds
- **Latency percentiles** — P50, P95, P99 to track tail latencies
- **Error rate** — percentage of 4xx/5xx responses
- **Request log** — live table of recent requests with method, status, latency
- **Connection count** — active tunnel connections

Metrics update every 1 second via SSE (`GET /api/metrics/stream`). All charts update smoothly without full re-renders.

### Examples

**Example 1: Realistic slow API**

```
Name: Slow payment gateway
Path: /api/pay
Method: POST
Latency: 1000ms
Jitter: 200ms
Distribution: Gaussian
Error Rate: 2%
Error Distribution: Random
Error Code: 503
```

→ Simulates a flaky payment processor: most calls ~1000ms, occasional 503s.

**Example 2: Network with intermittent outage**

```
Name: Intermittent DB
Path: /api/db/*
Latency: 50ms
Distribution: Exponential
Lambda: 0.03
Error Rate: 20%
Error Distribution: Poisson
Lambda: 0.15
Burst Duration: 5000ms
```

→ 50ms base latency, rare spikes. Errors cluster in 5-second bursts at ~15% burst rate.

**Example 3: Dual-mode cache (fast local + slow remote)**

```
Name: Cache miss spikes
Path: /api/cache
Latency: 5ms
Distribution: Bimodal
Mean2: 300ms
StdDev2: 50ms
Weight1: 0.90
```

→ 90% hit cache (5ms), 10% miss remote backend (300ms).

---

## Project Structure

```
EntropyTunnel/
├── EntropyTunnel.Server/          # Central relay server
│   ├── Metrics/                   # MetricsCollector (60-second sliding window, SSE stream)
│   ├── State/                     # AgentState (rules, request log per agent)
│   ├── Sse/                       # SseConnectionManager
│   └── Program.cs                 # WebSocket relay + metrics SSE endpoint
├── EntropyTunnel.Client/
│   ├── Configuration/             # TunnelSettings (with TunnelPassword for auth gate)
│   ├── Dashboard/                 # React + TypeScript SPA
│   │   ├── src/
│   │   │   ├── components/        # ChaosRules (with dist dropdowns), MockRules, RoutingRules, RequestLog, MetricsCharts
│   │   │   ├── context/           # MetricsContext (SSE subscription + reconnect)
│   │   │   ├── api/client.ts      # HTTP API client
│   │   │   └── types/index.ts     # ChaosRule with distributions, TunnelMetricsSnapshot
│   │   └── vite.config.ts
│   ├── Pipeline/                  # IPipelineStage, RequestPipeline, TunnelContext, PathMatcher
│   ├── Stages/                    # AuthGateStage (password protection), MockEngine, ChaosEngine, RequestRouter, LocalForwarder
│   └── Program.cs                 # Host entry; wires up AuthGateStage + metrics recording
├── EntropyTunnel.Core/
│   ├── Models/                    # ChaosRule (with LatencyDistribution, ErrorDistribution enums), MockRule, RoutingRule, RequestLogEntry, TunnelMetricsSnapshot, RequestDataPoint
│   └── DistributionSampler.cs     # Box-Muller, Poisson, Exponential, Bimodal sampling; clamped to [0, 30000] ms
└── README.md
```
