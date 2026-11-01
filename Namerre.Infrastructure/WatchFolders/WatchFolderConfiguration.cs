using Microsoft.Extensions.Options;
using Namerre.Application.WatchFolders;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Infrastructure.WatchFolders;

/// <summary><see cref="IWatchFolderConfiguration"/> over <c>Namerre:WatchFolders</c>.</summary>
public sealed class WatchFolderConfiguration(IOptions<NamerreOptions> options) : IWatchFolderConfiguration
{
    public IReadOnlyList<WatchFolderSettings> Folders
        => options.Value.WatchFolders.Select(folder => folder.ToSettings()).ToArray();
}
