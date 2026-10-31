using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Metadata;

internal sealed class SuggestTmdbMatchQueryHandler(TmdbSuggester suggester)
    : IRequestHandler<SuggestTmdbMatchQuery, TmdbSuggestion>
{
    public Task<TmdbSuggestion> Handle(SuggestTmdbMatchQuery request, CancellationToken cancellationToken)
        => suggester.Suggest(request.Request, cancellationToken);
}
