using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Previews;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Infrastructure.Renaming;

/// <summary>Takes the next queued job and runs it: preparing a plan, executing a rename or rollback, or a recovery.</summary>
internal sealed class RenameJobRunner(RenameStore store, IRenameFileSystem fileSystem, PipelineEngine pipeline,
    RenameCoordinator coordinator, RenameRecorder recorder, JournaledMover mover, FileVerifier verifier,
    RecoveryPlanner recovery, ILogger logger)
{
    public async Task<bool> ProcessNext(CancellationToken cancellationToken)
    {
        await coordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            coordinator.EnsureOperational();
            var job = store.TakeJob();
            if (job is null) return false;
            var (id, jobKind) = job.Value;
            var started = Stopwatch.GetTimestamp();
            var kind = jobKind;
            using var activity = NamerreTelemetry.Source.StartActivity("rename." + jobKind.ToLowerInvariant(),
                ActivityKind.Internal, parentContext: default, links: coordinator.TakeOrigin(id));
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
                        recorder.Save(plan with { Status = "Failed", Issues = [RenameIssues.Public(exception)] });
                        status = "Failed";
                    }
                    else
                    {
                        var run = store.Get<RunView>(id, "Run");
                        if (jobKind != "Recover") kind = run.Kind;
                        run = run with
                        {
                            Status = "RecoveryRequired", Error = RenameIssues.Public(exception).Message,
                            Files = run.Files.Select(f => FailureFile(run, f, exception)).ToArray()
                        };
                        recorder.Save(run);
                        status = run.Status;
                        files = run.Files;
                        logger.LogWarning("Run {RunId} requires recovery: {ErrorCode}", id, RenameIssues.Public(exception).Code);
                    }
                }
                catch (Exception storageFailure)
                {
                    coordinator.MarkPersistenceUnavailable();
                    logger.LogCritical(storageFailure, "Cannot persist job failure; retained journal requires startup inspection.");
                    RecordJob(activity, id, kind, "StorageUnavailable", null, started);
                    throw new RenameException("StorageUnavailable", "Durable journaling is unavailable. Mutations have stopped.", 503);
                }
            }
            RecordJob(activity, id, kind, status, files, started);
            try { store.FinishJob(id); }
            catch
            {
                coordinator.MarkPersistenceUnavailable();
                throw;
            }
            return true;
        }
        finally { coordinator.Gate.Release(); }
    }

    private async Task<PlanView> PrepareJob(string id, CancellationToken cancellationToken)
    {
        var plan = store.Get<PlanView>(id, "Plan");
        if (plan.Facts is null)
        {
            using var facts = NamerreTelemetry.Source.StartActivity("rename.facts");
            // Read once and kept, so steps see the same sizes and dates however often the plan is evaluated.
            plan = plan with { Facts = pipeline.ReadFacts(plan.Pipeline) };
            recorder.Save(plan);
        }
        PreviewResult preview;
        using (NamerreTelemetry.Source.StartActivity("rename.preview"))
            preview = await pipeline.Preview(plan.Pipeline, plan.Facts, cancellationToken);
        if (!preview.CanPrepare)
        {
            plan = plan with { Status = "Failed", Issues = preview.Files.SelectMany(f => f.Issues).ToArray() };
            recorder.Save(plan);
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
            recorder.Save(plan);
        }
        // Metadata confirmation records are durable snapshots; execution uses only these prepared paths and hashes.
        plan = plan with { Status = "Ready" };
        recorder.Save(plan);
        return plan;
    }

    private async Task<RunView> ExecuteJob(string id, CancellationToken cancellationToken)
    {
        var run = store.Get<RunView>(id, "Run");
        Activity.Current?.SetTag("namerre.run.kind", run.Kind);
        Activity.Current?.SetTag("namerre.files.count", run.Files.Count);
        coordinator.EnsureNoRecovery(id);
        run = run with { Status = "Running" };
        recorder.Save(run);
        using (NamerreTelemetry.Source.StartActivity("rename.preflight"))
            await Preflight(run.Files, cancellationToken);
        var changed = run.Files.Where(f => f.SourcePath != f.TargetPath).ToArray();
        using (NamerreTelemetry.Source.StartActivity("rename.stage"))
            foreach (var file in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await verifier.Verify(file.SourcePath, file.Hash, cancellationToken, file.Id);
                run = mover.Move(run, file.Id, file.SourcePath, file.StagingPath, "Stage");
                run = mover.ChangeFile(run, file.Id, "Staged");
            }
        using (NamerreTelemetry.Source.StartActivity("rename.place"))
            foreach (var file in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                run = mover.EnsureDirectories(run, file.Id, file.TargetPath);
                await verifier.Verify(file.StagingPath, file.Hash, cancellationToken, file.Id);
                run = mover.Move(run, file.Id, file.StagingPath, file.TargetPath, "Place");
                run = mover.ChangeFile(run, file.Id, "Completed");
            }
        if (run.DeleteEmptyFolders)
        {
            using var cleanup = NamerreTelemetry.Source.StartActivity("rename.cleanup");
            // Every file is already placed, so this is the wait clients see as "Finishing up".
            var started = Stopwatch.GetTimestamp();
            run = mover.DeleteEmptyFolders(run, changed);
            var removed = run.Journal.Count(j => j.Kind == "DeleteDirectory" && j.Status == "Completed");
            cleanup?.SetTag("namerre.folders.deleted", removed);
            logger.LogInformation("Empty folder cleanup for run {Id} removed {Count} folders in {Elapsed}", run.Id, removed,
                Stopwatch.GetElapsedTime(started));
        }
        run = run with { Status = "Completed", Files = run.Files.Select(f => f with { Status = "Completed" }).ToArray() };
        recorder.Complete(run, "RolledBack");
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
            await verifier.Verify(file.SourcePath, file.Hash, cancellationToken, file.Id);
        }
    }

    private async Task<RunView> RecoverJob(string id, CancellationToken cancellationToken)
    {
        var run = store.Get<RunView>(id, "Run");
        Activity.Current?.SetTag("namerre.run.kind", run.Kind);
        Activity.Current?.SetTag("namerre.files.count", run.Files.Count);
        RecoveryPreview evidence;
        using (NamerreTelemetry.Source.StartActivity("rename.recovery.evidence"))
            evidence = await recovery.Build(run, cancellationToken);
        if (!evidence.CanRecover) throw new RenameException("RecoveryBlocked", "Recovery evidence changed or is ambiguous.");
        run = run with { Status = "Recovering" };
        recorder.Save(run);
        var restoring = new List<(RunFile File, string Stage)>();
        foreach (var file in run.Files)
        {
            var located = evidence.Files.Single(f => f.Id == file.Id).LocatedPath!;
            if (fileSystem.SamePath(located, file.SourcePath) && located == file.SourcePath) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var stage = JournaledMover.Staging(located);
            await verifier.Verify(located, file.Hash, cancellationToken, file.Id);
            run = mover.Move(run, file.Id, located, stage, "RecoveryStage");
            restoring.Add((file, stage));
        }
        foreach (var (file, stage) in restoring)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run = mover.EnsureDirectories(run, file.Id, file.SourcePath);
            await verifier.Verify(stage, file.Hash, cancellationToken, file.Id);
            run = mover.Move(run, file.Id, stage, file.SourcePath, "RecoveryRestore");
        }
        run = run with { Status = "Recovered", Error = null,
            Files = run.Files.Select(f => f with { Status = "Restored", Error = null, ErrorCode = null }).ToArray() };
        recorder.Complete(run, "Completed");
        return run;
    }

    private static RunFile FailureFile(RunView run, RunFile file, Exception exception)
    {
        var last = run.Journal.LastOrDefault(j => j.FileId == file.Id && !JournaledMover.IsDirectoryEntry(j.Kind));
        var failedId = (exception as RenameException)?.FileId ?? exception.Data["fileId"] as string;
        var status = last?.Status == "Intent" ? "Uncertain"
            : last?.Kind is "Place" ? "Completed"
            : last?.Kind is "Stage" ? "Staged"
            : last is not null ? "Uncertain" : file.Id == failedId ? "Failed" : "Pending";
        var issue = RenameIssues.Public(exception);
        return file with { Status = status, Error = file.Id == failedId ? issue.Message : file.Error,
            ErrorCode = file.Id == failedId ? issue.Code : file.ErrorCode };
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
}
