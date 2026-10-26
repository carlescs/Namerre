namespace Namerre.Application.Renaming.Runs;

public sealed record DeleteRunsRequest(IReadOnlyList<string> RunIds);
