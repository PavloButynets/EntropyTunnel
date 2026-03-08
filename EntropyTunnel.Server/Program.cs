using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EntropyTunnel.Core;
using EntropyTunnel.Core.Models;
using EntropyTunnel.Core.Payloads;
using EntropyTunnel.Server.Sse;
using EntropyTunnel.Server.State;
using Microsoft.Extensions.Primitives;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins").Get<string[]>();

var dashboardBaseUrl = builder.Configuration
    .GetValue("DashboardBaseUrl", "http://localhost:5173")!;

builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<SseConnectionManager>();

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyMethod()
         .AllowAnyHeader()));

builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", opts =>
    {
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SameSite = SameSiteMode.Strict;
        opts.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        opts.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// ── App ────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseWebSockets();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

var _stateStore = app.Services.GetRequiredService<AgentStateStore>();
var _sseMgr = app.Services.GetRequiredService<SseConnectionManager>();

// Active WebSocket connections, keyed by clientId
var _connections = new ConcurrentDictionary<string, AgentConnection>(StringComparer.OrdinalIgnoreCase);

// HTTP proxy state: pending tcs awaiting agent response headers
var _pendingRequests = new ConcurrentDictionary<
    Guid,
    TaskCompletionSource<(string ContentType, int StatusCode, ChannelReader<byte[]> BodyReader, Dictionary<string, string[]> Headers)>>();

// Active body channels for in-flight streamed responses
var _activeChannels = new ConcurrentDictionary<Guid, Channel<byte[]>>();

// Per-account passwords — generated on first connect for an account, reused for subsequent agents
var _accountPasswords = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

// ── Helper: send a raw binary packet to a specific agent ───────────────────────

async Task SendToAgentAsync(string clientId, byte[] packet)
{
    if (!_connections.TryGetValue(clientId, out var conn)
        || conn.Socket.State != WebSocketState.Open) return;

    await conn.Lock.WaitAsync();
    try
    {
        if (conn.Socket.State == WebSocketState.Open)
            await conn.Socket.SendAsync(
                new ArraySegment<byte>(packet),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                CancellationToken.None);
    }
    finally { conn.Lock.Release(); }
}

// Helper: push current rule snapshot to agent via 0x20 SyncRules

async Task SyncRulesToAgentAsync(string clientId)
{
    var state = _stateStore.Get(clientId);
    if (state is null) return;

    var payload = new SyncRulesPayload
    {
        ChaosRules = [.. state.ChaosRules.Values.OrderBy(r => r.Name)],
        MockRules = [.. state.MockRules.Values.OrderBy(r => r.Name)],
        RoutingRules = [.. state.RoutingRules.Values.OrderBy(r => r.Priority)],
    };

    await SendToAgentAsync(clientId, ControlFrameBuilder.Build(ControlFrame.SyncRules, payload));
}

static string GeneratePassword()
{
    const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
    var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);
    return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
}

// WebSocket tunnel endpoint

