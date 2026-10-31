using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

public sealed record ListRenamePresetsQuery : IRequest<IReadOnlyList<PresetSummary>>;
