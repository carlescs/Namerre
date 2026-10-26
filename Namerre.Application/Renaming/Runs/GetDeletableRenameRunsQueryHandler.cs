using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

internal sealed class GetDeletableRenameRunsQueryHandler(IRenameService service)
    : IRequestHandler<GetDeletableRenameRunsQuery, DeletableRuns>
{
    public Task<DeletableRuns> Handle(GetDeletableRenameRunsQuery request, CancellationToken cancellationToken)
        => service.GetDeletableRuns(cancellationToken);
}
