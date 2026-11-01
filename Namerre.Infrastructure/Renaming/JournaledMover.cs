using Namerre.Application.Renaming.Pipeline;
using Microsoft.Extensions.Logging;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Runs;

namespace Namerre.Infrastructure.Renaming;

/// <summary>
/// The journaled move engine: every filesystem change is recorded as an intent before it happens and marked
/// completed after, so an interrupted run leaves evidence for recovery.
/// </summary>
internal sealed class JournaledMover(IRenameFileSystem fileSystem, RenameRecorder recorder, ILogger logger)
{
    public RunView Move(RunView run, string fileId, string source, string destination, string kind)
    {
        var sequence = NextSequence(run);
        run = run with { Journal = run.Journal.Append(new JournalEntry(sequence, kind, fileId, source, destination, "Intent")).ToArray() };
        recorder.Save(run);
        try { fileSystem.Move(source, destination); }
        catch (Exception exception)
        {
            exception.Data["fileId"] = fileId;
            throw;
        }
        return CompleteJournal(run, sequence);
    }

    public RunView EnsureDirectories(RunView run, string fileId, string target)
    {
        foreach (var directory in fileSystem.MissingDirectories(target))
        {
            var sequence = NextSequence(run);
            var intent = new JournalEntry(sequence, "CreateDirectory", fileId, "", directory, "Intent");
            run = run with { Journal = run.Journal.Append(intent).ToArray() };
            recorder.Save(run);
            fileSystem.CreateDirectory(directory);
            run = CompleteJournal(run, sequence);
        }
        return run;
    }

    // Runs once every file is placed. Each folder is checked deepest first, so a parent emptied by removing its
    // only child folder goes too; the root itself is never a candidate. Cleanup is best effort: a folder that is
    // not empty or cannot be deleted stays, and the run still completes because every file reached its target.
    public RunView DeleteEmptyFolders(RunView run, IReadOnlyList<RunFile> moved)
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
            recorder.Save(run);
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

    public RunView ChangeFile(RunView run, string fileId, string status)
    {
        run = run with { Files = run.Files.Select(f => f.Id == fileId ? f with { Status = status } : f).ToArray() };
        recorder.Save(run);
        return run;
    }

    private RunView CompleteJournal(RunView run, long sequence, string status = "Completed")
    {
        run = run with { Journal = run.Journal.Select(j => j.Sequence == sequence ? j with { Status = status } : j).ToArray() };
        recorder.Save(run);
        return run;
    }

    /// <summary>A fresh, reserved staging path in the same folder as <paramref name="source"/>.</summary>
    public static string Staging(string source) => (source.Contains('/') ? source[..(source.LastIndexOf('/') + 1)] : "")
        + ".namerre-stage-" + Guid.NewGuid().ToString("N");

    /// <summary>Folder journal entries record directory changes, never file locations, so recovery ignores them.</summary>
    public static bool IsDirectoryEntry(string kind) => kind is "CreateDirectory" or "DeleteDirectory";

    private static long NextSequence(RunView run) => run.Journal.Count == 0 ? 1 : run.Journal.Max(j => j.Sequence) + 1;

    private static IEnumerable<string> Ancestors(string path)
    {
        for (var end = path.LastIndexOf('/'); end > 0; end = path.LastIndexOf('/', end - 1))
            yield return path[..end];
    }
}
