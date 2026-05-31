using Microsoft.EntityFrameworkCore;

namespace EntropyTunnel.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AccountRow> Accounts => Set<AccountRow>();
    public DbSet<AgentRow> Agents => Set<AgentRow>();
    public DbSet<RuleRow> Rules => Set<RuleRow>();
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

        model.Entity<RuleRow>(e =>
        {
            e.ToTable("rules").HasKey(x => x.Id);
            e.HasIndex(x => new { x.ClientId, x.Type });
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
