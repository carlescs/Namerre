using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

internal sealed class GetRenamePresetQueryHandler(PresetService presets) : IRequestHandler<GetRenamePresetQuery, PresetView>
{
    public Task<PresetView> Handle(GetRenamePresetQuery request, CancellationToken cancellationToken)
        => Task.FromResult(presets.Get(request.Id));
}
