namespace Namerre.Application.Renaming.Plans;

/// <param name="CompanionOf">ID of the video this related file follows, if any.</param>
public sealed record PlanFile(string Id, string SourcePath, string TargetPath, string Hash, long Size,
    string? CompanionOf = null);
