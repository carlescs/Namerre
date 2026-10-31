using System.Text.RegularExpressions;

namespace Namerre.Application.Renaming.Metadata;

/// <summary>
/// Reads title, year, season and episode from names such as <c>Show.Name.S02E05.1080p</c>, <c>Show 2x05</c> or
/// <c>Movie.Name.2019.1080p.BluRay</c>. The frontend mirrors this in <c>tmdb-filename.ts</c> to prefill the picker;
/// keep both in sync.
/// </summary>
public static partial class TmdbFileName
{
    public static ParsedFileName Parse(string path, string mediaType)
    {
        var parts = path.Split('/');
        var file = parts[^1];
        var dot = file.LastIndexOf('.');
        var name = dot > 0 ? file[..dot] : file;
        var episode = mediaType == "tv" ? Episode().Match(name) : Match.Empty;
        var title = episode.Success ? name[..episode.Index] : name;
        if (episode.Success && Clean(title).Length == 0)
            title = parts[..^1].Reverse().FirstOrDefault(part => !SeasonFolder().IsMatch(part)) ?? "";
        var year = Year().Matches(title).LastOrDefault();
        if (year is not null) title = title[..year.Index];
        // Underscores count as word characters, so they become spaces before tags are found on word boundaries.
        title = ReleaseTag().Split(title.Replace('_', ' '))[0];
        var query = Clean(title);
        if (!episode.Success) return new(query, year?.Groups[1].Value);
        var season = int.Parse(Value(episode, 1, 5));
        var first = int.Parse(Value(episode, 2, 6));
        int? last = null;
        foreach (var group in new[] { 3, 4, 7 })
            if (episode.Groups[group].Success) last = int.Parse(episode.Groups[group].Value);
        // Only a short forward range is a multi-episode file (confirmations take at most ten episodes).
        if (last <= first || last - first >= TmdbSearchRequest.MaxEpisodesPerFile) last = null;
        return new(query, year?.Groups[1].Value, season, first, last);
    }

    private static string Value(Match match, int group, int alternative)
        => match.Groups[group].Success ? match.Groups[group].Value : match.Groups[alternative].Value;

    private static string Clean(string value)
        => Spaces().Replace(Brackets().Replace(value.Replace('.', ' ').Replace('_', ' '), " "), " ")
            .Trim(' ', '-');

    [GeneratedRegex(@"s(\d{1,3})e(\d{1,4})(?:-?e(\d{1,4})|-(\d{1,4})(?![\dp]))?|(\d{1,3})x(\d{1,4})(?:-(?:\d{1,3}x)?(\d{1,4})(?![\dp]))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Episode();

    [GeneratedRegex(@"^(?:season|s)[ ._-]*\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SeasonFolder();

    [GeneratedRegex(@"[ ._(\[]((?:19|20)\d{2})(?=$|[ ._)\]])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Year();

    [GeneratedRegex(@"\b(?:480p|720p|1080[pi]|2160p|4k|bluray|blu-ray|web[ .-]?dl|webrip|hdtv|dvdrip|x26[45]|h26[45])\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ReleaseTag();

    [GeneratedRegex(@"[\[\]()]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Spaces();
}
