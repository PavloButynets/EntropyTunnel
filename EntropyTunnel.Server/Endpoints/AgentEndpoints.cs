using System.Security.Claims;
using System.Text.Json;
using EntropyTunnel.Core.Models;
using EntropyTunnel.Server.Data;
using EntropyTunnel.Server.Security;
using EntropyTunnel.Server.Sse;
using EntropyTunnel.Server.State;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace EntropyTunnel.Server.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/ping", () => new { app = "EntropyTunnel.Server", version = "2.0" });

        api.MapPost("/auth/login", async (LoginRequest body, HttpContext ctx, AppDbContext db) =>
        {
            AccountRow? account = null;

            if (!string.IsNullOrEmpty(body.ClientId))
            {
                var agent = await db.Agents.FirstOrDefaultAsync(a => a.ClientId == body.ClientId);
                if (agent is not null)
                    account = await db.Accounts.FirstOrDefaultAsync(a => a.AccountId == agent.AccountId);
            }
            else
            {
                var all = await db.Accounts.ToListAsync();
                account = all.FirstOrDefault(a => PasswordHasher.Verify(body.Password, a.Password));
            }

            if (account is null || !PasswordHasher.Verify(body.Password, account.Password))
                return Results.StatusCode(401);

            var claims = new[] { new Claim(ClaimTypes.Name, account.AccountId) };
            var identity = new ClaimsIdentity(claims, "Cookies");
            await ctx.SignInAsync("Cookies", new ClaimsPrincipal(identity));
            return Results.Ok(new { accountId = account.AccountId });
        });

        api.MapGet("/auth/me", (HttpContext ctx) =>
        {
            var accountId = ctx.User.Identity?.Name;
            if (string.IsNullOrEmpty(accountId)) return Results.StatusCode(401);
            return Results.Ok(new { accountId });
        });

        api.MapGet("/agents", async (HttpContext ctx, AgentStateStore store, AppDbContext db) =>
        {
            var accountId = ctx.User.Identity?.Name;
            if (string.IsNullOrEmpty(accountId)) return Results.StatusCode(401);

            var dbAgents = await db.Agents.Where(a => a.AccountId == accountId).ToListAsync();
            var list = dbAgents.Select(a =>
            {
                var state = store.Get(a.ClientId);
                return new
                {
                    clientId = a.ClientId,
                    isConnected = state?.IsConnected ?? false,
                    publicUrl = state?.PublicUrl ?? "",
                    connectedAt = state?.ConnectedAt,
                };
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
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.ClientId == routeClientId);
            if (agent is null || !agent.AccountId.Equals(authenticatedAccountId, StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(403);

            return await next(ctx);
        });

        agentApi.MapGet("/status", (string clientId, AgentStateStore store) =>
        {
            var state = store.Get(clientId);
            return Results.Ok(new
            {
                isConnected = state?.IsConnected ?? false,
                publicUrl = state?.PublicUrl ?? "",
                connectedAt = state?.ConnectedAt,
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
        agentApi.MapGet("/rules/chaos", async (string clientId, AppDbContext db) =>
        {
            var rows = await db.ChaosRules.Where(r => r.ClientId == clientId).ToListAsync();
            return Results.Ok(rows.Select(r => JsonSerializer.Deserialize<ChaosRule>(r.Data)!).OrderBy(r => r.Name));
        });

        agentApi.MapPost("/rules/chaos", async (string clientId, ChaosRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            rule = rule with { Id = Guid.NewGuid() };
            await store.SaveChaosRuleAsync(clientId, rule);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Created($"/api/agents/{clientId}/rules/chaos/{rule.Id}", rule);
        });

        agentApi.MapPut("/rules/chaos/{id:guid}", async (string clientId, Guid id, ChaosRule rule, AppDbContext db, AgentStateStore store, TunnelHub hub) =>
        {
            if (!await db.ChaosRules.AnyAsync(r => r.Id == id)) return Results.NotFound();
            rule = rule with { Id = id };
            await store.SaveChaosRuleAsync(clientId, rule);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Ok(rule);
        });

        agentApi.MapDelete("/rules/chaos/{id:guid}", async (string clientId, Guid id, AppDbContext db, AgentStateStore store, TunnelHub hub) =>
        {
            if (!await db.ChaosRules.AnyAsync(r => r.Id == id)) return Results.NotFound();
            await store.DeleteChaosRuleAsync(id);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.NoContent();
        });

        agentApi.MapPatch("/rules/chaos/{id:guid}/toggle", async (string clientId, Guid id, AppDbContext db, AgentStateStore store, TunnelHub hub) =>
        {
            var row = await db.ChaosRules.FirstOrDefaultAsync(r => r.Id == id);
            if (row is null) return Results.NotFound();
            var rule = JsonSerializer.Deserialize<ChaosRule>(row.Data)!;
            var updated = rule with { IsEnabled = !rule.IsEnabled };
            await store.SaveChaosRuleAsync(clientId, updated);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Ok(updated);
        });
    }

    private static void MapMockRules(RouteGroupBuilder agentApi)
    {
        agentApi.MapGet("/rules/mocks", async (string clientId, AppDbContext db) =>
        {
            var rows = await db.MockRules.Where(r => r.ClientId == clientId).ToListAsync();
            return Results.Ok(rows.Select(r => JsonSerializer.Deserialize<MockRule>(r.Data)!).OrderBy(r => r.Name));
        });

        agentApi.MapPost("/rules/mocks", async (string clientId, MockRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            rule = rule with { Id = Guid.NewGuid() };
            await store.SaveMockRuleAsync(clientId, rule);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Created($"/api/agents/{clientId}/rules/mocks/{rule.Id}", rule);
        });

        agentApi.MapPut("/rules/mocks/{id:guid}", async (string clientId, Guid id, MockRule rule, AppDbContext db, AgentStateStore store, TunnelHub hub) =>
        {
            if (!await db.MockRules.AnyAsync(r => r.Id == id)) return Results.NotFound();
            rule = rule with { Id = id };
            await store.SaveMockRuleAsync(clientId, rule);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Ok(rule);
        });

        agentApi.MapDelete("/rules/mocks/{id:guid}", async (string clientId, Guid id, AppDbContext db, AgentStateStore store, TunnelHub hub) =>
        {
            if (!await db.MockRules.AnyAsync(r => r.Id == id)) return Results.NotFound();
            await store.DeleteMockRuleAsync(id);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.NoContent();
        });
    }

    private static void MapRoutingRules(RouteGroupBuilder agentApi)
    {
        agentApi.MapGet("/rules/routing", async (string clientId, AppDbContext db) =>
        {
            var rows = await db.RoutingRules.Where(r => r.ClientId == clientId).ToListAsync();
            return Results.Ok(rows.Select(r => JsonSerializer.Deserialize<RoutingRule>(r.Data)!).OrderBy(r => r.Priority));
        });

        agentApi.MapPost("/rules/routing", async (string clientId, RoutingRule rule, AgentStateStore store, TunnelHub hub) =>
        {
            rule = rule with { Id = Guid.NewGuid() };
            await store.SaveRoutingRuleAsync(clientId, rule);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Created($"/api/agents/{clientId}/rules/routing/{rule.Id}", rule);
        });

        agentApi.MapPut("/rules/routing/{id:guid}", async (string clientId, Guid id, RoutingRule rule, AppDbContext db, AgentStateStore store, TunnelHub hub) =>
        {
            if (!await db.RoutingRules.AnyAsync(r => r.Id == id)) return Results.NotFound();
            rule = rule with { Id = id };
            await store.SaveRoutingRuleAsync(clientId, rule);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.Ok(rule);
        });

        agentApi.MapDelete("/rules/routing/{id:guid}", async (string clientId, Guid id, AppDbContext db, AgentStateStore store, TunnelHub hub) =>
        {
            if (!await db.RoutingRules.AnyAsync(r => r.Id == id)) return Results.NotFound();
            await store.DeleteRoutingRuleAsync(id);
            await hub.SyncRulesToAgentAsync(clientId);
            return Results.NoContent();
        });
    }

    private static void MapLog(RouteGroupBuilder agentApi)
    {
        agentApi.MapGet("/log", async (string clientId, AppDbContext db) =>
        {
            var rows = await db.RequestLog
                .Where(l => l.ClientId == clientId)
                .OrderByDescending(l => l.Timestamp)
                .Take(1000)
                .ToListAsync();
            return Results.Ok(rows.Select(r => JsonSerializer.Deserialize<RequestLogEntry>(r.Data)!));
        });

        agentApi.MapDelete("/log", async (string clientId, AgentStateStore store) =>
        {
            await store.ClearLogAsync(clientId);
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

public record LoginRequest(string? ClientId, string Password);
