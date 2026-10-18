using CMediator.Abstractions;

namespace Namerre.Application.Folders;

internal sealed class GetFolderQueryHandler(IFolderBrowser browser)
    : IRequestHandler<GetFolderQuery, FolderListing>
{
    public Task<FolderListing> Handle(GetFolderQuery request, CancellationToken cancellationToken)
        => browser.Browse(request.Path, cancellationToken);
}
