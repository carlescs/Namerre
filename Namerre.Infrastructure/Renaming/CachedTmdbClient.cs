using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Namerre.Infrastructure.Renaming;

/// <summary>
/// Shared front for TMDB lookups: caches up to 256 successful responses for ten minutes. Each lookup takes a fresh
/// client from <paramref name="clients"/>, so the HTTP client factory can rotate handlers.
/// </summary>
public sealed class CachedTmdbClient(Func<ITmdbClient> clients) : ITmdbClient, IDisposable
{
    // A private cache: its size limit requires every entry to declare a size, which a shared cache cannot promise.
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 256 });

    internal int Count => cache.Count;

    public async Task<JsonElement> Get(string path, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(path, out JsonElement cached)) return cached;
        var result = await clients().Get(path, cancellationToken);
        // When full, the entry is skipped and the cache compacts expired and least recently used entries.
        cache.Set(path, result, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        });
        return result;
    }

    public void Dispose() => cache.Dispose();
}
