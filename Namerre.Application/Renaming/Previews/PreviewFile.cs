namespace Namerre.Application.Renaming.Previews;

/// <param name="CompanionOf">ID of the video this related file follows; null for files the steps renamed.</param>
public sealed record PreviewFile(string Id, string SourcePath, string TargetPath, bool Unchanged,
    IReadOnlyList<StepTrace> Steps, IReadOnlyList<RenameIssue> Issues, string? CompanionOf = null);
