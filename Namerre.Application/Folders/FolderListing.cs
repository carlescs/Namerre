namespace Namerre.Application.Folders;

public sealed record FolderListing(
    string Path,
    string? ParentPath,
    IReadOnlyList<FolderEntry> Directories,
    IReadOnlyList<FileEntry> Files);

public sealed record FolderEntry(string Name, string Path);

public sealed record FileEntry(string Name, long Length, DateTime LastModifiedUtc);
