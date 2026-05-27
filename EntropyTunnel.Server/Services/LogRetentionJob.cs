using EntropyTunnel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace EntropyTunnel.Server.Services;

/// <summary>
/// Deletes request_log entries older than LogRetentionDays (default 7).
/// Runs once at startup, then every 24 hours.
/// </summary>
public sealed class LogRetentionJob(IDbContextFactory<AppDbContext> dbFactory, IConfiguration config)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = config.GetValue("LogRetentionDays", 7);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var deleted = await db.RequestLog
                    .Where(l => l.Timestamp < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                    Console.WriteLine($"[Retention] Deleted {deleted} log entries older than {retentionDays}d.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Retention] Cleanup failed: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
