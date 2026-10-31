using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

public sealed record GetRenamePresetQuery(string Id) : IRequest<PresetView>;
