using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Rollback;

internal sealed class RollbackRenameCommandHandler(IRenameService service)
    : IRequestHandler<RollbackRenameCommand, JobAccepted>
{
    public Task<JobAccepted> Handle(RollbackRenameCommand request, CancellationToken cancellationToken)
        => service.Rollback(request.Id, request.Selection, cancellationToken);
}
