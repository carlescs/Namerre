namespace Namerre.Application.Renaming.Previews;

public sealed record PreviewFile(string Id, string SourcePath, string TargetPath, bool Unchanged,
    IReadOnlyList<StepTrace> Steps, IReadOnlyList<RenameIssue> Issues);
