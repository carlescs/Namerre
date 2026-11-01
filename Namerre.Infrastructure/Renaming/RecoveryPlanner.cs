using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Runs;

namespace Namerre.Infrastructure.Renaming;

/// <summary>
/// Locates each file of an interrupted run from its journaled paths and decides whether every file can be
/// restored to its pre-operation location without ambiguity.
/// </summary>
internal sealed class RecoveryPlanner(IRenameFileSystem fileSystem, FileVerifier verifier)
{
    public async Task<RecoveryPreview> Build(RunView run, CancellationToken cancellationToken)
    {
        var files = new List<RecoveryFile>();
        foreach (var file in run.Files)
        {
            var candidates = new List<string>();
            var issues = new List<RenameIssue>();
            var paths = new[] { file.SourcePath, file.StagingPath, file.TargetPath }
                .Concat(run.Journal.Where(j => j.FileId == file.Id && !JournaledMover.IsDirectoryEntry(j.Kind))
                    .SelectMany(j => new[] { j.FromPath, j.ToPath })).Where(p => p.Length > 0).Distinct().ToArray();
            var unique = new List<string>();
            foreach (var path in paths)
                if (!unique.Any(p => fileSystem.SamePath(p, path))) unique.Add(path);
            foreach (var path in unique)
            {
                try
                {
                    if (!fileSystem.Exists(path)) continue;
                    if (await verifier.Fingerprint(path, file.Hash, cancellationToken) == file.Hash)
                    {
                        var actual = fileSystem.ActualPath(path);
                        if (!candidates.Contains(actual, StringComparer.Ordinal)) candidates.Add(actual);
                    }
                }
                catch (Exception exception) when (exception is RenameException or IOException or UnauthorizedAccessException)
                {
                    issues.Add(RenameIssues.Public(exception));
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
            catch (RenameException exception) { issues.Add(RenameIssues.Public(exception)); }
            files[i] = item with { Issues = issues };
        }
        return new(run.Id, run.Status is "RecoveryRequired" or "RecoveryQueued" or "Recovering"
            && files.All(f => f.Issues.Count == 0), "RestoreOriginal", files);
    }
}
