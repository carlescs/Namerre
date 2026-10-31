using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Namerre.Infrastructure.WatchFolders;

/// <summary>Runs <see cref="WatchFolderService.Scan"/> every few seconds; each folder keeps its own interval.</summary>
public sealed class WatchFolderWorker(WatchFolderService service, ILogger<WatchFolderWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await service.Scan(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Watch folder scan failed"); }
            try { await Task.Delay(Tick, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
