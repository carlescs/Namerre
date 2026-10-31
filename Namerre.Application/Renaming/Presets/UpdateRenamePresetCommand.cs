using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

public sealed record UpdateRenamePresetCommand(string Id, SavePresetRequest Request) : IRequest<PresetView>;
