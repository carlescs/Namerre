using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Rollback;

internal sealed class PreviewRollbackQueryHandler(IRenameService service)
    : IRequestHandler<PreviewRollbackQuery, SelectionPreview>
{
    public Task<SelectionPreview> Handle(PreviewRollbackQuery request, CancellationToken cancellationToken)
        => service.RollbackPreview(request.Id, request.Selection, cancellationToken);
}
