namespace Namerre.Application.WatchFolders;

/// <summary>Watch folder state for the API. Unknown folder names are <c>NotFound</c> (404).</summary>
public interface IWatchFolders
{
    /// <summary>Scans every folder whose interval has passed; the worker calls this every few seconds.</summary>
    Task Scan(CancellationToken cancellationToken);
    IReadOnlyList<WatchFolderStatus> List();
    WatchFolderStatus SetPaused(string name, bool paused);
    /// <summary>Forgets a failed or ignored file so the next scan takes it again.</summary>
    WatchFolderStatus Retry(string name, string path);
}
