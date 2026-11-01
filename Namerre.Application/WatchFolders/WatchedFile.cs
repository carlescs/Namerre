namespace Namerre.Application.WatchFolders;

/// <summary>
/// What a watch folder knows about one file, keyed by folder name and root-relative path and fingerprinted by size and
/// modification time (<see cref="ModifiedTicks"/> are UTC ticks), so a changed file is taken again.
/// </summary>
public sealed record WatchedFile(string Folder, string Path, long Size, long ModifiedTicks, WatchFileState State,
    int Attempts = 0, DateTimeOffset? NextAttemptAt = null, string? Error = null, string? PlanId = null,
    string? RunId = null, DateTimeOffset UpdatedAt = default)
{
    /// <summary>Whether the file still has the given size and modification time.</summary>
    public bool Matches(long size, long modifiedTicks) => Size == size && ModifiedTicks == modifiedTicks;
}
