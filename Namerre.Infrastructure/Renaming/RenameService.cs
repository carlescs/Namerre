using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Namerre.Application.Authentication;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Previews;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Infrastructure.Renaming;

/// <summary>
/// The public rename commands and queries: validation, history scoping and queueing. Queued jobs run in
/// <see cref="RenameJobRunner"/>; moves go through <see cref="JournaledMover"/>; rollback and recovery checks are
/// planned by <see cref="RollbackPlanner"/> and <see cref="RecoveryPlanner"/>.
/// </summary>
public sealed class RenameService : IRenameService, IUserHistory
{
    /// <summary>Most runs one bulk deletion accepts; also caps the "select all pages" candidate list.</summary>
    public const int MaxRunsPerDeletion = 1000;
    private readonly RenameStore store;
    private readonly PipelineEngine pipeline;
    private readonly ILogger<RenameService> logger;
    private readonly ICurrentUser? currentUser;
    private readonly int maxConcurrentPreviews;
    // Bounds concurrent read-only previews; a preview that finds no free slot is rejected (429), not queued.
    private readonly SemaphoreSlim previews;
    private readonly RenameCoordinator coordinator;
    private readonly RenameRecorder recorder;
    private readonly RollbackPlanner rollbacks;
    private readonly RecoveryPlanner recoveries;
    private readonly RollbackCheckTracker rollbackChecks = new();
    private readonly RenameJobRunner jobs;

    public RenameService(RenameStore store, IRenameFileSystem fileSystem, PipelineEngine pipeline,
        ILogger<RenameService> logger, IRenameNotifier? notifier = null,
        ILibraryNotifier? libraryNotifier = null, ICurrentUser? currentUser = null,
        RenameLimits? limits = null)
    {
        this.store = store;
        this.pipeline = pipeline;
        this.logger = logger;
        this.currentUser = currentUser;
        maxConcurrentPreviews = (limits ?? RenameLimits.Default).MaxConcurrentPreviews;
        previews = new(maxConcurrentPreviews);
        coordinator = new(store);
        recorder = new(store, logger, notifier, libraryNotifier);
        var verifier = new FileVerifier(fileSystem);
        rollbacks = new(store, fileSystem, verifier);
        recoveries = new(fileSystem, verifier);
        jobs = new(store, fileSystem, pipeline, coordinator, recorder, new JournaledMover(fileSystem, recorder, logger),
            verifier, recoveries, logger);
    }

