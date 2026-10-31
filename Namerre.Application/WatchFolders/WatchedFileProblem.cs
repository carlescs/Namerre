namespace Namerre.Application.WatchFolders;

/// <param name="Status"><c>Failed</c> (retried at <paramref name="NextAttemptAt"/>) or <c>Ignored</c> (until the file changes).</param>
public sealed record WatchedFileProblem(string Path, string Status, int Attempts, string? Error,
    DateTimeOffset? NextAttemptAt, DateTimeOffset UpdatedAt);
