using System.Text.Json;
using Namerre.Application.Renaming;

namespace Namerre.Infrastructure.Renaming;

/// <summary>Reads one TMDB API path, mapping failures to <see cref="RenameException"/>.</summary>
public interface ITmdbClient
{
    Task<JsonElement> Get(string path, CancellationToken cancellationToken);
}
