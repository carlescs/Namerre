using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Infrastructure.Renaming;

/// <summary>Typed HTTP client for the TMDB API; short-lived, so it carries no cache or limits of its own.</summary>
public sealed class TmdbClient(HttpClient client, IOptions<NamerreOptions> options) : ITmdbClient
{
    public async Task<JsonElement> Get(string path, CancellationToken cancellationToken)
    {
        var token = options.Value.Tmdb.ReadAccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new RenameException("TmdbUnavailable", "Configure the server's Namerre:Tmdb:ReadAccessToken to use TMDB.", 503);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                throw new RenameException("TmdbRateLimited", retry is null
                    ? "TMDB rate limit reached. Try later."
                    : $"TMDB rate limit reached. Retry after {Math.Ceiling(retry.Value)} seconds.", 429);
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new RenameException("MetadataNotFound", "That TMDB movie, show or episode does not exist.", 404);
            if (!response.IsSuccessStatusCode)
                throw new RenameException("TmdbUnavailable", "TMDB is unavailable or rejected the server credentials.", 503);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            return document.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RenameException("TmdbTimeout", "TMDB did not respond within 15 seconds.", 504);
        }
        catch (HttpRequestException)
        {
            throw new RenameException("TmdbUnavailable", "TMDB cannot be reached.", 503);
        }
        catch (JsonException)
        {
            throw new RenameException("TmdbInvalidResponse", "TMDB returned an invalid response.", 502);
        }
    }
}
