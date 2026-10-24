using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

internal sealed class DeleteRenameRunCommandHandler(IRenameService service)
    : IRequestHandler<DeleteRenameRunCommand, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(DeleteRenameRunCommand request, CancellationToken cancellationToken)
        => service.DeleteRun(request.Id, cancellationToken);
}
