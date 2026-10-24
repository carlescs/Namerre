using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Plans;

internal sealed class ExecuteRenameCommandHandler(IRenameService service)
    : IRequestHandler<ExecuteRenameCommand, JobAccepted>
{
    public Task<JobAccepted> Handle(ExecuteRenameCommand request, CancellationToken cancellationToken)
        => service.Execute(request.Id, request.Request, cancellationToken);
}
