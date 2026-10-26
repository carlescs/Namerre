using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

internal sealed class DeleteRenameRunsCommandHandler(IRenameService service)
    : IRequestHandler<DeleteRenameRunsCommand, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(DeleteRenameRunsCommand request, CancellationToken cancellationToken)
        => service.DeleteRuns(request.Request, cancellationToken);
}
