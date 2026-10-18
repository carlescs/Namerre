using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Namerre.Infrastructure.Renaming;

public sealed class RenameWorker(RenameService service, ILogger<RenameWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var unavailable = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await service.ProcessNext(stoppingToken);
                unavailable = false;
                if (!processed) await Task.Delay(250, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                if (!unavailable)
                    logger.LogWarning(exception, "Rename worker unavailable. Browsing and local previews remain independent of durable storage.");
                unavailable = true;
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
