using Microsoft.Extensions.Logging;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Runs;

namespace Namerre.Infrastructure.Renaming;

/// <summary>
/// Durable plan and run writes, each followed by the change notification. Notifications run after the durable
/// write; a failed push only delays clients until their fallback refresh.
/// </summary>
internal sealed class RenameRecorder(RenameStore store, ILogger logger, IRenameNotifier? notifier,
    ILibraryNotifier? libraryNotifier)
{
    public void Save(RunView run)
    {
        store.Save(run.Id, "Run", run.Status, run);
        Notify("Run", run.Id, run.Status);
    }

    public void Save(PlanView plan)
    {
        store.Save(plan.Id, "Plan", plan.Status, plan);
        Notify("Plan", plan.Id, plan.Status);
    }

    public void Complete(RunView run, string parentFileStatus)
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

    // Only the record's owner and administrators are told; see RenameStore.OwnerOf.
    public void Notify(string kind, string id, string status)
    {
        if (notifier is null) return;
        long? owner;
        try { owner = store.OwnerOf(id); }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Rename change notification failed: {Kind} {Id}", kind, id);
            return;
        }
        Notify(kind, id, status, owner);
    }

    public void Notify(string kind, string id, string status, long? owner)
    {
        if (notifier is null) return;
        try { notifier.Changed(new(kind, id, status, owner)); }
        catch (Exception exception) { logger.LogWarning(exception, "Rename change notification failed: {Kind} {Id}", kind, id); }
    }

    // Runs after the durable write; the notifier only queues the work, so this never blocks the job.
    private void NotifyLibraries(RunView run)
    {
        if (libraryNotifier is null || run.Status is not ("Completed" or "Recovered")) return;
        try { libraryNotifier.RunFinished(run); }
        catch (Exception exception) { logger.LogWarning(exception, "Library notification failed: Run {Id}", run.Id); }
    }
}
