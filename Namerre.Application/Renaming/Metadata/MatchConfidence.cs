using System.Globalization;
using System.Text;

namespace Namerre.Application.Renaming.Metadata;

/// <summary>
/// Scores how well a TMDB search result fits a parsed file name, from 0 to 1: three quarters title similarity (exact
/// after normalisation, else the bigram overlap of the title or original title) and one quarter year (same year 1,
/// one year apart 0.5, unknown 0.5, otherwise 0). A rival result with an equally good title and no year to tell
/// them apart costs <see cref="AmbiguityPenalty"/>.
/// </summary>
public static class MatchConfidence
{
    /// <summary>Suggestions at or above this score can be confirmed together; the frontend uses the same value.</summary>
    public const double High = 0.85;
    public const double AmbiguityPenalty = 0.15;

    /// <summary>The best candidate and its score, or null when there are none. Ties keep TMDB's (popularity) order.</summary>
    public static (TmdbMatch Match, double Confidence)? Best(IReadOnlyList<TmdbMatch> candidates, ParsedFileName parsed)
    {
        if (candidates.Count == 0) return null;
        var titles = candidates.Select(candidate => Title(candidate, parsed.Query)).ToArray();
        var scores = candidates.Select((candidate, i) => 0.75 * titles[i] + 0.25 * Year(candidate.Year, parsed.Year)).ToArray();
        var best = 0;
        for (var i = 1; i < scores.Length; i++)
            if (scores[i] > scores[best] + 1e-9) best = i;
        var score = scores[best];
        if (parsed.Year is null && titles.Where((_, i) => i != best).Any(title => title >= titles[best] - 0.05))
            score -= AmbiguityPenalty;
        return (candidates[best], Math.Round(Math.Clamp(score, 0, 1), 2));
    }

    private static double Title(TmdbMatch candidate, string query)
    {
        var wanted = Normalize(query);
        if (wanted.Length == 0) return 0;
        return new[] { candidate.Title, candidate.OriginalTitle }.Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => Similarity(wanted, Normalize(title!))).DefaultIfEmpty(0).Max();
    }

    private static double Year(string? candidate, string? wanted)
    {
        if (wanted is null || candidate is null || !int.TryParse(candidate, out var found)
            || !int.TryParse(wanted, out var expected)) return 0.5;
        return Math.Abs(found - expected) switch { 0 => 1, 1 => 0.5, _ => 0 };
    }

    /// <summary>1 for equal strings, else the Sørensen–Dice coefficient of their character bigrams.</summary>
    private static double Similarity(string first, string second)
    {
        if (first == second) return 1;
        if (first.Length < 2 || second.Length < 2) return 0;
        var bigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < first.Length - 1; i++)
            bigrams[first.Substring(i, 2)] = bigrams.GetValueOrDefault(first.Substring(i, 2)) + 1;
        var shared = 0;
        for (var i = 0; i < second.Length - 1; i++)
        {
            var bigram = second.Substring(i, 2);
            if (bigrams.GetValueOrDefault(bigram) is > 0 and var count)
            {
                bigrams[bigram] = count - 1;
                shared++;
            }
        }
        return 2.0 * shared / (first.Length + second.Length - 2);
    }

    /// <summary>Lowercase letters and digits only, without diacritics, so punctuation and spacing never matter.</summary>
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            else if (c == '&') builder.Append("and");
        }
        return builder.ToString();
    }
}
