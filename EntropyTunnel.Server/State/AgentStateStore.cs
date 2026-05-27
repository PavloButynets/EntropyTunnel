using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using EntropyTunnel.Core.Models;
using EntropyTunnel.Core.Payloads;
using EntropyTunnel.Server.Data;
using EntropyTunnel.Server.Security;
using Microsoft.EntityFrameworkCore;

namespace EntropyTunnel.Server.State;

public sealed class AgentStateStore(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly ConcurrentDictionary<string, AgentState> _agents =
        new(StringComparer.OrdinalIgnoreCase);

    internal readonly Channel<LogRow> LogChannel = Channel.CreateUnbounded<LogRow>(
        new UnboundedChannelOptions { SingleReader = true });

    // In-memory connection state

    public AgentState GetOrCreate(string clientId) =>
        _agents.GetOrAdd(clientId, _ => new AgentState());

    public AgentState? Get(string clientId) =>
        _agents.TryGetValue(clientId, out var s) ? s : null;

    public IEnumerable<(string ClientId, AgentState State)> GetAll() =>
        _agents.OrderBy(kvp => kvp.Key).Select(kvp => (kvp.Key, kvp.Value));

    // Rules for agent sync

    public async Task<SyncRulesPayload> GetSyncPayloadAsync(string clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var chaosRows = await db.ChaosRules.Where(r => r.ClientId == clientId).ToListAsync();
        var mockRows = await db.MockRules.Where(r => r.ClientId == clientId).ToListAsync();
        var routingRows = await db.RoutingRules.Where(r => r.ClientId == clientId).ToListAsync();

        return new SyncRulesPayload
        {
            ChaosRules = [.. chaosRows.Select(r => JsonSerializer.Deserialize<ChaosRule>(r.Data)!).OrderBy(r => r.Name)],
            MockRules = [.. mockRows.Select(r => JsonSerializer.Deserialize<MockRule>(r.Data)!).OrderBy(r => r.Name)],
            RoutingRules = [.. routingRows.Select(r => JsonSerializer.Deserialize<RoutingRule>(r.Data)!).OrderBy(r => r.Priority)],
        };
    }

    // Agent & account persistence

    public async Task UpsertAgentAsync(string clientId, string accountId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Agents.FindAsync(clientId);
        if (existing is null)
            db.Agents.Add(new AgentRow { ClientId = clientId, AccountId = accountId });
        else
            existing.AccountId = accountId;
        await db.SaveChangesAsync();
    }

    public async Task UpsertAccountAsync(string accountId, string password)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var hashed = PasswordHasher.Hash(password);
        var existing = await db.Accounts.FindAsync(accountId);
        if (existing is null)
            db.Accounts.Add(new AccountRow { AccountId = accountId, Password = hashed });
        else
            existing.Password = hashed;
        await db.SaveChangesAsync();
    }

    // Chaos rule persistence

    public async Task SaveChaosRuleAsync(string clientId, ChaosRule rule)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.ChaosRules.FindAsync(rule.Id);
        if (existing is null)
            db.ChaosRules.Add(new ChaosRuleRow { Id = rule.Id, ClientId = clientId, Data = JsonSerializer.Serialize(rule) });
        else
            existing.Data = JsonSerializer.Serialize(rule);
        await db.SaveChangesAsync();
    }

    public async Task DeleteChaosRuleAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.ChaosRules.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    // Mock rule persistence

    public async Task SaveMockRuleAsync(string clientId, MockRule rule)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.MockRules.FindAsync(rule.Id);
        if (existing is null)
            db.MockRules.Add(new MockRuleRow { Id = rule.Id, ClientId = clientId, Data = JsonSerializer.Serialize(rule) });
        else
            existing.Data = JsonSerializer.Serialize(rule);
        await db.SaveChangesAsync();
    }

    public async Task DeleteMockRuleAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.MockRules.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    // Routing rule persistence

    public async Task SaveRoutingRuleAsync(string clientId, RoutingRule rule)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.RoutingRules.FindAsync(rule.Id);
        if (existing is null)
            db.RoutingRules.Add(new RoutingRuleRow { Id = rule.Id, ClientId = clientId, Data = JsonSerializer.Serialize(rule) });
        else
            existing.Data = JsonSerializer.Serialize(rule);
        await db.SaveChangesAsync();
    }

    public async Task DeleteRoutingRuleAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.RoutingRules.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    // Log

    public void EnqueueLogEntry(string clientId, RequestLogEntry entry)
    {
        LogChannel.Writer.TryWrite(new LogRow
        {
            RequestId = entry.RequestId,
            ClientId = clientId,
            Timestamp = entry.Timestamp,
            Data = JsonSerializer.Serialize(entry),
        });
    }

    public async Task ClearLogAsync(string clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.RequestLog.Where(l => l.ClientId == clientId).ExecuteDeleteAsync();
    }
}