app.Map("/tunnel", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
        return Results.BadRequest("WebSocket connection required.");

    string? clientId = context.Request.Query["clientId"];
    if (string.IsNullOrEmpty(clientId))
        return Results.BadRequest("Missing clientId query parameter.");

    using var ws = await context.WebSockets.AcceptWebSocketAsync();

    var conn = new AgentConnection(ws, new SemaphoreSlim(1, 1));
    _connections[clientId] = conn;

    var state = _stateStore.GetOrCreate(clientId);
    state.IsConnected = true;
    state.ConnectedAt = DateTimeOffset.UtcNow;

    Console.WriteLine($"[Server] AGENT CONNECTED: {clientId}");

    // Associate agent with its account
    string accountIdRaw = context.Request.Query["accountId"].ToString();
    string agentAccountId = !string.IsNullOrEmpty(accountIdRaw) ? accountIdRaw : clientId;
    state.AccountId = agentAccountId;

    // Send 0x22 SessionAuth immediately on connect - one shared password per account
    var password = _accountPasswords.GetOrAdd(agentAccountId, _ => GeneratePassword());
    var authPayload = new SessionAuthPayload
    {
        DashboardUrl = $"{dashboardBaseUrl}/dashboard?token={password}",
        Token = password,
        Password = password,
    };
    await SendToAgentAsync(clientId, ControlFrameBuilder.Build(ControlFrame.SessionAuth, authPayload));

    // Send 0x20 SyncRules with current stored rules
    await SyncRulesToAgentAsync(clientId);

    // Receive loop
    var receiveBuffer = new byte[64 * 1024];

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                ms.Write(receiveBuffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            var packet = ms.ToArray();
            if (packet.Length == 0) continue;

            // Single-byte keep-alive ping (0x00) — ignore
            if (packet.Length == 1 && packet[0] == 0x00) continue;
            if (packet.Length < 17) continue;

            var idBytes = new byte[16];
            Array.Copy(packet, 0, idBytes, 0, 16);
            var id = new Guid(idBytes);

            byte packetType = packet[16];

            // - 0x21 LogEvent (control frame: id == Guid.Empty)
            if (packetType == ControlFrame.LogEvent && id == Guid.Empty)
            {
                if (packet.Length < 21) continue;

                int jsonLen = BitConverter.ToInt32(packet, 17);
                if (jsonLen <= 0 || packet.Length < 21 + jsonLen) continue;

                string json = Encoding.UTF8.GetString(packet, 21, jsonLen);
                var entry = ControlFrameBuilder.Deserialize<RequestLogEntry>(json);
                if (entry is not null)
                {
                    state.AddLogEntry(entry);
                    await _sseMgr.BroadcastAsync(clientId, json);
                }
                continue;
            }

            // 0x01 Response Header
            if (packetType == 0x01)
            {
                int statusCode = BitConverter.ToInt32(packet, 17);
                int typeLen = BitConverter.ToInt32(packet, 21);
                string contentType = Encoding.UTF8.GetString(packet, 25, typeLen);

                int headersJsonLen = BitConverter.ToInt32(packet, 25 + typeLen);
                string headersJson = Encoding.UTF8.GetString(packet, 29 + typeLen, headersJsonLen);
                var headers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(headersJson)
                              ?? new Dictionary<string, string[]>();

                var channel = Channel.CreateUnbounded<byte[]>();
                _activeChannels[id] = channel;

                if (_pendingRequests.TryRemove(id, out var tcs))
                    tcs.SetResult((contentType, statusCode, channel.Reader, headers));
            }
            // 0x02 Response Body Chunk
            else if (packetType == 0x02)
            {
                if (_activeChannels.TryGetValue(id, out var channel))
                {
                    var chunk = new byte[packet.Length - 17];
                    Array.Copy(packet, 17, chunk, 0, chunk.Length);
                    await channel.Writer.WriteAsync(chunk);
                }
            }
            // 0x03 Response EOF
            else if (packetType == 0x03)
            {
                if (_activeChannels.TryRemove(id, out var channel))
                    channel.Writer.Complete();
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Server] Error ({clientId}): {ex.Message}");
    }
    finally
    {
        _connections.TryRemove(clientId, out _);
        state.IsConnected = false;
        Console.WriteLine($"[Server] AGENT DISCONNECTED: {clientId}");
    }

    return Results.Empty;
});


var api = app.MapGroup("/api");

api.MapGet("/ping", () => new { app = "EntropyTunnel.Server", version = "2.0" });

api.MapPost("/auth/login", async (LoginRequest body, HttpContext ctx) =>
{
    var match = _accountPasswords.FirstOrDefault(kvp => kvp.Value == body.Password);
    if (match.Key is null) return Results.StatusCode(401);

    var claims = new[] { new Claim(ClaimTypes.Name, match.Key) }; // Name = accountId
    var identity = new ClaimsIdentity(claims, "Cookies");
    await ctx.SignInAsync("Cookies", new ClaimsPrincipal(identity));
    return Results.Ok(new { accountId = match.Key });
});

api.MapGet("/agents", (HttpContext ctx, AgentStateStore store) =>
{
    var accountId = ctx.User.Identity?.Name;
    if (string.IsNullOrEmpty(accountId)) return Results.StatusCode(401);

    var list = store.GetByAccount(accountId).Select(t => new
    {
        clientId = t.ClientId,
        isConnected = t.State.IsConnected,
        publicUrl = t.State.PublicUrl,
        connectedAt = t.State.ConnectedAt,
    });
    return Results.Ok(list);
});

// Lightweight auth probe — returns accountId from cookie or 401
api.MapGet("/auth/me", (HttpContext ctx) =>
{
    var accountId = ctx.User.Identity?.Name;
    if (string.IsNullOrEmpty(accountId)) return Results.StatusCode(401);
    return Results.Ok(new { accountId });
});


var agentApi = api.MapGroup("/agents/{clientId}");

agentApi.AddEndpointFilter(async (ctx, next) =>
{
    var authenticatedAccountId = ctx.HttpContext.User.Identity?.Name;
    if (string.IsNullOrEmpty(authenticatedAccountId))
        return Results.StatusCode(401);

    var routeClientId = ctx.HttpContext.GetRouteValue("clientId") as string ?? "";
    var agentState = _stateStore.Get(routeClientId);
    if (agentState is null || !agentState.AccountId.Equals(authenticatedAccountId, StringComparison.OrdinalIgnoreCase))
        return Results.StatusCode(403);

    return await next(ctx);
});

