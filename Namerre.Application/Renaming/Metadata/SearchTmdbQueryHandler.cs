using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Metadata;

internal sealed class SearchTmdbQueryHandler(ITmdbMetadata metadata)
    : IRequestHandler<SearchTmdbQuery, TmdbSearchResult>
{
    public Task<TmdbSearchResult> Handle(SearchTmdbQuery request, CancellationToken cancellationToken)
        => metadata.Search(request.Request, cancellationToken);
}
