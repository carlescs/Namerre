using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Recovery;

internal sealed class PreviewRecoveryQueryHandler(IRenameService service)
    : IRequestHandler<PreviewRecoveryQuery, RecoveryPreview>
{
    public Task<RecoveryPreview> Handle(PreviewRecoveryQuery request, CancellationToken cancellationToken)
        => service.PreviewRecovery(request.Id, cancellationToken);
}
