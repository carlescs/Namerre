using CMediator.Abstractions;

namespace Namerre.Application.WatchFolders;

internal sealed class GetWatchFoldersQueryHandler(IWatchFolders folders)
    : IRequestHandler<GetWatchFoldersQuery, IReadOnlyList<WatchFolderStatus>>
{
    public Task<IReadOnlyList<WatchFolderStatus>> Handle(GetWatchFoldersQuery request, CancellationToken cancellationToken)
        => Task.FromResult(folders.List());
}
