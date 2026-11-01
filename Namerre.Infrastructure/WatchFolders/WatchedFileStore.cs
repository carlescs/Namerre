using Microsoft.EntityFrameworkCore;
using Namerre.Application.WatchFolders;
using Namerre.Infrastructure.Renaming;

namespace Namerre.Infrastructure.WatchFolders;

/// <summary>
/// <see cref="IWatchedFileStore"/> over the <c>WatchedFiles</c> and <c>Settings</c> tables. The state is stored by its
/// name (<c>Queued</c>, <c>Done</c>, <c>Failed</c>, <c>Ignored</c>).
/// </summary>
public sealed class WatchedFileStore(RenameStore store) : IWatchedFileStore
{
    public WatchedFile? Get(string folder, string path)
        => Use(db => db.WatchedFiles.AsNoTracking().SingleOrDefault(f => f.Folder == folder && f.Path == path) is { } row
            ? ToFile(row) : null);

    public IReadOnlyList<WatchedFile> List(string folder)
        => Use(db => db.WatchedFiles.AsNoTracking().Where(f => f.Folder == folder).AsEnumerable().Select(ToFile).ToArray());

    public IReadOnlyList<WatchedFile> List(string folder, WatchFileState state)
    {
        var name = state.ToString();
        return Use(db => db.WatchedFiles.AsNoTracking().Where(f => f.Folder == folder && f.Status == name)
            .AsEnumerable().Select(ToFile).ToArray());
    }

    public int Count(string folder, WatchFileState state)
    {
        var name = state.ToString();
        return Use(db => db.WatchedFiles.AsNoTracking().Count(f => f.Folder == folder && f.Status == name));
    }

    public IReadOnlyList<WatchedFile> Problems(string folder, int limit)
    {
        var failed = WatchFileState.Failed.ToString();
        var ignored = WatchFileState.Ignored.ToString();
        return Use(db => db.WatchedFiles.AsNoTracking()
            .Where(f => f.Folder == folder && (f.Status == failed || f.Status == ignored))
            .AsEnumerable().OrderByDescending(f => f.UpdatedAt).Take(limit).Select(ToFile).ToArray());
    }

    public void Save(IReadOnlyCollection<WatchedFile> files, IReadOnlyCollection<(string Folder, string Path)>? removals = null)
        => Use(db =>
        {
            foreach (var (folder, path) in removals ?? [])
                db.WatchedFiles.Where(f => f.Folder == folder && f.Path == path).ExecuteDelete();
            foreach (var file in files)
            {
                var row = db.WatchedFiles.Local.FirstOrDefault(f => f.Folder == file.Folder && f.Path == file.Path)
                    ?? db.WatchedFiles.SingleOrDefault(f => f.Folder == file.Folder && f.Path == file.Path);
                if (row is null)
                {
                    row = new WatchedFileRow { Folder = file.Folder, Path = file.Path };
                    db.WatchedFiles.Add(row);
                }
                row.Size = file.Size;
                row.ModifiedTicks = file.ModifiedTicks;
                row.Status = file.State.ToString();
                row.Attempts = file.Attempts;
                row.NextAttemptAt = file.NextAttemptAt;
                row.Error = file.Error;
                row.PlanId = file.PlanId;
                row.RunId = file.RunId;
                row.UpdatedAt = file.UpdatedAt;
            }
            return db.SaveChanges();
        });

    public int Remove(string folder, string path, params WatchFileState[] states)
    {
        var names = states.Select(state => state.ToString()).ToArray();
        return Use(db => db.WatchedFiles.Where(f => f.Folder == folder && f.Path == path && names.Contains(f.Status))
            .ExecuteDelete());
    }

    public bool IsPaused(string folder)
    {
        var key = PausedKey(folder);
        return Use(db => db.Settings.AsNoTracking().Any(s => s.Key == key));
    }

    public void SetPaused(string folder, bool paused)
        => Use(db =>
        {
            var key = PausedKey(folder);
            var row = db.Settings.SingleOrDefault(s => s.Key == key);
            if (paused && row is null) db.Settings.Add(new RenameSetting { Key = key, Value = "true" });
            if (!paused && row is not null) db.Settings.Remove(row);
            return db.SaveChanges();
        });

    private T Use<T>(Func<RenameDbContext, T> action)
    {
        try { return store.Use(action); }
        catch (DbUpdateException exception)
        {
            throw new WatchFolderStoreException("The watched files could not be saved.", exception);
        }
    }

    private static WatchedFile ToFile(WatchedFileRow row)
        => new(row.Folder, row.Path, row.Size, row.ModifiedTicks, Enum.Parse<WatchFileState>(row.Status), row.Attempts,
            row.NextAttemptAt, row.Error, row.PlanId, row.RunId, row.UpdatedAt);

    private static string PausedKey(string name) => "watch-paused:" + name.ToLowerInvariant();
}
