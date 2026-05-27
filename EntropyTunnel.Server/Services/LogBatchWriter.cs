using EntropyTunnel.Server.Data;
using EntropyTunnel.Server.State;
using Microsoft.EntityFrameworkCore;

namespace EntropyTunnel.Server.Services;

/// <summary>
/// Drains the log channel from <see cref="AgentStateStore"/> and inserts entries to
/// PostgreSQL in batches.  Flushes when the batch reaches 100 entries or after 200 ms,
/// whichever comes first - keeping DB round-trips low even under burst traffic.
/// </summary>
public sealed class LogBatchWriter(AgentStateStore store, IDbContextFactory<AppDbContext> dbFactory)
    : BackgroundService
{
    private const int MaxBatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = store.LogChannel.Reader;
        var batch = new List<LogRow>(MaxBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            batch.Clear();

            // Wait up to FlushInterval for the first item
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(FlushInterval);

            try
            {
                await reader.WaitToReadAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout elapsed - nothing to flush, loop again
                continue;
            }

            // Drain up to MaxBatchSize items without blocking
            while (batch.Count < MaxBatchSize && reader.TryRead(out var row))
                batch.Add(row);

            if (batch.Count == 0) continue;

            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                db.RequestLog.AddRange(batch);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DB] log batch flush failed ({batch.Count} entries): {ex.Message}");
            }
        }
    }
}
