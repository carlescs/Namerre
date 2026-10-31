namespace Namerre.Application.Renaming;

/// <param name="Skipped">The step's condition did not match this file, so the path passed through unchanged.</param>
public sealed record StepTrace(string InstanceId, string TypeId, string InputPath, string? OutputPath,
    RenameIssue? Error, bool Skipped = false);
