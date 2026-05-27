using Microsoft.EntityFrameworkCore;

namespace EntropyTunnel.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AccountRow> Accounts => Set<AccountRow>();
    public DbSet<AgentRow> Agents => Set<AgentRow>();
    public DbSet<ChaosRuleRow> ChaosRules => Set<ChaosRuleRow>();
    public DbSet<MockRuleRow> MockRules => Set<MockRuleRow>();
    public DbSet<RoutingRuleRow> RoutingRules => Set<RoutingRuleRow>();
    public DbSet<LogRow> RequestLog => Set<LogRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AccountRow>(e => e.ToTable("accounts").HasKey(x => x.AccountId));

        model.Entity<AgentRow>(e =>
        {
            e.ToTable("agents").HasKey(x => x.ClientId);
            e.HasOne<AccountRow>().WithMany()
             .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ChaosRuleRow>(e =>
        {
            e.ToTable("chaos_rules");
            e.HasOne<AgentRow>().WithMany()
             .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<MockRuleRow>(e =>
        {
            e.ToTable("mock_rules");
            e.HasOne<AgentRow>().WithMany()
             .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<RoutingRuleRow>(e =>
        {
            e.ToTable("routing_rules");
            e.HasOne<AgentRow>().WithMany()
             .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<LogRow>(e =>
        {
            e.ToTable("request_log").HasKey(x => x.RequestId);
            e.HasIndex(x => new { x.ClientId, x.Timestamp });
            e.HasOne<AgentRow>().WithMany()
             .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
