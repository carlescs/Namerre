using System.Text.Json;
using Microsoft.Extensions.Options;
using Namerre.Infrastructure.Configuration;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Domain.Renaming;

namespace Namerre.Infrastructure.Renaming;

public sealed class TmdbMetadata(ITmdbClient client, IOptions<NamerreOptions> options, RenameStore store) : ITmdbMetadata
{
    public sealed record Confirmation(MetadataSelection Binding, TmdbMatch Match);

    public async Task<TmdbSearchResult> Search(TmdbSearchRequest request, CancellationToken cancellationToken)
    {
        if (request.MediaType is not ("movie" or "tv") || (request.Query?.Length ?? 0) > 200
            || request.Id is <= 0 || request.ShowId is <= 0 || request.Season is < 0 or > 999
            || request.Episode is < 1 or > 9999)
            throw new RenameException("InvalidMetadataQuery", "Specify movie or tv, a short search, and valid identifiers.");
        var language = string.IsNullOrWhiteSpace(request.Language)
            ? options.Value.Tmdb.Language ?? "en" : request.Language;
        if (language.Length > 20 || language.Any(c => !char.IsAsciiLetter(c) && c != '-'))
            throw new RenameException("InvalidLanguage", "Use a TMDB language code.");
        if (request.Id is int chosen)
        {
            if (string.IsNullOrWhiteSpace(request.FileId) || string.IsNullOrWhiteSpace(request.InstanceId)
                || request.FileId.Length > 100 || request.InstanceId.Length > 100
                || request.InputPath is null || request.OptionsHash is null
                || request.OptionsHash.Length != 64 || !request.OptionsHash.All(Uri.IsHexDigit))
                throw new RenameException("MissingBinding", "Confirmation requires fileId, instanceId, inputPath and optionsHash.");
            NamingRules.ValidateRelativePath(request.InputPath);
            var details = await client.Get($"{request.MediaType}/{chosen}?language={Uri.EscapeDataString(language)}", cancellationToken);
            var title = Text(details, request.MediaType == "movie" ? "title" : "name");
            var date = Text(details, request.MediaType == "movie" ? "release_date" : "first_air_date");
            string? episodeTitle = null;
            if (request.MediaType == "tv")
            {
                if (request.Season is null || request.Episode is null)
                    throw new RenameException("EpisodeRequired", "Confirm a TV show with explicit season and episode numbers.");
                var episode = await client.Get($"tv/{chosen}/season/{request.Season}/episode/{request.Episode}?language={Uri.EscapeDataString(language)}",
                    cancellationToken);
                episodeTitle = Text(episode, "name");
            }
            if (string.IsNullOrWhiteSpace(title) || (request.MediaType == "tv" && string.IsNullOrWhiteSpace(episodeTitle)))
                throw new RenameException("MetadataIncomplete", "TMDB did not return the required title fields.", 502);
            var token = Guid.NewGuid().ToString("N");
            var match = new TmdbMatch(chosen, request.MediaType, title, date.Length >= 4 ? date[..4] : null,
                request.Season, request.Episode, episodeTitle, token);
            var selection = new MetadataSelection(request.FileId, request.InstanceId, request.InputPath,
                request.OptionsHash, token);
            store.Save(token, "Confirmation", "Confirmed", new Confirmation(selection, match));
            return new([match]);
        }
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new RenameException("QueryRequired", "Enter search text; no match will be chosen automatically.");
        var result = await client.Get($"search/{request.MediaType}?query={Uri.EscapeDataString(request.Query)}&language={Uri.EscapeDataString(language)}",
            cancellationToken);
        var matches = new List<TmdbMatch>();
        if (result.TryGetProperty("results", out var results))
        {
            foreach (var item in results.EnumerateArray().Take(20))
            {
                var date = Text(item, request.MediaType == "movie" ? "release_date" : "first_air_date");
                matches.Add(new(item.GetProperty("id").GetInt32(), request.MediaType,
                    Text(item, request.MediaType == "movie" ? "title" : "name"), date.Length >= 4 ? date[..4] : null,
                    null, null, null));
            }
        }
        return new(matches);
    }

    public TmdbMatch Resolve(MetadataSelection selection)
    {
        try
        {
            var confirmation = store.Get<Confirmation>(selection.ConfirmationToken, "Confirmation");
            if (confirmation.Binding != selection)
                throw new RenameException("MetadataStale", "This match belongs to a different file, step input or configuration.");
            return confirmation.Match;
        }
        catch (RenameException exception) when (exception.Code == "NotFound")
        {
            throw new RenameException("MetadataStale", "Confirm this TMDB match again.");
        }
    }

    private static string Text(JsonElement element, string key)
        => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
}
