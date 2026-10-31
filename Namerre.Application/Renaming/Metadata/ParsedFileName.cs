namespace Namerre.Application.Renaming.Metadata;

/// <summary>What a file name says about its movie or episode; see <see cref="TmdbFileName.Parse"/>.</summary>
/// <param name="Query">Cleaned title to search for; empty when none could be read.</param>
/// <param name="EpisodeEnd">Last episode of a multi-episode name such as <c>S01E01-E02</c>.</param>
public sealed record ParsedFileName(string Query, string? Year = null, int? Season = null, int? Episode = null,
    int? EpisodeEnd = null);
