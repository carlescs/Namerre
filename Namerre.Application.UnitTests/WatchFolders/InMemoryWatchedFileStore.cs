using Namerre.Application.WatchFolders;

namespace Namerre.Application.UnitTests.WatchFolders;

/// <summary>A dictionary-backed <see cref="IWatchedFileStore"/> that keeps the tests free of a database.</summary>
internal sealed class InMemoryWatchedFileStore : IWatchedFileStore
{
    private readonly Dictionary<(string Folder, string Path), WatchedFile> rows = [];
    private readonly HashSet<string> paused = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WatchedFile> Rows => rows.Values.ToArray();

    public void Seed(params WatchedFile[] files)
    {
        foreach (var file in files) rows[(file.Folder, file.Path)] = file;
    }

    public WatchedFile? Get(string folder, string path) => rows.GetValueOrDefault((folder, path));

    public IReadOnlyList<WatchedFile> List(string folder) => rows.Values.Where(f => f.Folder == folder).ToArray();

    public IReadOnlyList<WatchedFile> List(string folder, WatchFileState state)
        => List(folder).Where(f => f.State == state).ToArray();

    public int Count(string folder, WatchFileState state) => List(folder, state).Count;

    public IReadOnlyList<WatchedFile> Problems(string folder, int limit)
        => List(folder).Where(f => f.State is WatchFileState.Failed or WatchFileState.Ignored)
            .OrderByDescending(f => f.UpdatedAt).Take(limit).ToArray();

    public void Save(IReadOnlyCollection<WatchedFile> files, IReadOnlyCollection<(string Folder, string Path)>? removals = null)
    {
        foreach (var removal in removals ?? []) rows.Remove(removal);
        foreach (var file in files) rows[(file.Folder, file.Path)] = file;
    }

    public int Remove(string folder, string path, params WatchFileState[] states)
        => rows.TryGetValue((folder, path), out var file) && states.Contains(file.State) && rows.Remove((folder, path)) ? 1 : 0;

    public bool IsPaused(string folder) => paused.Contains(folder);

    public void SetPaused(string folder, bool value)
    {
        if (value) paused.Add(folder);
        else paused.Remove(folder);
    }
}
