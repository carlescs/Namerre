namespace Namerre.Application.WatchFolders;

/// <summary>
/// Persistence of watch-folder progress (watched files and the pause setting). Failures throw
/// <see cref="WatchFolderStoreException"/>.
/// </summary>
public interface IWatchedFileStore
{
    WatchedFile? Get(string folder, string path);
    /// <summary>Every file the folder has a record of.</summary>
    IReadOnlyList<WatchedFile> List(string folder);
    IReadOnlyList<WatchedFile> List(string folder, WatchFileState state);
    int Count(string folder, WatchFileState state);
    /// <summary>The newest <see cref="WatchFileState.Failed"/> and <see cref="WatchFileState.Ignored"/> files, newest first.</summary>
    IReadOnlyList<WatchedFile> Problems(string folder, int limit);
    /// <summary>Removes the given paths, then adds or replaces the files, together.</summary>
    void Save(IReadOnlyCollection<WatchedFile> files, IReadOnlyCollection<(string Folder, string Path)>? removals = null);
    /// <summary>Forgets the file when it is in one of the states; returns how many records were removed.</summary>
    int Remove(string folder, string path, params WatchFileState[] states);
    bool IsPaused(string folder);
    void SetPaused(string folder, bool paused);
}
