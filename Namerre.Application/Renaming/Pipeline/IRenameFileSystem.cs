namespace Namerre.Application.Renaming.Pipeline;

public interface IRenameFileSystem
{
    string RootBinding { get; }
    void Validate(string path);
    bool Exists(string path);
    string ActualPath(string path);
    bool SamePath(string first, string second);
    Task<(string Hash, long Size)> Hash(string path, CancellationToken cancellationToken);
    /// <summary>Cheap size and modification-time fingerprint, prefixed with <c>stat:</c>; reads no file content.</summary>
    (string Hash, long Size) Stamp(string path);
    /// <summary>Size and dates of an existing file; reads no file content.</summary>
    FileFacts Facts(string path);
    void CheckSameDevice(string source, string target);
    void Move(string source, string destination);
    IReadOnlyList<string> MissingDirectories(string path);
    void CreateDirectory(string path);
    bool IsEmptyDirectory(string path);
    /// <summary>Deletes the directory only when it exists and is empty; returns whether it was deleted.</summary>
    bool DeleteEmptyDirectory(string path);
}
