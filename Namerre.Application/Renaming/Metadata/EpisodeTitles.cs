using System.Text.RegularExpressions;

namespace Namerre.Application.Renaming.Metadata;

/// <summary>Names a multi-episode file from its episodes' titles. The frontend shows the joined title as TMDB confirmed it.</summary>
public static partial class EpisodeTitles
{
    public const string Separator = " + ";

    /// <summary>
    /// One title stays as is and identical titles collapse into one. Titles that differ only by a part suffix —
    /// <c>(1)</c>/<c>(2)</c>, <c>Part 1</c>/<c>Part 2</c>, <c>Part One</c>, <c>Pt. II</c> — give their common name;
    /// anything else is joined with <c> + </c>.
    /// </summary>
    public static string Join(IReadOnlyList<string> titles)
    {
        if (titles.Count == 0) throw new ArgumentException("At least one title is required.", nameof(titles));
        var distinct = titles.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 1) return distinct[0];
        var bases = titles.Select(title => PartSuffix().Match(title) is { Success: true } match
            ? title[..match.Index].TrimEnd(' ', ',', '-', ':', '–') : null).ToArray();
        if (bases.All(b => !string.IsNullOrWhiteSpace(b)) && bases.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return bases[0]!;
        return string.Join(Separator, titles);
    }

    [GeneratedRegex(@"\s*(?:\(\s*(?:\d+|[ivx]+)\s*\)|[,:\-–]?\s*\(?(?:part|pt\.?)\s*(?:\d+|[ivx]+|one|two|three|four|five)\)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PartSuffix();
}
