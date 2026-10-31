using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Namerre.Infrastructure.Renaming;

namespace Namerre.Infrastructure.Diagnostics;

/// <summary>
/// Gauges read from the durable store when metrics are collected: jobs waiting in the queue and runs awaiting
/// recovery (an interrupted mutation blocks every further mutation, so it is worth alerting on). Created only when
/// telemetry export is enabled; a store that is not configured or unavailable reports nothing.
/// </summary>
public sealed class RenameStateMetrics : IDisposable
{
    private readonly Meter meter;
    private readonly RenameStore store;
    private readonly ILogger<RenameStateMetrics> logger;
    private bool warned;

    public RenameStateMetrics(IMeterFactory meters, RenameStore store, ILogger<RenameStateMetrics> logger)
    {
        this.store = store;
        this.logger = logger;
        meter = meters.Create(NamerreTelemetry.Name);
        meter.CreateObservableGauge("namerre.rename.queue.jobs", () => Read(backlog => backlog.QueuedJobs),
            "{job}", "Durable rename jobs waiting to run.");
        meter.CreateObservableGauge("namerre.rename.runs.recovery", () => Read(backlog => backlog.RunsAwaitingRecovery),
            "{run}", "Runs that were interrupted and need recovery, or are recovering.");
    }

    private IEnumerable<Measurement<int>> Read(Func<RenameStore.Backlog, int> value)
    {
        RenameStore.Backlog backlog;
        try
        {
            if (!store.IsConfigured) return [];
            backlog = store.ReadBacklog();
            warned = false;
        }
        catch (Exception exception)
        {
            if (!warned) logger.LogWarning(exception, "Cannot read the rename backlog for metrics");
            warned = true;
            return [];
        }
        return [new(value(backlog))];
    }

    public void Dispose() => meter.Dispose();
}
