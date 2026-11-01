using CMediator.Abstractions;
using Namerre.Application.Renaming.Pipeline;

namespace Namerre.Application.Renaming.Plugins;

internal sealed class GetRenamePluginsQueryHandler(PipelineEngine pipeline)
    : IRequestHandler<GetRenamePluginsQuery, IReadOnlyList<PluginDescriptor>>
{
    public Task<IReadOnlyList<PluginDescriptor>> Handle(GetRenamePluginsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(pipeline.Catalog);
}
