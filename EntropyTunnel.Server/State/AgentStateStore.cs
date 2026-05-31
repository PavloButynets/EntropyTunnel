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
    private const string TypeChaos = "chaos";
    private const string TypeMock = "mock";
    private const string TypeRouting = "routing";

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

    // Rules sync

    public async Task<SyncRulesPayload> GetSyncPayloadAsync(string clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Rules.Where(r => r.ClientId == clientId).ToListAsync();

        return new SyncRulesPayload
        {
            ChaosRules = [.. rows.Where(r => r.Type == TypeChaos)
                .Select(r => JsonSerializer.Deserialize<ChaosRule>(r.Data)!)
                .OrderBy(r => r.Name)],
            MockRules = [.. rows.Where(r => r.Type == TypeMock)
                .Select(r => JsonSerializer.Deserialize<MockRule>(r.Data)!)
                .OrderBy(r => r.Name)],
            RoutingRules = [.. rows.Where(r => r.Type == TypeRouting)
                .Select(r => JsonSerializer.Deserialize<RoutingRule>(r.Data)!)
                .OrderBy(r => r.Priority)],
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

    // Rule persistence

    public Task SaveChaosRuleAsync(string clientId, ChaosRule rule) =>
        SaveRuleAsync(clientId, TypeChaos, rule.Id, JsonSerializer.Serialize(rule));

    public Task DeleteChaosRuleAsync(Guid id) => DeleteRuleAsync(id);

    public Task SaveMockRuleAsync(string clientId, MockRule rule) =>
        SaveRuleAsync(clientId, TypeMock, rule.Id, JsonSerializer.Serialize(rule));

    public Task DeleteMockRuleAsync(Guid id) => DeleteRuleAsync(id);

    public Task SaveRoutingRuleAsync(string clientId, RoutingRule rule) =>
        SaveRuleAsync(clientId, TypeRouting, rule.Id, JsonSerializer.Serialize(rule));

    public Task DeleteRoutingRuleAsync(Guid id) => DeleteRuleAsync(id);

    private async Task SaveRuleAsync(string clientId, string type, Guid id, string data)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Rules.FindAsync(id);
        if (existing is null)
            db.Rules.Add(new RuleRow { Id = id, ClientId = clientId, Type = type, Data = data });
        else
            existing.Data = data;
        await db.SaveChangesAsync();
    }

    private async Task DeleteRuleAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Rules.Where(r => r.Id == id).ExecuteDeleteAsync();
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
