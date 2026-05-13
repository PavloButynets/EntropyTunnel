using System.Security.Claims;
using EntropyTunnel.Core.Models;
using EntropyTunnel.Server.Sse;
using EntropyTunnel.Server.State;
using Microsoft.AspNetCore.Authentication;

namespace EntropyTunnel.Server.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/ping", () => new { app = "EntropyTunnel.Server", version = "2.0" });

        api.MapPost("/auth/login", async (LoginRequest body, HttpContext ctx, TunnelHub hub) =>
        {
            var match = hub.AccountPasswords.FirstOrDefault(kvp => kvp.Value == body.Password);
            if (match.Key is null) return Results.StatusCode(401);

            var claims = new[] { new Claim(ClaimTypes.Name, match.Key) };
            var identity = new ClaimsIdentity(claims, "Cookies");
            await ctx.SignInAsync("Cookies", new ClaimsPrincipal(identity));
            return Results.Ok(new { accountId = match.Key });
        });

        api.MapGet("/auth/me", (HttpContext ctx) =>
        {
            var accountId = ctx.User.Identity?.Name;
            if (string.IsNullOrEmpty(accountId)) return Results.StatusCode(401);
            return Results.Ok(new { accountId });
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

        var agentApi = api.MapGroup("/agents/{clientId}");

        agentApi.AddEndpointFilter(async (ctx, next) =>
        {
            var authenticatedAccountId = ctx.HttpContext.User.Identity?.Name;
            if (string.IsNullOrEmpty(authenticatedAccountId))
                return Results.StatusCode(401);

            var routeClientId = ctx.HttpContext.GetRouteValue("clientId") as string ?? "";
            var agentState = ctx.HttpContext.RequestServices.GetRequiredService<AgentStateStore>().Get(routeClientId);
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

        MapChaosRules(agentApi);
        MapMockRules(agentApi);
        MapRoutingRules(agentApi);
        MapLog(agentApi);
        MapEvents(agentApi);
    }

    private static void MapChaosRules(RouteGroupBuilder agentApi)
    {
        agentApi.MapGet("/rules/chaos", (string clientId, AgentStateStore store) =>
            Results.Ok(store.GetOrCreate(clientId).ChaosRules.Values.OrderBy(r => r.Name)));

        agentApi.MapPost("/rules/chaos", async (string clientId, ChaosRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            rule = rule with { Id = Guid.NewGuid() };
            store.GetOrCreate(clientId).ChaosRules[rule.Id] = rule;
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Created($"/api/agents/{clientId}/rules/chaos/{rule.Id}", rule);
        });

        agentApi.MapPut("/rules/chaos/{id:guid}", async (string clientId, Guid id, ChaosRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            var state = store.GetOrCreate(clientId);
            if (!state.ChaosRules.ContainsKey(id)) return Results.NotFound();
            state.ChaosRules[id] = rule with { Id = id };
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Ok(rule);
        });

        agentApi.MapDelete("/rules/chaos/{id:guid}", async (string clientId, Guid id, AgentStateStore store, TunnelHub hub) =>
        {
            if (!store.GetOrCreate(clientId).ChaosRules.TryRemove(id, out _)) return Results.NotFound();
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.NoContent();
        });

        agentApi.MapPatch("/rules/chaos/{id:guid}/toggle", async (string clientId, Guid id, AgentStateStore store, TunnelHub hub) =>
        {
            var rules = store.GetOrCreate(clientId).ChaosRules;
            if (!rules.TryGetValue(id, out var existing)) return Results.NotFound();
            var updated = existing with { IsEnabled = !existing.IsEnabled };
            rules[id] = updated;
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Ok(updated);
        });
    }

    private static void MapMockRules(RouteGroupBuilder agentApi)
    {
        agentApi.MapGet("/rules/mocks", (string clientId, AgentStateStore store) =>
            Results.Ok(store.GetOrCreate(clientId).MockRules.Values.OrderBy(r => r.Name)));

        agentApi.MapPost("/rules/mocks", async (string clientId, MockRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            rule = rule with { Id = Guid.NewGuid() };
            store.GetOrCreate(clientId).MockRules[rule.Id] = rule;
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Created($"/api/agents/{clientId}/rules/mocks/{rule.Id}", rule);
        });

        agentApi.MapPut("/rules/mocks/{id:guid}", async (string clientId, Guid id, MockRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            var state = store.GetOrCreate(clientId);
            if (!state.MockRules.ContainsKey(id)) return Results.NotFound();
            state.MockRules[id] = rule with { Id = id };
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Ok(rule);
        });

        agentApi.MapDelete("/rules/mocks/{id:guid}", async (string clientId, Guid id, AgentStateStore store, TunnelHub hub) =>
        {
            if (!store.GetOrCreate(clientId).MockRules.TryRemove(id, out _)) return Results.NotFound();
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.NoContent();
        });
    }

    private static void MapRoutingRules(RouteGroupBuilder agentApi)
    {
        agentApi.MapGet("/rules/routing", (string clientId, AgentStateStore store) =>
            Results.Ok(store.GetOrCreate(clientId).RoutingRules.Values.OrderBy(r => r.Priority)));

        agentApi.MapPost("/rules/routing", async (string clientId, RoutingRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            rule = rule with { Id = Guid.NewGuid() };
            store.GetOrCreate(clientId).RoutingRules[rule.Id] = rule;
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Created($"/api/agents/{clientId}/rules/routing/{rule.Id}", rule);
        });

        agentApi.MapPut("/rules/routing/{id:guid}", async (string clientId, Guid id, RoutingRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            var state = store.GetOrCreate(clientId);
            if (!state.RoutingRules.ContainsKey(id)) return Results.NotFound();
            state.RoutingRules[id] = rule with { Id = id };
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Ok(rule);
        });

        agentApi.MapDelete("/rules/routing/{id:guid}", async (string clientId, Guid id, AgentStateStore store, TunnelHub hub) =>
        {
            if (!store.GetOrCreate(clientId).RoutingRules.TryRemove(id, out _)) return Results.NotFound();
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.NoContent();
        });
    }

    private static void MapLog(RouteGroupBuilder agentApi)
    {
        agentApi.MapGet("/log", (string clientId, AgentStateStore store) =>
            Results.Ok(store.GetOrCreate(clientId).GetLog()));

        agentApi.MapDelete("/log", (string clientId, AgentStateStore store) =>
        {
            store.GetOrCreate(clientId).ClearLog();
            return Results.NoContent();
        });
    }

    private static void MapEvents(RouteGroupBuilder agentApi)
    {
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
                catch (OperationCanceledException) { }
            }
        });
    }
}

public record LoginRequest(string Password);
