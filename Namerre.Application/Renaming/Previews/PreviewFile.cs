using Namerre.Application.Renaming.Pipeline;

namespace Namerre.Application.Renaming.Previews;

/// <param name="CompanionOf">ID of the video this related file follows; null for files the steps renamed.</param>
/// <param name="Numbered">The name received a <c> (n)</c> suffix to avoid a collision
/// (<see cref="PipelineRequest.OnCollision"/>); a related file following a numbered video is marked too.</param>
public sealed record PreviewFile(string Id, string SourcePath, string TargetPath, bool Unchanged,
    IReadOnlyList<StepTrace> Steps, IReadOnlyList<RenameIssue> Issues, string? CompanionOf = null,
    bool Numbered = false);
