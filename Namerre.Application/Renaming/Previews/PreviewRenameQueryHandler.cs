using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Previews;

internal sealed class PreviewRenameQueryHandler(IRenameService service)
    : IRequestHandler<PreviewRenameQuery, PreviewResult>
{
    public Task<PreviewResult> Handle(PreviewRenameQuery request, CancellationToken cancellationToken)
        => service.Preview(request.Pipeline, cancellationToken);
}
