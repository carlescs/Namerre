using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

internal sealed class GetRenameRunQueryHandler(IRenameService service)
    : IRequestHandler<GetRenameRunQuery, RunView>
{
    public Task<RunView> Handle(GetRenameRunQuery request, CancellationToken cancellationToken)
        => service.GetRun(request.Id, cancellationToken);
}
