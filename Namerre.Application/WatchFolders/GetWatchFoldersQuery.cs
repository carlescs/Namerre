using CMediator.Abstractions;

namespace Namerre.Application.WatchFolders;

public sealed record GetWatchFoldersQuery : IRequest<IReadOnlyList<WatchFolderStatus>>;
