namespace Namerre.Application.Renaming.Runs;

/// <param name="CompanionOf">ID of the video this related file followed when renamed, if any.</param>
public sealed record RunFile(string Id, string SourcePath, string TargetPath, string StagingPath, string Hash,
    string Status, string? Error = null, string? ErrorCode = null, string? CompanionOf = null);