    public async Task<PreviewResult> Preview(PipelineRequest request, CancellationToken cancellationToken)
    {
        if (!await previews.WaitAsync(0, cancellationToken))
            throw RenameCoordinator.Rejected(new RenameException("PreviewBusy", $"{maxConcurrentPreviews} previews are already running. Try again shortly.", 429));
        using var activity = NamerreTelemetry.Source.StartActivity("rename.preview");
        activity?.SetTag("namerre.files.count", request.Files?.Count ?? 0);
        activity?.SetTag("namerre.steps.count", request.Plugins?.Count ?? 0);
        var started = Stopwatch.GetTimestamp();
        var result = "error";
        try
        {
            var preview = await pipeline.Preview(request, cancellationToken);
            result = preview.CanPrepare ? "ready" : "blocked";
            activity?.SetTag("namerre.preview.can_prepare", preview.CanPrepare);
            return preview;
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            result = NamerreTelemetry.ErrorType(exception);
            throw;
        }
        finally
        {
            previews.Release();
            NamerreTelemetry.PreviewDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("namerre.preview.result", result));
        }
    }

    public Task<JobAccepted> Prepare(PipelineRequest request, CancellationToken cancellationToken)
        => Prepare(request, null, cancellationToken);

    /// <param name="origin">Recorded on the plan and its run, e.g. <c>watch:Downloads</c>; null for a person.</param>
    public async Task<JobAccepted> Prepare(PipelineRequest request, string? origin, CancellationToken cancellationToken)
    {
        pipeline.ValidateRequest(request);
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            coordinator.EnsureOperational();
            var id = NewId();
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<PipelineRequest>(
                System.Text.Json.JsonSerializer.Serialize(request, RenameStore.Json), RenameStore.Json)!;
            var plan = new PlanView(id, 1, "Preparing", 0, snapshot, [], [], DateTimeOffset.UtcNow, Origin: origin,
                CreatedBy: origin is null ? currentUser?.Current : null);
            coordinator.Enqueue(id, "Plan", plan.Status, plan, "Prepare");
            recorder.Notify("Plan", id, plan.Status);
            logger.LogInformation("Queued plan {PlanId} for {FileCount} files with {StepCount} steps from {Origin}", id,
                request.Files.Count, request.Plugins.Count, origin ?? plan.CreatedBy?.Username ?? "user");
            return Accepted(id, plan.Status, true);
        }
        finally { coordinator.Gate.Release(); }
    }

    /// <summary>Re-attributes history under the job gate, so a running job cannot write back an older copy.</summary>
    public async Task<int> Reattribute(long fromUserId, UserReference to, CancellationToken cancellationToken)
    {
        if (!store.IsConfigured)
            return 0;
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            var changed = store.ReattributeUser(fromUserId, to);
            logger.LogInformation("Attributed {Count} plans and runs of user {FromUserId} to user {UserId}", changed,
                fromUserId, to.Id);
            return changed;
        }
        finally { coordinator.Gate.Release(); }
    }

    public Task<PlanView> GetPlan(string id, CancellationToken cancellationToken)
        => Task.FromResult(store.Get<PlanView>(id, "Plan", Viewer));

    public async Task<JobAccepted> Execute(string id, ExecuteRequest request, CancellationToken cancellationToken)
    {
        ValidateOperation(request.OperationId);
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            coordinator.EnsureOperational();
            var plan = store.Get<PlanView>(id, "Plan", Viewer);
            if (request.Version != plan.Version)
                throw new RenameException("StalePlan", "Confirm the exact prepared plan version.", 409);
            var operation = "Execute:" + id;
            var previous = store.Operation(operation);
            if (previous is not null) return Accepted(previous, store.Get<RunView>(previous, "Run").Status);
            coordinator.EnsureNoRecovery();
            if (plan.Status != "Ready")
                throw new RenameException("PlanNotReady", "The plan is not ready for execution.", 409);
            var run = CreateRun(plan.Id, null, "Rename", plan.Files.Select(f =>
                new RunFile(f.Id, f.SourcePath, f.TargetPath, JournaledMover.Staging(f.SourcePath), f.Hash, "Pending",
                    CompanionOf: f.CompanionOf)).ToArray(),
                plan.Pipeline.DeleteEmptyFolders) with { Origin = plan.Origin, CreatedBy = currentUser?.Current ?? plan.CreatedBy };
            coordinator.Enqueue(run.Id, "Run", run.Status, run, "Execute", operation);
            recorder.Notify("Run", run.Id, run.Status);
            logger.LogInformation("Queued {Kind} run {RunId} of plan {PlanId} for {FileCount} files", run.Kind, run.Id,
                plan.Id, run.Files.Count);
            return Accepted(run.Id, run.Status);
        }
        finally { coordinator.Gate.Release(); }
    }

    public Task<RunPage> GetRuns(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100)
            throw new RenameException("InvalidPagination", "Use page >= 1 and pageSize between 1 and 100.");
        var slice = store.List<RunView>("Run", skip: (page - 1) * pageSize, take: pageSize + 1, ownedBy: Viewer);
        return Task.FromResult(new RunPage(slice.Take(pageSize).ToArray(), page, pageSize, slice.Count > pageSize));
    }

    // Rollbacks share their parent's owner (both belong to the plan's owner), so every child is visible too.
    public Task<RunView> GetRun(string id, CancellationToken cancellationToken)
        => Task.FromResult(store.Get<RunView>(id, "Run", Viewer) with { RollbackRuns = store.ChildRuns(id) });

    public async Task<IReadOnlyList<string>> DeleteRun(string id, CancellationToken cancellationToken)
    {
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            coordinator.EnsureOperational();
            var deleted = store.DeleteRun(id);
            foreach (var run in deleted) recorder.Notify("Run", run.Id, "Deleted", run.OwnerId);
            logger.LogInformation("Deleted run {RunId} with {Count} runs in total", id, deleted.Count);
            return deleted.Select(run => run.Id).ToArray();
        }
        finally { coordinator.Gate.Release(); }
    }

    public Task<DeletableRuns> GetDeletableRuns(CancellationToken cancellationToken)
    {
        var ids = store.DeletableRunIds(take: MaxRunsPerDeletion + 1);
        return Task.FromResult(new DeletableRuns(ids.Take(MaxRunsPerDeletion).ToArray(), ids.Count > MaxRunsPerDeletion));
    }

    public async Task<IReadOnlyList<string>> DeleteRuns(DeleteRunsRequest request, CancellationToken cancellationToken)
    {
        if (request.RunIds is null || request.RunIds.Count is < 1 or > MaxRunsPerDeletion
            || request.RunIds.Any(string.IsNullOrWhiteSpace) || request.RunIds.Distinct().Count() != request.RunIds.Count)
            throw new RenameException("InvalidSelection", $"Select 1–{MaxRunsPerDeletion} distinct run IDs.");
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            coordinator.EnsureOperational();
            var deleted = store.DeleteRuns(request.RunIds);
            foreach (var run in deleted) recorder.Notify("Run", run.Id, "Deleted", run.OwnerId);
            logger.LogInformation("Deleted {Count} runs", deleted.Count);
            return deleted.Select(run => run.Id).ToArray();
        }
        finally { coordinator.Gate.Release(); }
    }

    public async Task<SelectionPreview> RollbackPreview(string id, SelectionRequest request, CancellationToken cancellationToken)
    {
        ValidateSelection(request);
        // Tracked before waiting for the gate, so a polling client sees the check as soon as it is queued.
        using var tracking = rollbackChecks.Start(id, request.OperationId, request.FileIds.Count);
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            return await rollbacks.Build(store.Get<RunView>(id, "Run", Viewer), request.FileIds, cancellationToken,
                tracking.Progress);
        }
        finally { coordinator.Gate.Release(); }
    }

    public Task<RollbackCheckProgress> GetRollbackCheckProgress(string id, string operationId, CancellationToken cancellationToken)
    {
        ValidateOperation(operationId);
        if (Viewer is { } viewer && store.OwnerOf(id) != viewer)
            throw new RenameException("NotFound", "The requested resource was not found.", 404);
        return Task.FromResult(rollbackChecks.Get(id, operationId));
    }

    public async Task<JobAccepted> Rollback(string id, SelectionRequest request, CancellationToken cancellationToken)
    {
        ValidateSelection(request);
        ValidateOperation(request.OperationId);
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            coordinator.EnsureOperational();
            var parent = store.Get<RunView>(id, "Run", Viewer);
            var key = "Rollback:" + id + ":" + request.OperationId;
            var previous = store.Operation(key);
            if (previous is not null) return Accepted(previous, store.Get<RunView>(previous, "Run").Status);
            coordinator.EnsureNoRecovery();
            var preview = await rollbacks.Build(parent, request.FileIds, cancellationToken);
            var safe = preview.Files.Where(f => f.Safe).Select(f => f.Id).ToHashSet();
            if (safe.Count == 0)
                throw new RenameException("RollbackBlocked", "No selected files can safely be rolled back. Inspect the rollback preview.", 409);
            var files = parent.Files.Where(f => safe.Contains(f.Id)).Select(f =>
                new RunFile(f.Id, f.TargetPath, f.SourcePath, JournaledMover.Staging(f.TargetPath), f.Hash, "Pending",
                    CompanionOf: f.CompanionOf)).ToArray();
            // Unless the request decides, a rollback inherits the cleanup choice, so folders the rename created and
            // filled are removed again.
            var run = CreateRun(parent.PlanId, parent.Id, "Rollback", files,
                request.DeleteEmptyFolders ?? parent.DeleteEmptyFolders) with { CreatedBy = currentUser?.Current };
            coordinator.Enqueue(run.Id, "Run", run.Status, run, "Execute", key);
            recorder.Notify("Run", run.Id, run.Status);
            logger.LogInformation("Queued {Kind} run {RunId} of run {ParentRunId} for {FileCount} files", run.Kind, run.Id,
                parent.Id, run.Files.Count);
            return Accepted(run.Id, run.Status);
        }
        finally { coordinator.Gate.Release(); }
    }

    public async Task<RecoveryPreview> PreviewRecovery(string id, CancellationToken cancellationToken)
    {
        await coordinator.Gate.WaitAsync(cancellationToken);
        try { return await recoveries.Build(store.Get<RunView>(id, "Run", Viewer), cancellationToken); }
        finally { coordinator.Gate.Release(); }
    }

    public async Task<JobAccepted> Recover(string id, RecoveryRequest request, CancellationToken cancellationToken)
    {
        ValidateOperation(request.OperationId);
        if (request.Action != "RestoreOriginal")
            throw new RenameException("InvalidRecovery", "The supported recovery action is RestoreOriginal.");
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            coordinator.EnsureOperational();
            var run = store.Get<RunView>(id, "Run", Viewer);
            var key = "Recover:" + id + ":" + request.OperationId;
            var previous = store.Operation(key);
            if (previous is not null) return Accepted(previous, store.Get<RunView>(previous, "Run").Status);
            if (run.Status != "RecoveryRequired")
                throw new RenameException("RecoveryNotRequired", "This run does not require recovery.", 409);
            var preview = await recoveries.Build(run, cancellationToken);
            if (!preview.CanRecover)
                throw new RenameException("RecoveryBlocked", "Recovery evidence is ambiguous, changed or blocked. No files were moved.", 409);
            run = run with { Status = "RecoveryQueued", RecoveredBy = currentUser?.Current ?? run.RecoveredBy };
            coordinator.RememberOrigin(id);
            store.RequeueRecovery(run, key);
            recorder.Notify("Run", id, run.Status);
            logger.LogInformation("Queued recovery of run {RunId} for {FileCount} files", id, run.Files.Count);
            return Accepted(id, run.Status);
        }
        finally { coordinator.Gate.Release(); }
    }

    /// <summary>Runs the next queued job, if any; called by <see cref="RenameWorker"/>.</summary>
    public Task<bool> ProcessNext(CancellationToken cancellationToken) => jobs.ProcessNext(cancellationToken);

    /// <summary>
    /// The user whose history the current request may see; null when it may see everything: administrators, and
    /// background work such as jobs and watch folders.
    /// </summary>
    private long? Viewer => currentUser is { Current: { } user, IsAdmin: false } ? user.Id : null;

    private static string NewId() => Guid.NewGuid().ToString("N");
    private static RunView CreateRun(string planId, string? parent, string kind, IReadOnlyList<RunFile> files,
        bool deleteEmptyFolders)
        => new(NewId(), planId, parent, kind, "Queued", files, [], DateTimeOffset.UtcNow, DeleteEmptyFolders: deleteEmptyFolders);
    private static JobAccepted Accepted(string id, string status, bool plan = false)
        => new(id, status, "/api/" + (plan ? "rename-plans/" : "rename-runs/") + id);
    private static void ValidateOperation(string value)
    {
        if (!Guid.TryParse(value, out _)) throw new RenameException("InvalidOperationId", "Use a UUID operationId.");
    }
    private void ValidateSelection(SelectionRequest request)
    {
        var limit = pipeline.Limits.MaxFilesPerRun;
        if (request.FileIds is null || request.FileIds.Count < 1 || request.FileIds.Count > limit
            || request.FileIds.Distinct().Count() != request.FileIds.Count)
            throw new RenameException("InvalidSelection", $"Select 1–{limit} distinct file IDs.");
    }
}
