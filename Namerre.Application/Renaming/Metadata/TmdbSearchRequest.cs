namespace Namerre.Application.Renaming.Metadata;

/// <param name="EpisodeEnd">Confirms the consecutive episodes <paramref name="Episode"/> to this one of one season for a
/// multi-episode file (at most <see cref="MaxEpisodesPerFile"/>); null or equal to the episode confirms one episode.</param>
public sealed record TmdbSearchRequest(string MediaType, string Query, string Language = "en-US",
    int? ShowId = null, int? Season = null, int? Episode = null, int? Id = null,
    string? FileId = null, string? InstanceId = null, string? InputPath = null, string? OptionsHash = null,
    int? EpisodeEnd = null)
{
    public const int MaxEpisodesPerFile = 10;
}
