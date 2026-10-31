using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Previews;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Infrastructure.Renaming;

public sealed class RenameService(RenameStore store, IRenameFileSystem fileSystem, PipelineEngine pipeline,
    ILogger<RenameService> logger, IRenameNotifier? notifier = null,
    ILibraryNotifier? libraryNotifier = null) : IRenameService
{
    /// <summary>Most runs one bulk deletion accepts; also caps the "select all pages" candidate list.</summary>
    public const int MaxRunsPerDeletion = 1000;
    private readonly SemaphoreSlim gate = new(1);
    private readonly SemaphoreSlim previews = new(4);
    // In-flight rollback checks keyed by run and operation, so a polling client can show per-file progress.
    private readonly ConcurrentDictionary<string, RollbackCheckProgress> rollbackChecks = new();
    // The trace context of the request that queued each job, so the job's span links back to it. Best effort and
    // in memory only: jobs resumed after a restart start unlinked traces.
    private readonly ConcurrentDictionary<string, ActivityContext> jobOrigins = new();
    private bool persistenceUnavailable;

    public async Task<PreviewResult> Preview(PipelineRequest request, CancellationToken cancellationToken)
    {
        if (!await previews.WaitAsync(0, cancellationToken))
            throw Rejected(new RenameException("PreviewBusy", "Four previews are already running. Try again shortly.", 429));
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
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOperational();
            var id = NewId();
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<PipelineRequest>(
                System.Text.Json.JsonSerializer.Serialize(request, RenameStore.Json), RenameStore.Json)!;
            var plan = new PlanView(id, 1, "Preparing", 0, snapshot, [], [], DateTimeOffset.UtcNow, Origin: origin);
            Enqueue(id, "Plan", plan.Status, plan, "Prepare");
            Notify("Plan", id, plan.Status);
            logger.LogInformation("Queued plan {PlanId} for {FileCount} files with {StepCount} steps from {Origin}", id,
                request.Files.Count, request.Plugins.Count, origin ?? "user");
            return Accepted(id, plan.Status, true);
        }
        finally { gate.Release(); }
    }

    public Task<PlanView> GetPlan(string id, CancellationToken cancellationToken)
        => Task.FromResult(store.Get<PlanView>(id, "Plan"));

    public async Task<JobAccepted> Execute(string id, ExecuteRequest request, CancellationToken cancellationToken)
    {
        ValidateOperation(request.OperationId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOperational();
            var plan = store.Get<PlanView>(id, "Plan");
            if (request.Version != plan.Version)
                throw new RenameException("StalePlan", "Confirm the exact prepared plan version.", 409);
            var operation = "Execute:" + id;
            var previous = store.Operation(operation);
            if (previous is not null) return Accepted(previous, store.Get<RunView>(previous, "Run").Status);
            EnsureNoRecovery();
            if (plan.Status != "Ready")
                throw new RenameException("PlanNotReady", "The plan is not ready for execution.", 409);
            var run = CreateRun(plan.Id, null, "Rename", plan.Files.Select(f =>
                new RunFile(f.Id, f.SourcePath, f.TargetPath, Staging(f.SourcePath), f.Hash, "Pending",
                    CompanionOf: f.CompanionOf)).ToArray(),
                plan.Pipeline.DeleteEmptyFolders) with { Origin = plan.Origin };
            Enqueue(run.Id, "Run", run.Status, run, "Execute", operation);
            Notify("Run", run.Id, run.Status);
            logger.LogInformation("Queued {Kind} run {RunId} of plan {PlanId} for {FileCount} files", run.Kind, run.Id,
                plan.Id, run.Files.Count);
            return Accepted(run.Id, run.Status);
        }
        finally { gate.Release(); }
    }

    public Task<RunPage> GetRuns(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100)
            throw new RenameException("InvalidPagination", "Use page >= 1 and pageSize between 1 and 100.");
        var slice = store.List<RunView>("Run", skip: (page - 1) * pageSize, take: pageSize + 1);
        return Task.FromResult(new RunPage(slice.Take(pageSize).ToArray(), page, pageSize, slice.Count > pageSize));
    }

    public Task<RunView> GetRun(string id, CancellationToken cancellationToken)
        => Task.FromResult(store.Get<RunView>(id, "Run") with { RollbackRuns = store.ChildRuns(id) });

    public async Task<IReadOnlyList<string>> DeleteRun(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOperational();
            var deleted = store.DeleteRun(id);
            foreach (var run in deleted) Notify("Run", run, "Deleted");
            logger.LogInformation("Deleted run {RunId} with {Count} runs in total", id, deleted.Count);
            return deleted;
        }
        finally { gate.Release(); }
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
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOperational();
            var deleted = store.DeleteRuns(request.RunIds);
            foreach (var run in deleted) Notify("Run", run, "Deleted");
            logger.LogInformation("Deleted {Count} runs", deleted.Count);
            return deleted;
        }
        finally { gate.Release(); }
    }

    public async Task<SelectionPreview> RollbackPreview(string id, SelectionRequest request, CancellationToken cancellationToken)
    {
        ValidateSelection(request);
        var key = RollbackCheckKey(id, request.OperationId);
        // Only the first in-flight check for an operation reports progress, so a repeated request cannot reset or remove it.
        var owner = rollbackChecks.TryAdd(key, new(0, request.FileIds.Count));
        Action<int>? progress = null;
        if (owner) progress = completed => rollbackChecks[key] = new(completed, request.FileIds.Count);
        try
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await BuildRollback(store.Get<RunView>(id, "Run"), request.FileIds, cancellationToken, progress);
            }
            finally { gate.Release(); }
        }
        finally { if (owner) rollbackChecks.TryRemove(key, out _); }
    }

    public Task<RollbackCheckProgress> GetRollbackCheckProgress(string id, string operationId, CancellationToken cancellationToken)
    {
        ValidateOperation(operationId);
        return Task.FromResult(rollbackChecks.TryGetValue(RollbackCheckKey(id, operationId), out var progress)
            ? progress : new RollbackCheckProgress(0, 0));
    }

    public async Task<JobAccepted> Rollback(string id, SelectionRequest request, CancellationToken cancellationToken)
    {
        ValidateSelection(request);
        ValidateOperation(request.OperationId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOperational();
            var key = "Rollback:" + id + ":" + request.OperationId;
            var previous = store.Operation(key);
            if (previous is not null) return Accepted(previous, store.Get<RunView>(previous, "Run").Status);
            EnsureNoRecovery();
            var parent = store.Get<RunView>(id, "Run");
            var preview = await BuildRollback(parent, request.FileIds, cancellationToken);
            var safe = preview.Files.Where(f => f.Safe).Select(f => f.Id).ToHashSet();
            if (safe.Count == 0)
                throw new RenameException("RollbackBlocked", "No selected files can safely be rolled back. Inspect the rollback preview.", 409);
            var files = parent.Files.Where(f => safe.Contains(f.Id)).Select(f =>
                new RunFile(f.Id, f.TargetPath, f.SourcePath, Staging(f.TargetPath), f.Hash, "Pending",
                    CompanionOf: f.CompanionOf)).ToArray();
            // Unless the request decides, a rollback inherits the cleanup choice, so folders the rename created and
            // filled are removed again.
            var run = CreateRun(parent.PlanId, parent.Id, "Rollback", files,
                request.DeleteEmptyFolders ?? parent.DeleteEmptyFolders);
            Enqueue(run.Id, "Run", run.Status, run, "Execute", key);
            Notify("Run", run.Id, run.Status);
            logger.LogInformation("Queued {Kind} run {RunId} of run {ParentRunId} for {FileCount} files", run.Kind, run.Id,
                parent.Id, run.Files.Count);
            return Accepted(run.Id, run.Status);
        }
        finally { gate.Release(); }
    }

    public async Task<RecoveryPreview> PreviewRecovery(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await BuildRecovery(store.Get<RunView>(id, "Run"), cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<JobAccepted> Recover(string id, RecoveryRequest request, CancellationToken cancellationToken)
    {
        ValidateOperation(request.OperationId);
        if (request.Action != "RestoreOriginal")
            throw new RenameException("InvalidRecovery", "The supported recovery action is RestoreOriginal.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOperational();
            var key = "Recover:" + id + ":" + request.OperationId;
            var previous = store.Operation(key);
            if (previous is not null) return Accepted(previous, store.Get<RunView>(previous, "Run").Status);
            var run = store.Get<RunView>(id, "Run");
            if (run.Status != "RecoveryRequired")
                throw new RenameException("RecoveryNotRequired", "This run does not require recovery.", 409);
            var preview = await BuildRecovery(run, cancellationToken);
            if (!preview.CanRecover)
                throw new RenameException("RecoveryBlocked", "Recovery evidence is ambiguous, changed or blocked. No files were moved.", 409);
            run = run with { Status = "RecoveryQueued" };
            RememberOrigin(id);
            store.RequeueRecovery(run, key);
            Notify("Run", id, run.Status);
            logger.LogInformation("Queued recovery of run {RunId} for {FileCount} files", id, run.Files.Count);
            return Accepted(id, run.Status);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> ProcessNext(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOperational();
            var job = store.TakeJob();
            if (job is null) return false;
            var (id, jobKind) = job.Value;
            var started = Stopwatch.GetTimestamp();
            var kind = jobKind;
            ActivityLink[]? links = jobOrigins.TryRemove(id, out var origin) ? [new(origin)] : null;
            using var activity = NamerreTelemetry.Source.StartActivity("rename." + jobKind.ToLowerInvariant(),
                ActivityKind.Internal, parentContext: default, links: links);
            activity?.SetTag("namerre.job.id", id);
            string status;
            IReadOnlyList<RunFile>? files = null;
            try
            {
                if (jobKind == "Prepare")
                {
                    var plan = await PrepareJob(id, cancellationToken);
                    status = plan.Status;
                    activity?.SetTag("namerre.files.count", plan.Files.Count);
                    if (plan.Status != "Ready")
                        logger.LogInformation("Plan {PlanId} cannot be prepared: {IssueCount} issues, first {IssueCode}", id,
                            plan.Issues.Count, plan.Issues.FirstOrDefault()?.Code);
                }
                else
                {
                    var run = jobKind == "Recover"
                        ? await RecoverJob(id, cancellationToken) : await ExecuteJob(id, cancellationToken);
                    if (jobKind != "Recover") kind = run.Kind;
                    status = run.Status;
                    files = run.Files;
                }
            }
            catch (Exception exception)
            {
                activity.Fail(exception);
                logger.LogError(exception, "Rename job failed: {JobId}", id);
                try
                {
                    if (jobKind == "Prepare")
                    {
                        var plan = store.Get<PlanView>(id, "Plan");
                        Save(plan with { Status = "Failed", Issues = [PublicIssue(exception)] });
                        status = "Failed";
                    }
                    else
                    {
                        var run = store.Get<RunView>(id, "Run");
                        if (jobKind != "Recover") kind = run.Kind;
                        run = run with
                        {
                            Status = "RecoveryRequired", Error = PublicIssue(exception).Message,
                            Files = run.Files.Select(f => FailureFile(run, f, exception)).ToArray()
                        };
                        Save(run);
                        status = run.Status;
                        files = run.Files;
                        logger.LogWarning("Run {RunId} requires recovery: {ErrorCode}", id, PublicIssue(exception).Code);
                    }
                }
                catch (Exception storageFailure)
                {
                    persistenceUnavailable = true;
                    logger.LogCritical(storageFailure, "Cannot persist job failure; retained journal requires startup inspection.");
                    RecordJob(activity, id, kind, "StorageUnavailable", null, started);
                    throw new RenameException("StorageUnavailable", "Durable journaling is unavailable. Mutations have stopped.", 503);
                }
            }
            RecordJob(activity, id, kind, status, files, started);
            try { store.FinishJob(id); }
            catch
            {
                persistenceUnavailable = true;
                throw;
            }
            return true;
        }
        finally { gate.Release(); }
    }

    private async Task<PlanView> PrepareJob(string id, CancellationToken cancellationToken)
    {
        var plan = store.Get<PlanView>(id, "Plan");
        if (plan.Facts is null)
        {
            using var facts = NamerreTelemetry.Source.StartActivity("rename.facts");
            // Read once and kept, so steps see the same sizes and dates however often the plan is evaluated.
            plan = plan with { Facts = pipeline.ReadFacts(plan.Pipeline) };
            Save(plan);
        }
        PreviewResult preview;
        using (NamerreTelemetry.Source.StartActivity("rename.preview"))
            preview = await pipeline.Preview(plan.Pipeline, plan.Facts, cancellationToken);
        if (!preview.CanPrepare)
        {
            plan = plan with { Status = "Failed", Issues = preview.Files.SelectMany(f => f.Issues).ToArray() };
            Save(plan);
            return plan;
        }
        using var fingerprints = NamerreTelemetry.Source.StartActivity("rename.fingerprint");
        fingerprints?.SetTag("namerre.verify_content", plan.Pipeline.VerifyContent);
        var files = new List<PlanFile>();
        foreach (var file in preview.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Quick-check plans record size and modification time instead of reading every file.
            var snapshot = plan.Pipeline.VerifyContent
                ? await fileSystem.Hash(file.SourcePath, cancellationToken) : fileSystem.Stamp(file.SourcePath);
            files.Add(new(file.Id, file.SourcePath, file.TargetPath, snapshot.Hash, snapshot.Size, file.CompanionOf));
            plan = plan with { Files = files.ToArray(), CompletedFiles = files.Count };
            Save(plan);
        }
        // Metadata confirmation records are durable snapshots; execution uses only these prepared paths and hashes.
        plan = plan with { Status = "Ready" };
        Save(plan);
        return plan;
    }

    private async Task<RunView> ExecuteJob(string id, CancellationToken cancellationToken)
    {
        var run = store.Get<RunView>(id, "Run");
        Activity.Current?.SetTag("namerre.run.kind", run.Kind);
        Activity.Current?.SetTag("namerre.files.count", run.Files.Count);
        EnsureNoRecovery(id);
        run = run with { Status = "Running" };
        Save(run);
        using (NamerreTelemetry.Source.StartActivity("rename.preflight"))
            await Preflight(run.Files, cancellationToken);
        var changed = run.Files.Where(f => f.SourcePath != f.TargetPath).ToArray();
        using (NamerreTelemetry.Source.StartActivity("rename.stage"))
            foreach (var file in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Verify(file.SourcePath, file.Hash, cancellationToken, file.Id);
                run = MoveJournaled(run, file.Id, file.SourcePath, file.StagingPath, "Stage");
                run = ChangeFile(run, file.Id, "Staged");
            }
        using (NamerreTelemetry.Source.StartActivity("rename.place"))
            foreach (var file in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                run = EnsureDirectories(run, file.Id, file.TargetPath);
                await Verify(file.StagingPath, file.Hash, cancellationToken, file.Id);
                run = MoveJournaled(run, file.Id, file.StagingPath, file.TargetPath, "Place");
                run = ChangeFile(run, file.Id, "Completed");
            }
        if (run.DeleteEmptyFolders)
        {
            using var cleanup = NamerreTelemetry.Source.StartActivity("rename.cleanup");
            // Every file is already placed, so this is the wait clients see as "Finishing up".
            var started = Stopwatch.GetTimestamp();
            run = DeleteEmptyFolders(run, changed);
            var removed = run.Journal.Count(j => j.Kind == "DeleteDirectory" && j.Status == "Completed");
            cleanup?.SetTag("namerre.folders.deleted", removed);
            logger.LogInformation("Empty folder cleanup for run {Id} removed {Count} folders in {Elapsed}", run.Id, removed,
                Stopwatch.GetElapsedTime(started));
        }
        run = run with { Status = "Completed", Files = run.Files.Select(f => f with { Status = "Completed" }).ToArray() };
        Complete(run, "RolledBack");
        return run;
    }

    private async Task Preflight(IReadOnlyList<RunFile> files, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            fileSystem.Validate(file.SourcePath);
            fileSystem.Validate(file.TargetPath);
            fileSystem.CheckSameDevice(file.SourcePath, file.TargetPath);
            if (files.Any(other => other.Id != file.Id && (fileSystem.SamePath(other.TargetPath, file.TargetPath)
                    || fileSystem.SamePath(other.SourcePath, file.SourcePath))))
                throw new RenameException("DuplicatePath", "The operation contains duplicate source or destination paths.");
            if (file.SourcePath != file.TargetPath && fileSystem.Exists(file.TargetPath)
                && !files.Any(other => other.SourcePath != other.TargetPath && fileSystem.SamePath(other.SourcePath, file.TargetPath)))
                throw new RenameException("TargetExists", "A destination is occupied. Nothing will be overwritten.");
            if (fileSystem.Exists(file.StagingPath))
                throw new RenameException("StageExists", "A reserved staging path is occupied.");
            await Verify(file.SourcePath, file.Hash, cancellationToken, file.Id);
        }
    }

    private async Task<SelectionPreview> BuildRollback(RunView run, IReadOnlyList<string> ids, CancellationToken cancellationToken,
        Action<int>? progress = null)
    {
        var selected = ids.ToHashSet();
        var result = new List<SelectionFile>();
        var active = store.List<RunView>("Run", ["Queued", "Running", "RecoveryRequired", "RecoveryQueued", "Recovering"])
            .Where(r => r.ParentRunId == run.Id).ToArray();
        foreach (var id in ids)
        {
            var file = run.Files.FirstOrDefault(f => f.Id == id);
            var issues = new List<RenameIssue>();
            if (file is null)
            {
                result.Add(new(id, false, "", "", [new("UnknownFile", "This file is not part of the run.")]));
                progress?.Invoke(result.Count);
                continue;
            }
            if (run.Status != "Completed" || file.Status != "Completed" || run.Kind != "Rename")
                issues.Add(new("NotRollbackEligible", "Only completed, not-yet-rolled-back rename files are eligible."));
            if (active.Any(r => r.Files.Any(f => f.Id == id)))
                issues.Add(new("RollbackPending", "This file already has a pending rollback or recovery."));
            if (issues.Count == 0)
            {
                try
                {
                    await Verify(file.TargetPath, file.Hash, cancellationToken);
                    fileSystem.Validate(file.SourcePath);
                    fileSystem.CheckSameDevice(file.TargetPath, file.SourcePath);
                    if (file.SourcePath != file.TargetPath && fileSystem.Exists(file.SourcePath)
                        && !run.Files.Any(other => selected.Contains(other.Id) && other.Status == "Completed"
                            && other.SourcePath != other.TargetPath && fileSystem.SamePath(other.TargetPath, file.SourcePath)))
                        issues.Add(new("RollbackDestinationOccupied", "The original path is occupied. Select all dependent swap/cycle files if applicable."));
                }
                catch (Exception exception) when (exception is RenameException or IOException or UnauthorizedAccessException)
                {
                    issues.Add(PublicIssue(exception));
                }
            }
            result.Add(new(id, issues.Count == 0, file.TargetPath, file.SourcePath, issues));
            progress?.Invoke(result.Count);
        }
        // A blocked participant cannot be treated as a source that will vacate another selected destination.
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < result.Count; i++)
            {
                var item = result[i];
                if (!item.Safe || item.CurrentPath == item.DestinationPath || !fileSystem.Exists(item.DestinationPath)) continue;
                if (!result.Any(other => other.Safe && other.CurrentPath != other.DestinationPath
                    && fileSystem.SamePath(other.CurrentPath, item.DestinationPath)))
                {
                    result[i] = item with { Safe = false, Issues = [new("BlockedDependency", "Another required participant is unsafe or not selected.")] };
                    changed = true;
                }
            }
        } while (changed);
        return new(result);
    }

    private async Task<RecoveryPreview> BuildRecovery(RunView run, CancellationToken cancellationToken)
    {
        var files = new List<RecoveryFile>();
        foreach (var file in run.Files)
        {
            var candidates = new List<string>();
            var issues = new List<RenameIssue>();
            var paths = new[] { file.SourcePath, file.StagingPath, file.TargetPath }
                .Concat(run.Journal.Where(j => j.FileId == file.Id && !IsDirectoryEntry(j.Kind))
                    .SelectMany(j => new[] { j.FromPath, j.ToPath })).Where(p => p.Length > 0).Distinct().ToArray();
            var unique = new List<string>();
            foreach (var path in paths)
                if (!unique.Any(p => fileSystem.SamePath(p, path))) unique.Add(path);
            foreach (var path in unique)
            {
                try
                {
                    if (!fileSystem.Exists(path)) continue;
                    if (await Fingerprint(path, file.Hash, cancellationToken) == file.Hash)
                    {
                        var actual = fileSystem.ActualPath(path);
                        if (!candidates.Contains(actual, StringComparer.Ordinal)) candidates.Add(actual);
                    }
                }
                catch (Exception exception) when (exception is RenameException or IOException or UnauthorizedAccessException)
                {
                    issues.Add(PublicIssue(exception));
                }
            }
            if (candidates.Count != 1)
                issues.Add(new(candidates.Count == 0 ? "ContentMissing" : "AmbiguousContent",
                    candidates.Count == 0 ? "No expected content was found at journaled locations."
                    : "Multiple copies have the expected hash. Content equivalence cannot prove unique file identity."));
            files.Add(new(file.Id, candidates.Count == 1 ? candidates[0] : null,
                candidates.Count == 1 ? "Located" : "Uncertain", issues));
        }
        for (var i = 0; i < files.Count; i++)
        {
            var item = files[i];
            if (item.LocatedPath is null) continue;
            var original = run.Files.Single(f => f.Id == item.Id).SourcePath;
            var issues = item.Issues.ToList();
            try
            {
                fileSystem.CheckSameDevice(item.LocatedPath, original);
                if (!fileSystem.SamePath(item.LocatedPath, original) && fileSystem.Exists(original)
                    && !files.Any(other => other.LocatedPath is not null && other.Issues.Count == 0
                        && fileSystem.SamePath(other.LocatedPath, original)))
                    issues.Add(new("RecoveryDestinationOccupied", "The pre-operation location contains unrelated or changed content."));
                if (files.Any(other => other.Id != item.Id && other.LocatedPath is not null
                    && fileSystem.SamePath(other.LocatedPath, item.LocatedPath)))
                    issues.Add(new("AmbiguousOwnership", "Multiple journal items identify the same existing content."));
            }
            catch (RenameException exception) { issues.Add(PublicIssue(exception)); }
            files[i] = item with { Issues = issues };
        }
        return new(run.Id, run.Status is "RecoveryRequired" or "RecoveryQueued" or "Recovering"
            && files.All(f => f.Issues.Count == 0), "RestoreOriginal", files);
    }

    private async Task<RunView> RecoverJob(string id, CancellationToken cancellationToken)
    {
        var run = store.Get<RunView>(id, "Run");
        Activity.Current?.SetTag("namerre.run.kind", run.Kind);
        Activity.Current?.SetTag("namerre.files.count", run.Files.Count);
        RecoveryPreview evidence;
        using (NamerreTelemetry.Source.StartActivity("rename.recovery.evidence"))
            evidence = await BuildRecovery(run, cancellationToken);
        if (!evidence.CanRecover) throw new RenameException("RecoveryBlocked", "Recovery evidence changed or is ambiguous.");
        run = run with { Status = "Recovering" };
        Save(run);
        var restoring = new List<(RunFile File, string Stage)>();
        foreach (var file in run.Files)
        {
            var located = evidence.Files.Single(f => f.Id == file.Id).LocatedPath!;
            if (fileSystem.SamePath(located, file.SourcePath) && located == file.SourcePath) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var stage = Staging(located);
            await Verify(located, file.Hash, cancellationToken, file.Id);
            run = MoveJournaled(run, file.Id, located, stage, "RecoveryStage");
            restoring.Add((file, stage));
        }
        foreach (var (file, stage) in restoring)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run = EnsureDirectories(run, file.Id, file.SourcePath);
            await Verify(stage, file.Hash, cancellationToken, file.Id);
            run = MoveJournaled(run, file.Id, stage, file.SourcePath, "RecoveryRestore");
        }
        run = run with { Status = "Recovered", Error = null,
            Files = run.Files.Select(f => f with { Status = "Restored", Error = null, ErrorCode = null }).ToArray() };
        Complete(run, "Completed");
        return run;
    }

    private RunView EnsureDirectories(RunView run, string fileId, string target)
    {
        foreach (var directory in fileSystem.MissingDirectories(target))
        {
            var sequence = NextSequence(run);
            var intent = new JournalEntry(sequence, "CreateDirectory", fileId, "", directory, "Intent");
            run = run with { Journal = run.Journal.Append(intent).ToArray() };
            Save(run);
            fileSystem.CreateDirectory(directory);
            run = CompleteJournal(run, sequence);
        }
        return run;
    }

    // Runs once every file is placed. Each folder is checked deepest first, so a parent emptied by removing its
    // only child folder goes too; the root itself is never a candidate. Cleanup is best effort: a folder that is
    // not empty or cannot be deleted stays, and the run still completes because every file reached its target.
    private RunView DeleteEmptyFolders(RunView run, IReadOnlyList<RunFile> moved)
    {
        var folders = moved.SelectMany(f => Ancestors(f.SourcePath).Select(folder => (Folder: folder, FileId: f.Id)))
            .DistinctBy(entry => entry.Folder, StringComparer.Ordinal)
            .OrderByDescending(entry => entry.Folder.Count(c => c == '/'));
        foreach (var (folder, fileId) in folders)
        {
            string? failed = null;
            try
            {
                if (!fileSystem.IsEmptyDirectory(folder)) continue;
            }
            catch (Exception exception) when (exception is RenameException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Cannot inspect folder {Folder} for cleanup", folder);
                failed = "Failed";
            }
            // A failed inspection changed nothing on disk, so its entry is recorded as failed without an intent.
            var sequence = NextSequence(run);
            run = run with { Journal = run.Journal.Append(new JournalEntry(sequence, "DeleteDirectory", fileId, folder, "",
                failed ?? "Intent")).ToArray() };
            Save(run);
            if (failed is not null) continue;
            var status = "Skipped";
            try { if (fileSystem.DeleteEmptyDirectory(folder)) status = "Completed"; }
            catch (Exception exception) when (exception is RenameException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Cannot delete empty folder {Folder}", folder);
                status = "Failed";
            }
            run = CompleteJournal(run, sequence, status);
        }
        return run;
    }

    private RunView MoveJournaled(RunView run, string fileId, string source, string destination, string kind)
    {
        var sequence = NextSequence(run);
        run = run with { Journal = run.Journal.Append(new JournalEntry(sequence, kind, fileId, source, destination, "Intent")).ToArray() };
        Save(run);
        try { fileSystem.Move(source, destination); }
        catch (Exception exception)
        {
            exception.Data["fileId"] = fileId;
            throw;
        }
        return CompleteJournal(run, sequence);
    }

    private RunView CompleteJournal(RunView run, long sequence, string status = "Completed")
    {
        run = run with { Journal = run.Journal.Select(j => j.Sequence == sequence ? j with { Status = status } : j).ToArray() };
        Save(run);
        return run;
    }

    private RunView ChangeFile(RunView run, string fileId, string status)
    {
        run = run with { Files = run.Files.Select(f => f.Id == fileId ? f with { Status = status } : f).ToArray() };
        Save(run);
        return run;
    }

    private async Task Verify(string path, string expectedHash, CancellationToken cancellationToken, string? fileId = null)
    {
        try
        {
            if (await Fingerprint(path, expectedHash, cancellationToken) != expectedHash)
                throw new RenameException("HashMismatch", IsStamp(expectedHash)
                    ? "File size or modification time changed since preparation. It was not moved."
                    : "File content changed since preparation. It was not moved.", fileId: fileId);
        }
        catch (Exception exception)
        {
            if (fileId is not null) exception.Data["fileId"] = fileId;
            throw;
        }
    }

    /// <summary>Recomputes the fingerprint kind recorded at preparation: a content hash or a size/time stamp.</summary>
    private async Task<string> Fingerprint(string path, string expected, CancellationToken cancellationToken)
        => IsStamp(expected) ? fileSystem.Stamp(path).Hash : (await fileSystem.Hash(path, cancellationToken)).Hash;

    private static bool IsStamp(string fingerprint) => fingerprint.StartsWith(BoundedFileSystem.StampPrefix, StringComparison.Ordinal);

    private static RunFile FailureFile(RunView run, RunFile file, Exception exception)
    {
        var last = run.Journal.LastOrDefault(j => j.FileId == file.Id && !IsDirectoryEntry(j.Kind));
        var failedId = (exception as RenameException)?.FileId ?? exception.Data["fileId"] as string;
        var status = last?.Status == "Intent" ? "Uncertain"
            : last?.Kind is "Place" ? "Completed"
            : last?.Kind is "Stage" ? "Staged"
            : last is not null ? "Uncertain" : file.Id == failedId ? "Failed" : "Pending";
        var issue = PublicIssue(exception);
        return file with { Status = status, Error = file.Id == failedId ? issue.Message : file.Error,
            ErrorCode = file.Id == failedId ? issue.Code : file.ErrorCode };
    }

    private void EnsureNoRecovery(string? except = null)
    {
        if (store.List<RunView>("Run", ["RecoveryRequired", "RecoveryQueued", "Recovering"]).Any(r => r.Id != except))
            throw Rejected(new RenameException("RecoveryPending", "Resolve interrupted mutations before starting another mutation.", 409));
    }

    private void EnsureOperational()
    {
        if (persistenceUnavailable)
            throw new RenameException("StorageUnavailable", "Journaling failed. Restart after restoring durable storage; retained jobs require inspection.", 503);
    }

    private void Enqueue<T>(string id, string kind, string status, T value, string jobKind, string? operationKey = null)
    {
        try { store.Enqueue(id, kind, status, value, jobKind, operationKey); }
        catch (RenameException exception) when (exception.Code == "QueueFull") { throw Rejected(exception); }
        RememberOrigin(id);
    }

    private void RememberOrigin(string id)
    {
        if (Activity.Current is { } current) jobOrigins[id] = current.Context;
    }

    private static RenameException Rejected(RenameException exception)
    {
        NamerreTelemetry.Rejections.Add(1, new KeyValuePair<string, object?>("namerre.rejection.reason", exception.Code));
        return exception;
    }

    private void RecordJob(Activity? activity, string id, string kind, string status, IReadOnlyList<RunFile>? files,
        long started)
    {
        var elapsed = Stopwatch.GetElapsedTime(started);
        KeyValuePair<string, object?> kindTag = new("namerre.job.kind", kind);
        NamerreTelemetry.JobDuration.Record(elapsed.TotalSeconds, kindTag,
            new KeyValuePair<string, object?>("namerre.job.status", status));
        foreach (var group in (files ?? []).GroupBy(f => f.Status))
            NamerreTelemetry.JobFiles.Add(group.Count(), kindTag,
                new KeyValuePair<string, object?>("namerre.file.status", group.Key));
        activity?.SetTag("namerre.job.kind", kind);
        activity?.SetTag("namerre.job.status", status);
        logger.LogInformation("Rename job {JobId} ({Kind}) finished as {Status} in {Elapsed}", id, kind, status, elapsed);
    }

    private void Save(RunView run)
    {
        store.Save(run.Id, "Run", run.Status, run);
        Notify("Run", run.Id, run.Status);
    }

    private void Save(PlanView plan)
    {
        store.Save(plan.Id, "Plan", plan.Status, plan);
        Notify("Plan", plan.Id, plan.Status);
    }

    private void Complete(RunView run, string parentFileStatus)
    {
        store.CompleteRun(run, parentFileStatus);
        Notify("Run", run.Id, run.Status);
        NotifyLibraries(run);
        // Completing a rollback or recovery also rewrites the parent's file statuses. The run is already final,
        // so failing to read the parent back must not surface as a job failure.
        if (run.ParentRunId is null) return;
        try { Notify("Run", run.ParentRunId, store.Get<RunView>(run.ParentRunId, "Run").Status); }
        catch (Exception exception) { logger.LogWarning(exception, "Cannot read parent run {Id} for notification", run.ParentRunId); }
    }

    // Runs after the durable write; a failed push only delays clients until their fallback refresh.
    private void Notify(string kind, string id, string status)
    {
        if (notifier is null) return;
        try { notifier.Changed(new(kind, id, status)); }
        catch (Exception exception) { logger.LogWarning(exception, "Rename change notification failed: {Kind} {Id}", kind, id); }
    }
    // Runs after the durable write; the notifier only queues the work, so this never blocks the job.
    private void NotifyLibraries(RunView run)
    {
        if (libraryNotifier is null || run.Status is not ("Completed" or "Recovered")) return;
        try { libraryNotifier.RunFinished(run); }
        catch (Exception exception) { logger.LogWarning(exception, "Library notification failed: Run {Id}", run.Id); }
    }
    private static long NextSequence(RunView run) => run.Journal.Count == 0 ? 1 : run.Journal.Max(j => j.Sequence) + 1;
    private static string NewId() => Guid.NewGuid().ToString("N");
    private static string Staging(string source) => (source.Contains('/') ? source[..(source.LastIndexOf('/') + 1)] : "")
        + ".namerre-stage-" + NewId();
    private static RunView CreateRun(string planId, string? parent, string kind, IReadOnlyList<RunFile> files,
        bool deleteEmptyFolders)
        => new(NewId(), planId, parent, kind, "Queued", files, [], DateTimeOffset.UtcNow, DeleteEmptyFolders: deleteEmptyFolders);
    /// <summary>Folder journal entries record directory changes, never file locations, so recovery ignores them.</summary>
    internal static bool IsDirectoryEntry(string kind) => kind is "CreateDirectory" or "DeleteDirectory";
    private static IEnumerable<string> Ancestors(string path)
    {
        for (var end = path.LastIndexOf('/'); end > 0; end = path.LastIndexOf('/', end - 1))
            yield return path[..end];
    }
    private static JobAccepted Accepted(string id, string status, bool plan = false)
        => new(id, status, "/api/" + (plan ? "rename-plans/" : "rename-runs/") + id);
    private static RenameIssue PublicIssue(Exception exception) => exception is RenameException rename
        ? new(rename.Code, rename.Message) : exception is OperationCanceledException
        ? new("Interrupted", "The job stopped at a journaled boundary. Inspect recovery before continuing.")
        : new("OperationFailed", "A filesystem or durable-storage operation failed. Inspect recovery evidence; no overwrites are allowed.");
    private static string RollbackCheckKey(string id, string operationId) => id + ":" + operationId;
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
