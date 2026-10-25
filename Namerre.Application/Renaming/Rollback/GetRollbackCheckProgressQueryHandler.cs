using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Rollback;

internal sealed class GetRollbackCheckProgressQueryHandler(IRenameService service)
    : IRequestHandler<GetRollbackCheckProgressQuery, RollbackCheckProgress>
{
    public Task<RollbackCheckProgress> Handle(GetRollbackCheckProgressQuery request, CancellationToken cancellationToken)
        => service.GetRollbackCheckProgress(request.Id, request.OperationId, cancellationToken);
}
