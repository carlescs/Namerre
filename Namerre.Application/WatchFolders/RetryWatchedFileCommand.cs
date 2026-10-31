using CMediator.Abstractions;

namespace Namerre.Application.WatchFolders;

public sealed record RetryWatchedFileCommand(string Name, RetryWatchedFileRequest Request) : IRequest<WatchFolderStatus>;
