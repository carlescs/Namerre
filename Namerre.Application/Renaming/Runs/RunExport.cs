namespace Namerre.Application.Renaming.Runs;

/// <summary>A downloadable run history file.</summary>
public sealed record RunExport(string FileName, string ContentType, byte[] Content);
