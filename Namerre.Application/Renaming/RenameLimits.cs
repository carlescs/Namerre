namespace Namerre.Application.Renaming;

/// <summary>
/// Workload limits enforced on every request and job. The defaults are the historic fixed values; configuration
/// (<c>Namerre:Limits</c>) may change them within the <c>Ceiling</c> constants, which keep a single run's journal,
/// hashing and plan snapshots bounded.
/// </summary>
public sealed record RenameLimits(int MaxFilesPerRun = RenameLimits.DefaultFiles,
    int MaxStepsPerRun = RenameLimits.DefaultSteps, int MaxQueuedJobs = RenameLimits.DefaultQueuedJobs)
{
    public const int DefaultFiles = 200;
    public const int DefaultSteps = 32;
    public const int DefaultQueuedJobs = 32;
    public const int CeilingFiles = 2000;
    public const int CeilingSteps = 64;
    public const int CeilingQueuedJobs = 256;

    public static RenameLimits Default { get; } = new();

    /// <summary>Whether every limit is between 1 and its ceiling.</summary>
    public bool IsValid => MaxFilesPerRun is >= 1 and <= CeilingFiles && MaxStepsPerRun is >= 1 and <= CeilingSteps
        && MaxQueuedJobs is >= 1 and <= CeilingQueuedJobs;
}