agentApi.MapGet("/status", (string clientId, AgentStateStore store) =>
{
    var state = store.Get(clientId);
    if (state is null) return Results.NotFound($"Agent '{clientId}' not found.");
    return Results.Ok(new
    {
        isConnected = state.IsConnected,
        publicUrl = state.PublicUrl,
        connectedAt = state.ConnectedAt,
    });
});

// Chaos rules

agentApi.MapGet("/rules/chaos", (string clientId, AgentStateStore store) =>
    Results.Ok(store.GetOrCreate(clientId).ChaosRules.Values.OrderBy(r => r.Name)));

agentApi.MapPost("/rules/chaos", async (string clientId, ChaosRule rule, AgentStateStore store) =>
{
    rule = rule with { Id = Guid.NewGuid() };
    store.GetOrCreate(clientId).ChaosRules[rule.Id] = rule;
    await SyncRulesToAgentAsync(clientId);
    return Results.Created($"/api/agents/{clientId}/rules/chaos/{rule.Id}", rule);
});

agentApi.MapPut("/rules/chaos/{id:guid}", async (string clientId, Guid id, ChaosRule rule, AgentStateStore store) =>
{
    var state = store.GetOrCreate(clientId);
    if (!state.ChaosRules.ContainsKey(id)) return Results.NotFound();
    rule = rule with { Id = id };
    state.ChaosRules[id] = rule;
    await SyncRulesToAgentAsync(clientId);
    return Results.Ok(rule);
});

agentApi.MapDelete("/rules/chaos/{id:guid}", async (string clientId, Guid id, AgentStateStore store) =>
{
    if (!store.GetOrCreate(clientId).ChaosRules.TryRemove(id, out _)) return Results.NotFound();
    await SyncRulesToAgentAsync(clientId);
    return Results.NoContent();
});

agentApi.MapPatch("/rules/chaos/{id:guid}/toggle", async (string clientId, Guid id, AgentStateStore store) =>
{
    var rules = store.GetOrCreate(clientId).ChaosRules;
    if (!rules.TryGetValue(id, out var existing)) return Results.NotFound();
    var updated = existing with { IsEnabled = !existing.IsEnabled };
    rules[id] = updated;
    await SyncRulesToAgentAsync(clientId);
    return Results.Ok(updated);
});

// Mock rules

agentApi.MapGet("/rules/mocks", (string clientId, AgentStateStore store) =>
    Results.Ok(store.GetOrCreate(clientId).MockRules.Values.OrderBy(r => r.Name)));

agentApi.MapPost("/rules/mocks", async (string clientId, MockRule rule, AgentStateStore store) =>
{
    rule = rule with { Id = Guid.NewGuid() };
    store.GetOrCreate(clientId).MockRules[rule.Id] = rule;
    await SyncRulesToAgentAsync(clientId);
    return Results.Created($"/api/agents/{clientId}/rules/mocks/{rule.Id}", rule);
});

agentApi.MapPut("/rules/mocks/{id:guid}", async (string clientId, Guid id, MockRule rule, AgentStateStore store) =>
{
    var state = store.GetOrCreate(clientId);
    if (!state.MockRules.ContainsKey(id)) return Results.NotFound();
    rule = rule with { Id = id };
    state.MockRules[id] = rule;
    await SyncRulesToAgentAsync(clientId);
    return Results.Ok(rule);
});

agentApi.MapDelete("/rules/mocks/{id:guid}", async (string clientId, Guid id, AgentStateStore store) =>
{
    if (!store.GetOrCreate(clientId).MockRules.TryRemove(id, out _)) return Results.NotFound();
    await SyncRulesToAgentAsync(clientId);
    return Results.NoContent();
});

// Routing rules

agentApi.MapGet("/rules/routing", (string clientId, AgentStateStore store) =>
    Results.Ok(store.GetOrCreate(clientId).RoutingRules.Values.OrderBy(r => r.Priority)));

agentApi.MapPost("/rules/routing", async (string clientId, RoutingRule rule, AgentStateStore store) =>
{
    rule = rule with { Id = Guid.NewGuid() };
    store.GetOrCreate(clientId).RoutingRules[rule.Id] = rule;
    await SyncRulesToAgentAsync(clientId);
    return Results.Created($"/api/agents/{clientId}/rules/routing/{rule.Id}", rule);
});

agentApi.MapPut("/rules/routing/{id:guid}", async (string clientId, Guid id, RoutingRule rule, AgentStateStore store) =>
{
    var state = store.GetOrCreate(clientId);
    if (!state.RoutingRules.ContainsKey(id)) return Results.NotFound();
    rule = rule with { Id = id };
    state.RoutingRules[id] = rule;
    await SyncRulesToAgentAsync(clientId);
    return Results.Ok(rule);
});

agentApi.MapDelete("/rules/routing/{id:guid}", async (string clientId, Guid id, AgentStateStore store) =>
{
    if (!store.GetOrCreate(clientId).RoutingRules.TryRemove(id, out _)) return Results.NotFound();
    await SyncRulesToAgentAsync(clientId);
    return Results.NoContent();
});

