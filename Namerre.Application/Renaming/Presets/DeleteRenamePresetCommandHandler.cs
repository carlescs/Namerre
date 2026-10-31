using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Presets;

internal sealed class DeleteRenamePresetCommandHandler(PresetService presets)
    : IRequestHandler<DeleteRenamePresetCommand, bool>
{
    public Task<bool> Handle(DeleteRenamePresetCommand request, CancellationToken cancellationToken)
    {
        presets.Delete(request.Id);
        return Task.FromResult(true);
    }
}
