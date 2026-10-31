using CMediator.Abstractions;

namespace Namerre.Application.WatchFolders;

internal sealed class SetWatchFolderPausedCommandHandler(IWatchFolders folders)
    : IRequestHandler<SetWatchFolderPausedCommand, WatchFolderStatus>
{
    public Task<WatchFolderStatus> Handle(SetWatchFolderPausedCommand request, CancellationToken cancellationToken)
        => Task.FromResult(folders.SetPaused(request.Name, request.Paused));
}
