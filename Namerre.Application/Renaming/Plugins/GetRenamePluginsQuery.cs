using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Plugins;

public sealed record GetRenamePluginsQuery : IRequest<IReadOnlyList<PluginDescriptor>>;
