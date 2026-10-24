namespace Namerre.Application.Renaming.Plans;

public sealed record PlanFile(string Id, string SourcePath, string TargetPath, string Hash, long Size);
