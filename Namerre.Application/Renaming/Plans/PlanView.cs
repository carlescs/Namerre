namespace Namerre.Application.Renaming.Plans;

/// <param name="Facts">Source file facts by file ID, read once when preparation starts and reused by every
/// step; null on plans prepared before they were recorded.</param>
/// <param name="Origin">What started the plan: null for a person, <c>watch:&lt;name&gt;</c> for a watch folder.</param>
public sealed record PlanView(string Id, int Version, string Status, int CompletedFiles, PipelineRequest Pipeline,
    IReadOnlyList<PlanFile> Files, IReadOnlyList<RenameIssue> Issues, DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, FileFacts>? Facts = null, string? Origin = null);
