using CMediator.Abstractions;

namespace Namerre.Application.Folders;

internal sealed class GetFolderSelectionQueryHandler(IFolderBrowser browser)
    : IRequestHandler<GetFolderSelectionQuery, IReadOnlyList<FolderListing>>
{
    public async Task<IReadOnlyList<FolderListing>> Handle(
        GetFolderSelectionQuery request, CancellationToken cancellationToken)
    {
        var folders = new List<FolderListing>();
        var paths = new Queue<string>();
        paths.Enqueue(request.Path);

        while (paths.TryDequeue(out var path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = await browser.Browse(path, cancellationToken);
            folders.Add(folder);
            if (request.IncludeSubfolders)
            {
                foreach (var directory in folder.Directories)
                {
                    paths.Enqueue(directory.Path);
                }
            }
        }

        return folders;
    }
}
