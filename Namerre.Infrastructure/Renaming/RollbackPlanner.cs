using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;

namespace Namerre.Infrastructure.Renaming;

/// <summary>Decides which selected files of a completed rename can safely move back to their original paths.</summary>
internal sealed class RollbackPlanner(RenameStore store, IRenameFileSystem fileSystem, FileVerifier verifier)
{
    public async Task<SelectionPreview> Build(RunView run, IReadOnlyList<string> ids, CancellationToken cancellationToken,
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
                    await verifier.Verify(file.TargetPath, file.Hash, cancellationToken);
                    fileSystem.Validate(file.SourcePath);
                    fileSystem.CheckSameDevice(file.TargetPath, file.SourcePath);
                    if (file.SourcePath != file.TargetPath && fileSystem.Exists(file.SourcePath)
                        && !run.Files.Any(other => selected.Contains(other.Id) && other.Status == "Completed"
                            && other.SourcePath != other.TargetPath && fileSystem.SamePath(other.TargetPath, file.SourcePath)))
                        issues.Add(new("RollbackDestinationOccupied", "The original path is occupied. Select all dependent swap/cycle files if applicable."));
                }
                catch (Exception exception) when (exception is RenameException or IOException or UnauthorizedAccessException)
                {
                    issues.Add(RenameIssues.Public(exception));
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
}
