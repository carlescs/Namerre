using CMediator.Abstractions;

namespace Namerre.Application.WatchFolders;

public sealed record SetWatchFolderPausedCommand(string Name, bool Paused) : IRequest<WatchFolderStatus>;
