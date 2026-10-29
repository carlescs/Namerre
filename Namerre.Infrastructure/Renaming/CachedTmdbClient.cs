using System.Collections.Concurrent;
using System.Text.Json;
using Namerre.Application.Renaming;

namespace Namerre.Infrastructure.Renaming;

/// <summary>
/// Shared front for TMDB lookups: caches successful responses for ten minutes and allows four lookups at once.
/// Each lookup takes a fresh client from <paramref name="clients"/>, so the HTTP client factory can rotate handlers.
/// </summary>
public sealed class CachedTmdbClient(Func<ITmdbClient> clients) : ITmdbClient
{
    private readonly SemaphoreSlim concurrency = new(4);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, JsonElement Value)> cache = new();

    public async Task<JsonElement> Get(string path, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(path, out var entry) && entry.Expires > DateTimeOffset.UtcNow) return entry.Value;
        if (!await concurrency.WaitAsync(0, cancellationToken))
            throw new RenameException("TmdbBusy", "Four TMDB lookups are already running. Try again shortly.", 429);
        try
        {
            var result = await clients().Get(path, cancellationToken);
            if (cache.Count >= 256)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var (key, cached) in cache)
                    if (cached.Expires <= now) cache.TryRemove(key, out _);
                if (cache.Count >= 256) cache.Clear();
            }
            cache[path] = (DateTimeOffset.UtcNow.AddMinutes(10), result);
            return result;
        }
        finally { concurrency.Release(); }
    }
}
