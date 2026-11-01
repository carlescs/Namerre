using Namerre.Application.Renaming;
using System.Collections.Concurrent;
using System.Diagnostics;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Infrastructure.Renaming;

/// <summary>
/// State shared by the command surface and the job runner: the gate that serializes durable state changes, the
/// "durable storage failed" stop flag, and the trace context of the request that queued each job.
/// </summary>
internal sealed class RenameCoordinator(RenameStore store)
{
    // In memory only and best effort: jobs resumed after a restart start unlinked traces.
    private readonly ConcurrentDictionary<string, ActivityContext> jobOrigins = new();
    private volatile bool persistenceUnavailable;

    /// <summary>Serializes every durable state change (prepare, execute, rollback, recovery, deletion): one at a time by design.</summary>
    public SemaphoreSlim Gate { get; } = new(1);

    public void EnsureOperational()
    {
        if (persistenceUnavailable)
            throw new RenameException("StorageUnavailable", "Journaling failed. Restart after restoring durable storage; retained jobs require inspection.", 503);
    }

    /// <summary>Stops every further mutation until restart, because the journal can no longer be trusted.</summary>
    public void MarkPersistenceUnavailable() => persistenceUnavailable = true;

    public void EnsureNoRecovery(string? except = null)
    {
        if (store.List<RunView>("Run", ["RecoveryRequired", "RecoveryQueued", "Recovering"]).Any(r => r.Id != except))
            throw Rejected(new RenameException("RecoveryPending", "Resolve interrupted mutations before starting another mutation.", 409));
    }

    public void Enqueue<T>(string id, string kind, string status, T value, string jobKind, string? operationKey = null)
    {
        try { store.Enqueue(id, kind, status, value, jobKind, operationKey); }
        catch (RenameException exception) when (exception.Code == "QueueFull") { throw Rejected(exception); }
        RememberOrigin(id);
    }

    public void RememberOrigin(string id)
    {
        if (Activity.Current is { } current) jobOrigins[id] = current.Context;
    }

    /// <summary>The links for a job's span: the request that queued it, when this process remembers it.</summary>
    public ActivityLink[]? TakeOrigin(string id) => jobOrigins.TryRemove(id, out var origin) ? [new(origin)] : null;

    public static RenameException Rejected(RenameException exception)
    {
        NamerreTelemetry.Rejections.Add(1, new KeyValuePair<string, object?>("namerre.rejection.reason", exception.Code));
        return exception;
    }
}
