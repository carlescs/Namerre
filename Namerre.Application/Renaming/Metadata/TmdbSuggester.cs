namespace Namerre.Application.Renaming.Metadata;

/// <summary>
/// Proposes a TMDB match from a file name: parses it (<see cref="TmdbFileName"/>), searches, scores the results
/// (<see cref="MatchConfidence"/>) and verifies the best one for the step's binding. Every lookup goes through
/// <see cref="ITmdbMetadata"/>, so the TMDB concurrency limit, timeouts and cache apply; TMDB failures, including
/// rate limiting, propagate as <see cref="RenameException"/>.
/// </summary>
public sealed class TmdbSuggester(ITmdbMetadata metadata)
{
    public async Task<TmdbSuggestion> Suggest(TmdbSuggestionRequest request, CancellationToken cancellationToken)
    {
        if (request.MediaType is not ("movie" or "tv") || string.IsNullOrWhiteSpace(request.InputPath)
            || request.InputPath.Length > 2048)
            throw new RenameException("InvalidMetadataQuery", "Specify movie or tv and the step's input path.");
        var parsed = TmdbFileName.Parse(request.InputPath, request.MediaType);
        if (parsed.Query.Length == 0 || (request.MediaType == "tv" && parsed.Episode is null))
            return new(parsed, [], null, 0,
                "Could not infer the title or episode numbers. Search again and enter them manually.");
        var query = parsed.Query.Length > 200 ? parsed.Query[..200] : parsed.Query;
        var candidates = (await metadata.Search(new(request.MediaType, query, request.Language), cancellationToken)).Matches;
        if (MatchConfidence.Best(candidates, parsed) is not { } scored)
            return new(parsed, candidates, null, 0, "No TMDB matches. Search again with a different title.");
        var (best, confidence) = scored;
        try
        {
            var verified = (await metadata.Search(new(request.MediaType, query, request.Language, Id: best.Id,
                    Season: parsed.Season, Episode: parsed.Episode, EpisodeEnd: parsed.EpisodeEnd,
                    FileId: request.FileId, InstanceId: request.InstanceId, InputPath: request.InputPath,
                    OptionsHash: request.OptionsHash), cancellationToken))
                .Matches.FirstOrDefault(match => match.Id == best.Id && match.ConfirmationToken is not null);
            return verified is null
                ? new(parsed, candidates, null, 0, "TMDB could not verify this match. Search again to correct it.")
                : new(parsed, candidates, verified, confidence);
        }
        catch (RenameException exception) when (exception.Code == "MetadataNotFound")
        {
            return new(parsed, candidates, null, 0, request.MediaType == "tv"
                ? $"{best.Title} has no episode S{parsed.Season:00}E{parsed.Episode:00} on TMDB. Correct the episode numbers."
                : "TMDB could not find this movie's details. Search again to correct it.");
        }
    }
}
