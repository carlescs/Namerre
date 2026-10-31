using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

internal sealed class UpdateRenamePresetCommandHandler(PresetService presets)
    : IRequestHandler<UpdateRenamePresetCommand, PresetView>
{
    public Task<PresetView> Handle(UpdateRenamePresetCommand request, CancellationToken cancellationToken)
        => Task.FromResult(presets.Update(request.Id, request.Request));
}
