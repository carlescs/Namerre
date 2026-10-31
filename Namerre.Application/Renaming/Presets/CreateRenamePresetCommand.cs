using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

public sealed record CreateRenamePresetCommand(SavePresetRequest Request) : IRequest<PresetView>;
