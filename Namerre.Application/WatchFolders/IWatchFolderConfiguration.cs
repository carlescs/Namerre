namespace Namerre.Application.WatchFolders;

/// <summary>The configured watch folders (<c>Namerre:WatchFolders</c>), already validated.</summary>
public interface IWatchFolderConfiguration
{
    IReadOnlyList<WatchFolderSettings> Folders { get; }
}
