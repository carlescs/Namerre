using CMediator.Abstractions;

namespace Namerre.Application.WatchFolders;

internal sealed class RetryWatchedFileCommandHandler(IWatchFolders folders)
    : IRequestHandler<RetryWatchedFileCommand, WatchFolderStatus>
{
    public Task<WatchFolderStatus> Handle(RetryWatchedFileCommand request, CancellationToken cancellationToken)
        => Task.FromResult(folders.Retry(request.Name, request.Request?.Path ?? ""));
}
