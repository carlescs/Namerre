using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Metadata;

public sealed record SearchTmdbQuery(TmdbSearchRequest Request) : IRequest<TmdbSearchResult>;
