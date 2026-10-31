using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

/// <summary>Changes only a preset's name.</summary>
public sealed record RenameRenamePresetCommand(string Id, RenamePresetRequest Request) : IRequest<PresetView>;
