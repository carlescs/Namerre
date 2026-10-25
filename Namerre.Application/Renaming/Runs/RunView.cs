using System.Text.Json.Serialization;

namespace Namerre.Application.Renaming.Runs;

/// <param name="RollbackRuns">
/// Rollback runs created from this run, oldest first. Derived on read and never persisted, so it is only
/// populated on single-run reads.
/// </param>
public sealed record RunView(string Id, string PlanId, string? ParentRunId, string Kind, string Status,
    IReadOnlyList<RunFile> Files, IReadOnlyList<JournalEntry> Journal, DateTimeOffset CreatedAt,
    string? Error = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RunReference>? RollbackRuns = null);
