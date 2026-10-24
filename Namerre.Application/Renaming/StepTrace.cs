namespace Namerre.Application.Renaming;

public sealed record StepTrace(string InstanceId, string TypeId, string InputPath, string? OutputPath,
    RenameIssue? Error);
