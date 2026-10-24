using Namerre.Application.Renaming.Metadata;

namespace Namerre.Application.Renaming;

public interface ITmdbMetadata
{
    Task<TmdbSearchResult> Search(TmdbSearchRequest request, CancellationToken cancellationToken);
    TmdbMatch Resolve(MetadataSelection selection);
}
