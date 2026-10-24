using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Plugins;

internal sealed class GetRenamePluginsQueryHandler
    : IRequestHandler<GetRenamePluginsQuery, IReadOnlyList<PluginDescriptor>>
{
    public Task<IReadOnlyList<PluginDescriptor>> Handle(GetRenamePluginsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(PipelineEngine.Catalog);
}
