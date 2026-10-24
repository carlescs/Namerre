using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

internal sealed class GetRenameRunsQueryHandler(IRenameService service)
    : IRequestHandler<GetRenameRunsQuery, RunPage>
{
    public Task<RunPage> Handle(GetRenameRunsQuery request, CancellationToken cancellationToken)
        => service.GetRuns(request.Page, request.PageSize, cancellationToken);
}