// Request log

agentApi.MapGet("/log", (string clientId, AgentStateStore store) =>
    Results.Ok(store.GetOrCreate(clientId).GetLog()));

agentApi.MapDelete("/log", (string clientId, AgentStateStore store) =>
{
    store.GetOrCreate(clientId).ClearLog();
    return Results.NoContent();
});

// SSE event stream 

agentApi.MapGet("/events", async (string clientId, HttpContext ctx, SseConnectionManager sseMgr, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    await ctx.Response.Body.FlushAsync(ct);

    var (channel, sub) = sseMgr.Subscribe(clientId);
    using (sub)
    {
        try
        {
            await foreach (var json in channel.Reader.ReadAllAsync(ct))
            {
                await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal exit.
        }
    }
});


app.Map("{*path}", async (HttpContext context, string? path) =>
{
    string host = context.Request.Host.Host;
    string clientId = host.Split('.')[0];

    if (char.IsDigit(clientId[0]) || clientId == "localhost")
        return Results.Ok($"Entropy Tunnel v2.0. Usage: http://<client-id>.{context.Request.Host.Value}/");

    if (!_connections.TryGetValue(clientId, out var conn) || conn.Socket.State != WebSocketState.Open)
        return Results.Content($"Tunnel '{clientId}' is offline.", "text/plain", Encoding.UTF8, 404);

    var requestId = Guid.NewGuid();
    var tcs = new TaskCompletionSource<(string, int, ChannelReader<byte[]>, Dictionary<string, string[]>)>();
    _pendingRequests.TryAdd(requestId, tcs);

    string fullPath = $"/{path ?? ""}{context.Request.QueryString}";

    var requestHeaders = new Dictionary<string, string>();
    foreach (var header in context.Request.Headers)
    {
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
        if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
        requestHeaders[header.Key] = string.Join(", ", header.Value.ToArray());
    }

    bool hasBody = context.Request.ContentLength > 0
                || context.Request.Headers.ContainsKey("Transfer-Encoding");

    var requestMeta = new
    {
        Method = context.Request.Method,
        Path = fullPath,
        Headers = requestHeaders,
        HasBody = hasBody,
    };

    string metaJson = JsonSerializer.Serialize(requestMeta);
    byte[] metaBytes = Encoding.UTF8.GetBytes(metaJson);
    var headerPacket = new byte[16 + 1 + 4 + metaBytes.Length];
    Array.Copy(requestId.ToByteArray(), 0, headerPacket, 0, 16);
    headerPacket[16] = 0x10; // Request Header
    Array.Copy(BitConverter.GetBytes(metaBytes.Length), 0, headerPacket, 17, 4);
    Array.Copy(metaBytes, 0, headerPacket, 21, metaBytes.Length);
    await SendToAgentAsync(clientId, headerPacket);

    // Stream request body
    if (hasBody && context.Request.Body.CanRead)
    {
        const int chunkSize = 16 * 1024;
        var buffer = new byte[chunkSize];
        int bytesRead;
        while ((bytesRead = await context.Request.Body.ReadAsync(buffer)) > 0)
        {
            var bodyChunk = new byte[16 + 1 + bytesRead];
            Array.Copy(requestId.ToByteArray(), 0, bodyChunk, 0, 16);
            bodyChunk[16] = 0x11; // Request Body Chunk
            Array.Copy(buffer, 0, bodyChunk, 17, bytesRead);
            await SendToAgentAsync(clientId, bodyChunk);
        }
    }

    // Send EOF
    var eofPacket = new byte[17];
    Array.Copy(requestId.ToByteArray(), 0, eofPacket, 0, 16);
    eofPacket[16] = 0x12; // Request EOF
    await SendToAgentAsync(clientId, eofPacket);

    // Wait for agent response (30 s timeout)
    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(30_000));
    if (completedTask != tcs.Task)
    {
        _pendingRequests.TryRemove(requestId, out _);
        _activeChannels.TryRemove(requestId, out _);
        return Results.StatusCode(504);
    }

    var (contentType, statusCode, bodyReader, headers) = await tcs.Task;
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = contentType;

    foreach (var header in headers)
    {
        if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
        if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
        context.Response.Headers[header.Key] = new StringValues(header.Value);
    }

    await foreach (var chunk in bodyReader.ReadAllAsync())
    {
        await context.Response.Body.WriteAsync(chunk);
        await context.Response.Body.FlushAsync();
    }

    return Results.Empty;
});

app.Run();


/// <summary>Bundles the WebSocket and its send-serialization lock for one agent connection.</summary>
record AgentConnection(WebSocket Socket, SemaphoreSlim Lock);

record LoginRequest(string Password);
