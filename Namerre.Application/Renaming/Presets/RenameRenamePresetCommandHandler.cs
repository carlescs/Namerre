using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

internal sealed class RenameRenamePresetCommandHandler(PresetService presets)
    : IRequestHandler<RenameRenamePresetCommand, PresetView>
{
    public Task<PresetView> Handle(RenameRenamePresetCommand request, CancellationToken cancellationToken)
        => Task.FromResult(presets.Rename(request.Id, request.Request));
}
