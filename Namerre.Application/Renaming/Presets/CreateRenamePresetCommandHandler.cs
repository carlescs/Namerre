using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

internal sealed class CreateRenamePresetCommandHandler(PresetService presets)
    : IRequestHandler<CreateRenamePresetCommand, PresetView>
{
    public Task<PresetView> Handle(CreateRenamePresetCommand request, CancellationToken cancellationToken)
        => Task.FromResult(presets.Create(request.Request));
}
