namespace Namerre.Application.Renaming;

/// <summary>A file that follows the video <paramref name="VideoId"/>, keeping <paramref name="Suffix"/>.</summary>
public sealed record Companion(string VideoId, string Suffix);
