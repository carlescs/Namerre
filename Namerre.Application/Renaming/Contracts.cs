using System.Text.Json;

namespace Namerre.Application.Renaming;

public sealed record PluginInstance(string InstanceId, string TypeId, int Version, bool Enabled, JsonElement Options);
public sealed record FileInput(string Id, string Path);
public sealed record MetadataSelection(string FileId, string InstanceId, string InputPath, string OptionsHash,
    string ConfirmationToken);
public sealed record PipelineRequest(IReadOnlyList<FileInput> Files, IReadOnlyList<PluginInstance> Plugins,
    IReadOnlyList<MetadataSelection>? Selections = null);
public sealed record PluginDescriptor(string TypeId, int Version, string Name, object DefaultOptions);
public sealed record RenameIssue(string Code, string Message);
public sealed record StepTrace(string InstanceId, string TypeId, string InputPath, string? OutputPath,
    RenameIssue? Error);
public sealed record PreviewFile(string Id, string SourcePath, string TargetPath, bool Unchanged,
    IReadOnlyList<StepTrace> Steps, IReadOnlyList<RenameIssue> Issues);
public sealed record PreviewResult(IReadOnlyList<PreviewFile> Files, bool CanPrepare);
public sealed record ExecuteRequest(int Version, string OperationId);
public sealed record SelectionRequest(IReadOnlyList<string> FileIds, string OperationId);
public sealed record RecoveryRequest(string Action, string OperationId);
public sealed record JobAccepted(string Id, string Status, string Location);
public sealed record PlanFile(string Id, string SourcePath, string TargetPath, string Hash, long Size);
public sealed record PlanView(string Id, int Version, string Status, int CompletedFiles, PipelineRequest Pipeline,
    IReadOnlyList<PlanFile> Files, IReadOnlyList<RenameIssue> Issues, DateTimeOffset CreatedAt);
public sealed record RunFile(string Id, string SourcePath, string TargetPath, string StagingPath, string Hash,
    string Status, string? Error = null, string? ErrorCode = null);
public sealed record JournalEntry(long Sequence, string Kind, string FileId, string FromPath, string ToPath,
    string Status);
public sealed record RunView(string Id, string PlanId, string? ParentRunId, string Kind, string Status,
    IReadOnlyList<RunFile> Files, IReadOnlyList<JournalEntry> Journal, DateTimeOffset CreatedAt,
    string? Error = null);
public sealed record RunPage(IReadOnlyList<RunView> Items, int Page, int PageSize, bool HasMore);
public sealed record SelectionFile(string Id, bool Safe, string CurrentPath, string DestinationPath,
    IReadOnlyList<RenameIssue> Issues);
public sealed record SelectionPreview(IReadOnlyList<SelectionFile> Files);
public sealed record RecoveryFile(string Id, string? LocatedPath, string State, IReadOnlyList<RenameIssue> Issues);
public sealed record RecoveryPreview(string RunId, bool CanRecover, string Action, IReadOnlyList<RecoveryFile> Files);
public sealed record TmdbSearchRequest(string MediaType, string Query, string Language = "en-US",
    int? ShowId = null, int? Season = null, int? Episode = null, int? Id = null,
    string? FileId = null, string? InstanceId = null, string? InputPath = null, string? OptionsHash = null);
public sealed record TmdbMatch(int Id, string MediaType, string Title, string? Year, int? Season,
    int? Episode, string? EpisodeTitle, string? ConfirmationToken = null);
public sealed record TmdbSearchResult(IReadOnlyList<TmdbMatch> Matches);

public sealed class RenameException(string code, string message, int status = 400, string? fileId = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public string? FileId { get; } = fileId;
}

public interface IRenameService
{
    Task<PreviewResult> Preview(PipelineRequest request, CancellationToken cancellationToken);
    Task<JobAccepted> Prepare(PipelineRequest request, CancellationToken cancellationToken);
    Task<PlanView> GetPlan(string id, CancellationToken cancellationToken);
    Task<JobAccepted> Execute(string id, ExecuteRequest request, CancellationToken cancellationToken);
    Task<RunPage> GetRuns(int page, int pageSize, CancellationToken cancellationToken);
    Task<RunView> GetRun(string id, CancellationToken cancellationToken);
    Task<SelectionPreview> RollbackPreview(string id, SelectionRequest request, CancellationToken cancellationToken);
    Task<JobAccepted> Rollback(string id, SelectionRequest request, CancellationToken cancellationToken);
    Task<RecoveryPreview> PreviewRecovery(string id, CancellationToken cancellationToken);
    Task<JobAccepted> Recover(string id, RecoveryRequest request, CancellationToken cancellationToken);
}

public interface ITmdbMetadata
{
    Task<TmdbSearchResult> Search(TmdbSearchRequest request, CancellationToken cancellationToken);
    TmdbMatch Resolve(MetadataSelection selection);
}
