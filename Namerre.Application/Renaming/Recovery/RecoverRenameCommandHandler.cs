using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Recovery;

internal sealed class RecoverRenameCommandHandler(IRenameService service)
    : IRequestHandler<RecoverRenameCommand, JobAccepted>
{
    public Task<JobAccepted> Handle(RecoverRenameCommand request, CancellationToken cancellationToken)
        => service.Recover(request.Id, request.Request, cancellationToken);
}
