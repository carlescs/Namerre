namespace Namerre.Application.WatchFolders;

/// <summary>
/// What a watch folder did with one file. The names are what the <c>WatchedFiles</c> table stores in its
/// <c>Status</c> column, so renaming a member needs a migration.
/// </summary>
public enum WatchFileState
{
    /// <summary>In a prepared plan or a running batch.</summary>
    Queued,
    /// <summary>Renamed, or already named as the preset wants it.</summary>
    Done,
    /// <summary>Failed; retried at <see cref="WatchedFile.NextAttemptAt"/> with back-off.</summary>
    Failed,
    /// <summary>Failed too often; left alone until the file changes or someone retries it.</summary>
    Ignored
}
