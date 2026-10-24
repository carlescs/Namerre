namespace Namerre.Application.Renaming.Metadata;

public sealed record TmdbMatch(int Id, string MediaType, string Title, string? Year, int? Season,
    int? Episode, string? EpisodeTitle, string? ConfirmationToken = null);
