namespace Namerre.Application.Renaming.Runs;

public sealed record RunView(string Id, string PlanId, string? ParentRunId, string Kind, string Status,
    IReadOnlyList<RunFile> Files, IReadOnlyList<JournalEntry> Journal, DateTimeOffset CreatedAt,
    string? Error = null);
