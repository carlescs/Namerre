using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

public sealed record DeleteRenamePresetCommand(string Id) : IRequest<bool>;
