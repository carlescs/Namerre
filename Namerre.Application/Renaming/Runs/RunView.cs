using System.Text.Json.Serialization;
using Namerre.Application.Authentication;

namespace Namerre.Application.Renaming.Runs;

/// <param name="RollbackRuns">
/// Rollback runs created from this run, oldest first. Derived on read and never persisted, so it is only
/// populated on single-run reads.
/// </param>
/// <param name="DeleteEmptyFolders">
/// Snapshot of the plan option: after every file is placed, remove folders that the moves left empty.
/// </param>
/// <param name="Origin">The plan's origin (see <see cref="Plans.PlanView.Origin"/>); rollbacks are started by a person.</param>
/// <param name="CreatedBy">The user who started the run (executed the plan or requested the rollback); null for
/// watch folders and runs from before users.</param>
/// <param name="RecoveredBy">The user who last requested recovery of this run.</param>
public sealed record RunView(string Id, string PlanId, string? ParentRunId, string Kind, string Status,
    IReadOnlyList<RunFile> Files, IReadOnlyList<JournalEntry> Journal, DateTimeOffset CreatedAt,
    string? Error = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RunReference>? RollbackRuns = null,
    bool DeleteEmptyFolders = false, string? Origin = null, UserReference? CreatedBy = null,
    UserReference? RecoveredBy = null);
