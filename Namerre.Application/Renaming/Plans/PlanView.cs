namespace Namerre.Application.Renaming.Plans;

public sealed record PlanView(string Id, int Version, string Status, int CompletedFiles, PipelineRequest Pipeline,
    IReadOnlyList<PlanFile> Files, IReadOnlyList<RenameIssue> Issues, DateTimeOffset CreatedAt);
