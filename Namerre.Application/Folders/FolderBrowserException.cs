namespace Namerre.Application.Folders;

public enum FolderBrowserError
{
    InvalidPath,
    Forbidden,
    NotFound,
    Unavailable
}

public sealed class FolderBrowserException(
    FolderBrowserError error,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public FolderBrowserError Error { get; } = error;
}
