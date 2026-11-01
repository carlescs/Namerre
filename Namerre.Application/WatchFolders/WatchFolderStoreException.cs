namespace Namerre.Application.WatchFolders;

/// <summary>The watched-file store could not read or write; the scan reports it and tries again next interval.</summary>
public sealed class WatchFolderStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
