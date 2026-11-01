using System.Collections.Concurrent;
using Namerre.Application.Renaming.Rollback;

namespace Namerre.Infrastructure.Renaming;

/// <summary>In-flight rollback checks keyed by run and operation, so a polling client can show per-file progress.</summary>
internal sealed class RollbackCheckTracker
{
    private readonly ConcurrentDictionary<string, RollbackCheckProgress> checks = new();

    /// <summary>
    /// Starts tracking a check. Only the first in-flight check for an operation reports progress, so a repeated
    /// request cannot reset or remove it; the others get a tracking with no progress callback.
    /// </summary>
    public Tracking Start(string runId, string operationId, int total)
    {
        var key = Key(runId, operationId);
        return checks.TryAdd(key, new(0, total))
            ? new(completed => checks[key] = new(completed, total), () => checks.TryRemove(key, out _))
            : new(null, null);
    }

    public RollbackCheckProgress Get(string runId, string operationId)
        => checks.TryGetValue(Key(runId, operationId), out var progress) ? progress : new RollbackCheckProgress(0, 0);

    private static string Key(string runId, string operationId) => runId + ":" + operationId;

    public sealed class Tracking(Action<int>? progress, Action? end) : IDisposable
    {
        public Action<int>? Progress { get; } = progress;
        public void Dispose() => end?.Invoke();
    }
}
