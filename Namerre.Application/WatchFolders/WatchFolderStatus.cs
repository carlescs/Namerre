namespace Namerre.Application.WatchFolders;

/// <param name="InFlight">Files of a prepared or running batch.</param>
/// <param name="Error">Why the folder cannot run, e.g. a missing preset; null while it works.</param>
/// <param name="Problems">Files that failed (retried with back-off) or are ignored until they change, newest first.</param>
public sealed record WatchFolderStatus(string Name, string Path, string Preset, bool Paused, DateTimeOffset? LastScanAt,
    int InFlight, int Completed, string? Error, IReadOnlyList<WatchedFileProblem> Problems,
    double? TmdbAutoConfirm = null);
