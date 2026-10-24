namespace Namerre.Application.Renaming.Runs;

public sealed record RunFile(string Id, string SourcePath, string TargetPath, string StagingPath, string Hash,
    string Status, string? Error = null, string? ErrorCode = null);
