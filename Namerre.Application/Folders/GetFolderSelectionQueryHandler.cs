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
                // Hidden folders are pruned before browsing, so an unreadable one such as
                // "System Volume Information" cannot fail a selection that would skip it anyway.
                foreach (var directory in folder.Directories)
                {
                    if (request.IncludeHidden || !HiddenEntries.IsHidden(directory.Name))
                    {
                        paths.Enqueue(directory.Path);
                    }
                }
            }
        }

        return folders;
    }
}
