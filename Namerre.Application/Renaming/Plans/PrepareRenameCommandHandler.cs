using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Plans;

internal sealed class PrepareRenameCommandHandler(IRenameService service)
    : IRequestHandler<PrepareRenameCommand, JobAccepted>
{
    public Task<JobAccepted> Handle(PrepareRenameCommand request, CancellationToken cancellationToken)
        => service.Prepare(request.Pipeline, cancellationToken);
}
