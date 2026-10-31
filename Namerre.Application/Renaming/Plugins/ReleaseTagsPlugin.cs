using System.Text.RegularExpressions;
using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Removes release tokens such as <c>1080p</c>, <c>x265</c>, <c>WEB-DL</c> or <c>DDP5.1</c>, bracketed groups and a
/// trailing <c>-GROUP</c>. Tokens match case-insensitively and only as whole tokens, so a tag inside a word is never
/// removed; separators left around a removed token collapse into one, and none are left at either end.
/// </summary>
public sealed class ReleaseTagsPlugin() : RenamePlugin<ReleaseTagsPlugin.Options>("releaseTags", 1,
    "Strip release tags", "Remove resolution, codec, source, audio and HDR tags, brackets and release groups",
    [
        new([
            new("categories", "Tags", OptionFieldKind.MultiSelect,
            [
                new("resolution", "Resolution (1080p, 2160p, 4K, UHD)"),
                new("codec", "Video codec (x264, x265, HEVC, AV1, 10bit)"),
                new("source", "Source (WEB-DL, WEBRip, BluRay, HDTV, Remux)"),
                new("audio", "Audio (DDP5.1, AAC, DTS-HD MA, TrueHD, Atmos)"),
                new("hdr", "HDR (HDR10, HDR10+, Dolby Vision, HLG)")
            ]),
            new("groups", "Also remove", OptionFieldKind.MultiSelect,
            [
                new("brackets", "Bracketed groups [...]"),
                new("parentheses", "Parenthesised groups (...), years included"),
                new("releaseGroup", "Trailing -GROUP after a release tag")
            ]),
            new("extra", "Extra tokens", OptionFieldKind.Textarea, Hint: "One per line or comma-separated",
                Placeholder: "PROPER, REPACK")
        ])
    ])
{
    private const int MaxExtraTokens = 100;
    private const string Separators = " ._-";
    private const char Removed = '\0';

    // Regex fragments per category; each must match a whole token on its own.
    private static readonly IReadOnlyDictionary<string, string[]> Tags = new Dictionary<string, string[]>
    {
        ["resolution"] = [@"(?:480|576|720|1080|1440|2160|4320)[pi]", "4K", "8K", "UHD"],
        ["codec"] = [@"[xh]\.?26[45]", "HEVC", "AVC", "AV1", "VP9", "XviD", "DivX", @"(?:8|10|12)[ -]?bit"],
        ["source"] = [@"WEB[ -]?DL", "WEBRip", "WEB[ -]?Cap", @"Blu[ -]?Ray", "BDRip", "BRRip", "BDRemux", "Remux",
            "HDTV", "PDTV", "DVDRip", "HDRip", "AMZN", "DSNP", "HMAX", "ATVP"],
        ["audio"] = [@"(?:DDP|DD\+?|E?AC-?3|AAC|DTS(?:-HD(?:[ .]MA)?|-X)?|TrueHD|FLAC|Opus|LPCM)(?:[ .]?[1-9]\.[0-9])?",
            "Atmos", "MP3"],
        ["hdr"] = [@"HDR10(?:\+|Plus)?", "HDR", "DV", "DoVi", @"Dolby[ .]Vision", "HLG"]
    };

    private static readonly string AllTags = Alternation(Tags.Values.SelectMany(t => t));

    public sealed class Options
    {
        public IReadOnlyList<string> Categories { get; init; } = ["resolution", "codec", "source", "audio", "hdr"];
        public IReadOnlyList<string> Groups { get; init; } = ["brackets", "releaseGroup"];
        public string Extra { get; init; } = "";
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with { Stem = Strip(context.Path.Stem, options) };

    internal static string Strip(string stem, Options options)
    {
        var extra = ExtraTokens(options.Extra);
        var tags = new List<string>(extra.Select(Regex.Escape));
        foreach (var (category, fragments) in Tags)
        {
            if (options.Categories.Contains(category)) tags.AddRange(fragments);
        }
        var result = stem;
        if (options.Groups.Contains("releaseGroup"))
        {
            // Only a group right after a known tag counts, so names such as Spider-Man keep their last word.
            var known = extra.Count == 0 ? AllTags : AllTags + "|" + Alternation(extra.Select(Regex.Escape));
            result = Replace(result, $@"(?<=(?<![\p{{L}}\p{{N}}])(?:{known}))-[\p{{L}}\p{{N}}]+$");
        }
        if (options.Groups.Contains("brackets")) result = Replace(result, @"\[[^\[\]]*\]");
        if (options.Groups.Contains("parentheses")) result = Replace(result, @"\([^()]*\)");
        if (tags.Count > 0)
            result = Replace(result, $@"(?<![\p{{L}}\p{{N}}])(?:{Alternation(tags)})(?![\p{{L}}\p{{N}}])");
        return result.Contains(Removed) ? Tidy(result) : result;
    }

    private static IReadOnlyList<string> ExtraTokens(string extra)
    {
        var tokens = extra.Split([',', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tokens.Length > MaxExtraTokens)
            throw new RenameException("InvalidConfiguration", $"Extra tokens may list at most {MaxExtraTokens} tokens.");
        return tokens;
    }

    // Longer alternatives first, so WEB-DL wins over a shorter token that starts the same way.
    private static string Alternation(IEnumerable<string> fragments)
        => string.Join("|", fragments.OrderByDescending(f => f.Length));

    private static string Replace(string input, string pattern)
        => Regex.Replace(input, pattern, Removed.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            NamingRules.RegexTimeout);

    /// <summary>
    /// Replaces each run of separators and removed tokens with its first separator in <c>" ._-"</c> order (a space
    /// when the run had none), or with nothing at either end of the name.
    /// </summary>
    private static string Tidy(string text)
    {
        var runs = Regex.Matches(text, @"[ ._\-]*\0[ ._\-\0]*", RegexOptions.None,
            NamingRules.RegexTimeout);
        var result = text;
        foreach (var run in runs.Reverse())
        {
            var atEdge = run.Index == 0 || run.Index + run.Length == text.Length;
            var separator = atEdge ? "" : Separators.Where(run.Value.Contains).Select(c => c.ToString())
                .DefaultIfEmpty(" ").First();
            result = result[..run.Index] + separator + result[(run.Index + run.Length)..];
        }
        return result;
    }
}
