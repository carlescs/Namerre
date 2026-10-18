namespace Namerre.Application.Folders;

public interface IFolderBrowser
{
    Task<FolderListing> Browse(string path, CancellationToken cancellationToken);
}
