using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Metadata;

public sealed record SuggestTmdbMatchQuery(TmdbSuggestionRequest Request) : IRequest<TmdbSuggestion>;
