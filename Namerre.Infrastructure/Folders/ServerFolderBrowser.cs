using Namerre.Application.Folders;

namespace Namerre.Infrastructure.Folders;

public sealed class ServerFolderBrowser(string? rootPath, string basePath, Func<string, bool>? isOwnedEntry = null) : IFolderBrowser
{
    public Task<FolderListing> Browse(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetRoot();
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(path) || path.StartsWith('\\') || path.Contains(':')
            || segments.Any(segment => segment is "." or ".."
                || (OperatingSystem.IsWindows() && segment.TrimEnd(' ', '.') != segment)
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new FolderBrowserException(
                FolderBrowserError.InvalidPath, "Choose a folder within the configured root.");
        }

        try
        {
            var current = root;
            foreach (var segment in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current = Path.Combine(current, segment);
                EnsureDirectory(current);
            }

            var directories = new List<FolderEntry>();
            var files = new List<FileEntry>();
            var relativePath = string.Join('/', segments);
            foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Links and junctions are intentionally excluded from the browser.
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }
                if (isOwnedEntry?.Invoke(entry.FullName) == true) continue;

                if (entry is DirectoryInfo)
                {
                    var childPath = relativePath.Length == 0
                        ? entry.Name
                        : $"{relativePath}/{entry.Name}";
                    directories.Add(new FolderEntry(entry.Name, childPath));
                }
                else if (entry is FileInfo file)
                {
                    files.Add(new FileEntry(file.Name, file.Length, file.LastWriteTimeUtc));
                }
            }

            return Task.FromResult(new FolderListing(
                relativePath,
                segments.Length == 0 ? null : string.Join('/', segments[..^1]),
                directories.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                files.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray()));
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new FolderBrowserException(
                FolderBrowserError.NotFound, "The selected folder no longer exists.", exception);
        }
        catch (FileNotFoundException exception)
        {
            throw new FolderBrowserException(
                FolderBrowserError.NotFound, "The folder contents changed. Select the folder again.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new FolderBrowserException(
                FolderBrowserError.Forbidden, "The server cannot access this folder.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new FolderBrowserException(
                FolderBrowserError.InvalidPath, "The folder path is invalid.", exception);
        }
        catch (IOException exception)
        {
            throw new FolderBrowserException(
                FolderBrowserError.Unavailable, "The folder could not be read. Try again.", exception);
        }
    }

    private string GetRoot()
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new FolderBrowserException(
                FolderBrowserError.Unavailable,
                "Server folder browsing is not configured. Set Namerre:RootPath on the server.");
        }

        try
        {
            var root = Path.GetFullPath(rootPath, basePath);
            Namerre.Infrastructure.Renaming.BoundedFileSystem.CheckAncestors(root);
            EnsureDirectory(root);
            return root;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or FolderBrowserException or Namerre.Application.Renaming.RenameException)
        {
            throw new FolderBrowserException(
                FolderBrowserError.Unavailable,
                "The configured server folder is unavailable. Contact the server administrator.",
                exception);
        }
    }

    private static void EnsureDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new FolderBrowserException(
                FolderBrowserError.Forbidden, "Symbolic links and junctions cannot be browsed.");
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new FolderBrowserException(
                FolderBrowserError.NotFound, "The selected path is not a folder.");
        }
    }
}
