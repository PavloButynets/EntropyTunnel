# EntropyTunnel

Self-hosted reverse tunnel with a chaos engineering pipeline. Expose a local service to the internet and intercept every request on the way - inject latency, mock responses, or simulate outages without touching your application code.

## How it works

The server runs somewhere publicly reachable. The agent runs on your machine, connects to the server over WebSocket, and keeps that connection alive. When a request comes in for `yourapp.entropy-tunnel.xyz`, the server finds the matching WebSocket connection and forwards the request through it. The agent runs the request through a pipeline, then sends the response back.

```
Internet → nginx → EntropyTunnel.Server → WebSocket → EntropyTunnel.Client → localhost:3000
                                                           │
                                                    MockEngine
                                                    ChaosEngine
                                                    RequestRouter
                                                    LocalForwarder
```

The dashboard is a separate SPA that talks to the server's REST API. The agent is headless.

## Structure

```
EntropyTunnel.Server/     - WebSocket relay + REST API + SSE log stream
  Handlers/               - TunnelHandler (WebSocket), HttpProxyHandler (catch-all proxy)
  Endpoints/              - all /api/* routes
  State/                  - AgentStateStore (rules, logs, per-agent isolation)
  TunnelHub.cs            - shared state and send helpers

EntropyTunnel.Client/     - headless agent, no HTTP server
  Stages/                 - MockEngine, ChaosEngine, RequestRouter, LocalForwarder
  Pipeline/               - IPipelineStage, TunnelContext
  Services/               - TunnelService (BackgroundService, auto-reconnects)
  Multiplexer/            - binary framing over WebSocket

EntropyTunnel.Core/       - shared models, wire protocol
EntropyTunnel.Dashboard/  - React + TypeScript SPA (separate project, VITE_API_URL)
```

## Running locally

Start the server:

```bash
dotnet run --project EntropyTunnel.Server
# listens on :8080
```

Configure the agent in `EntropyTunnel.Client/appsettings.json`:

```json
{
  "TunnelSettings": {
    "ServerUrl": "ws://localhost:8080/tunnel",
    "PublicDomain": "localhost:8080",
    "ClientId": "myapp",
    "LocalPort": 3000
  }
}
```

Start the agent:

```bash
dotnet run --project EntropyTunnel.Client
# or pass port and id directly:
dotnet run --project EntropyTunnel.Client -- 3000 myapp
```

To password-protect the tunnel from the public:

```bash
dotnet run --project EntropyTunnel.Client -- --password secret 3000 myapp
```

Dashboard dev mode (Vite HMR):

```bash
cd EntropyTunnel.Dashboard
npm install && npm run dev
# set VITE_API_URL=http://localhost:8080 in .env
```

## Wire protocol

All traffic between server and agent is multiplexed over a single persistent WebSocket. Every frame has a 17-byte header: 16 bytes request ID (Guid) + 1 byte frame type. `Guid.Empty` as the ID means it's a control frame, not tied to any HTTP request.

| Direction       | Type                | Byte   |
| --------------- | ------------------- | ------ |
| Server → Client | Request Header      | `0x10` |
| Server → Client | Request Body Chunk  | `0x11` |
| Server → Client | Request EOF         | `0x12` |
| Client → Server | Response Header     | `0x01` |
| Client → Server | Response Body Chunk | `0x02` |
| Client → Server | Response EOF        | `0x03` |
| Client → Server | Ping                | `0x00` |
| Server → Client | SyncRules           | `0x20` |
| Client → Server | LogEvent            | `0x21` |
| Server → Client | SessionAuth         | `0x22` |

Rules are stored on the server. When an agent connects, the server pushes the full rule set via `0x20 SyncRules`. After every rule change through the API, the server pushes again. The agent never loses its rules on reconnect.

## Chaos rules

Chaos rules match on HTTP path and method. For matched requests you can:

- **Inject latency** - fixed or with configurable jitter
- **Inject errors** - return a chosen HTTP status code at a given rate (random or burst mode)
- **Mock responses** - return a static body and status, bypassing the local service entirely

Rules are managed through the dashboard UI or directly via the REST API (`/api/{clientId}/rules/chaos`, `/api/{clientId}/rules/mocks`, `/api/{clientId}/rules/routing`).

## Building releases

Single-file self-contained binary:

```bash
# Windows
dotnet publish EntropyTunnel.Client -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/client

# Linux
dotnet publish EntropyTunnel.Client -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./publish/client

# Server
dotnet publish EntropyTunnel.Server -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./publish/server
```

CI publishes `win-x64`, `linux-x64`, and `osx-arm64` binaries on every `v*` tag.
