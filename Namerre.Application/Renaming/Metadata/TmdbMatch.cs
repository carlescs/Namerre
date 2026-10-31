namespace Namerre.Application.Renaming.Metadata;

/// <summary>
/// A TMDB movie or episode. <see cref="AirDate"/> is the episode air date or movie release date
/// (<c>yyyy-MM-dd</c>); <see cref="ShowYear"/> is the show's first-air year (TV only).
/// </summary>
public sealed record TmdbMatch(int Id, string MediaType, string Title, string? Year, int? Season,
    int? Episode, string? EpisodeTitle, string? ConfirmationToken = null, string? OriginalTitle = null,
    string? AirDate = null, string? ShowYear = null);
