namespace Namerre.Application.Renaming.Metadata;

/// <summary>
/// A proposed match. <see cref="Match"/> is verified and carries a confirmation token bound to the request, but it
/// only takes effect once the user (or a watch folder's explicit threshold) accepts it.
/// </summary>
/// <param name="Parsed">What the file name says; the picker starts from it when the proposal is corrected.</param>
/// <param name="Candidates">The search results the proposal was chosen from (empty when no search ran).</param>
/// <param name="Confidence">0–1, see <see cref="MatchConfidence"/>; 0 without a match.</param>
/// <param name="Problem">Why there is no match, e.g. no title could be read or TMDB has no such episode.</param>
public sealed record TmdbSuggestion(ParsedFileName Parsed, IReadOnlyList<TmdbMatch> Candidates, TmdbMatch? Match,
    double Confidence, string? Problem = null);
