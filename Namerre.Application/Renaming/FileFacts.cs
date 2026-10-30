namespace Namerre.Application.Renaming;

/// <summary>Filesystem details of a source file, read once per preview and snapshotted into plans.</summary>
/// <param name="CreatedUtc">Creation (birth) time; null when the filesystem reports none. On Unix filesystems
/// without birth times .NET reports the earliest status-change or modification time instead.</param>
public sealed record FileFacts(long Size, DateTimeOffset ModifiedUtc, DateTimeOffset? CreatedUtc);
