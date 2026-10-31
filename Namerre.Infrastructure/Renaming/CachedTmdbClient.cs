using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Namerre.Infrastructure.Diagnostics;

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
        if (cache.TryGetValue(path, out JsonElement cached))
        {
            Record("cache_hit");
            return cached;
        }
        JsonElement result;
        try { result = await clients().Get(path, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Record(NamerreTelemetry.ErrorType(exception));
            throw;
        }
        Record("ok");
        // When full, the entry is skipped and the cache compacts expired and least recently used entries.
        cache.Set(path, result, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        });
        return result;
    }

    public void Dispose() => cache.Dispose();

    private static void Record(string result)
        => NamerreTelemetry.TmdbLookups.Add(1, new KeyValuePair<string, object?>("namerre.tmdb.result", result));
}
