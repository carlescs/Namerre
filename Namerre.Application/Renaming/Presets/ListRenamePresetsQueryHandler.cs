using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

internal sealed class ListRenamePresetsQueryHandler(PresetService presets)
    : IRequestHandler<ListRenamePresetsQuery, IReadOnlyList<PresetSummary>>
{
    public Task<IReadOnlyList<PresetSummary>> Handle(ListRenamePresetsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(presets.List());
}
