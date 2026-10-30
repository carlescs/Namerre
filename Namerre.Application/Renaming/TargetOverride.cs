namespace Namerre.Application.Renaming;

/// <summary>A target path typed by the user that replaces the pipeline's result for one file.</summary>
public sealed record TargetOverride(string FileId, string TargetPath);
