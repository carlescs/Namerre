namespace Namerre.Application.Renaming.Plugins;

/// <summary>Where a file sits in the request, counting every file whether or not it has issues.</summary>
/// <param name="Index">Zero-based position in request order.</param>
/// <param name="Count">Number of files in the request.</param>
/// <param name="FolderIndex">Zero-based position among the files that share this file's source folder.</param>
/// <param name="FolderCount">Number of files in the request from this file's source folder.</param>
public sealed record FilePosition(int Index, int Count, int FolderIndex, int FolderCount);
