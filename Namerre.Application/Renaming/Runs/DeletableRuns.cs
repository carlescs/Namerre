namespace Namerre.Application.Renaming.Runs;

/// <summary>Settled run IDs, newest first; <see cref="HasMore"/> means more exist than one deletion accepts.</summary>
public sealed record DeletableRuns(IReadOnlyList<string> Ids, bool HasMore);
