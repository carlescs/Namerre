using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Namerre.Application.Renaming.Metadata;
using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming.Plugins;

public sealed class TmdbPlugin(ITmdbMetadata metadata, IRenameFileSystem fileSystem)
    : RenamePlugin<TmdbPlugin.Options>("tmdb", 1, "TMDB metadata",
        "Name movies and episodes from The Movie Database",
        [
            new([
                new("nameTemplate", "Filename template", Wide: true,
                    Placeholder: "Leave empty to use the default",
                    Hint: "Defaults: {title} ({year}) for movies, {title} - S{season}E{episode} - {episodeTitle} for episodes."),
                new("language", "Metadata language",
                    Hint: "Sets the language of titles. Pick one or type your own.",
                    Suggestions: ["en-US", "en-GB", "es-ES", "ca-ES", "fr-FR", "de-DE", "it-IT", "pt-PT", "pt-BR",
                        "ja-JP", "ko-KR", "zh-CN"])
            ], "File name"),
            new([new("changeFolder", "Allow folder change", OptionFieldKind.Checkbox)], "Folders"),
            new([
                new("baseFolder", "Base folder", Placeholder: "Library root",
                    Hint: "Relative to the library root, with forward slashes, e.g. Movies/Archive."),
                new("folderTemplate", "Folder template", Hint: "Creates a folder inside the base folder."),
                new("seasonFolder", "Add season folder for episodes", OptionFieldKind.Checkbox)
            ], Requires: ["changeFolder"]),
            new([
                new("seasonFolderTemplate", "Season folder template",
                    Hint: "Season {season} gives Season 01, Season {seasonNumber} gives Season 1, S{season} gives S01. Movies ignore it.")
            ], Requires: ["changeFolder", "seasonFolder"])
        ],
        // A second TMDB step would ask for every file to be matched again and repeat each matching row.
        singleInstance: true)
{
    public sealed class Options
    {
        public string NameTemplate { get; init; } = "";
        public bool ChangeFolder { get; init; }
        public string BaseFolder { get; init; } = "";
        public string FolderTemplate { get; init; } = "{title} ({year})";
        public bool SeasonFolder { get; init; }
        public string SeasonFolderTemplate { get; init; } = "Season {season}";
        public string Language { get; init; } = "en-US";
    }

    /// <summary>
    /// Binds a confirmed match to the metadata settings, not the templates used to render it: SHA-256 of
    /// <c>language</c> only. The frontend mirrors this in <c>options-hash.ts</c>.
    /// </summary>
    public static string OptionsHash(JsonElement options)
    {
        var language = options.TryGetProperty("language", out var value)
            ? value.GetString() ?? "en-US" : "en-US";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(language)));
    }

    protected override PathParts Apply(PluginContext context, Options options)
    {
        var selection = context.Selections.SingleOrDefault(s =>
            s.FileId == context.File.Id && s.InstanceId == context.InstanceId);
        if (selection is null || selection.InputPath != context.InputPath
            || selection.OptionsHash != OptionsHash(context.Options))
            throw new RenameException("MetadataRequired", "Explicitly confirm a TMDB match for this step's current input and metadata language.");
        var match = metadata.Resolve(selection);
        var defaultTemplate = match.MediaType == "tv"
            ? "{title} - S{season}E{episode} - {episodeTitle}" : "{title} ({year})";
        var stem = Render(string.IsNullOrWhiteSpace(options.NameTemplate) ? defaultTemplate : options.NameTemplate, match);
        var folder = context.Path.Folder;
        if (options.ChangeFolder)
        {
            if (options.BaseFolder.Length > 0) fileSystem.Validate(options.BaseFolder);
            folder = (options.BaseFolder.Length > 0 ? options.BaseFolder + "/" : "")
                + Render(options.FolderTemplate, match) + "/";
            // Season folders nest inside the show folder and only apply to episode matches.
            if (options.SeasonFolder && match.Season is not null)
                folder += Render(options.SeasonFolderTemplate, match) + "/";
        }
        return context.Path with
        {
            Folder = folder,
            Stem = stem + SubtitleSuffix(context.Path.Stem, context.Path.Extension)
        };
    }

    private static readonly HashSet<string> SubtitleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt" };

    private static readonly HashSet<string> SubtitleFlags =
        new(StringComparer.OrdinalIgnoreCase) { "hi", "sdh", "cc", "forced", "default" };

    // ISO 639-1 codes; three-letter (639-2/3) codes are accepted in lower case only to limit false positives.
    private static readonly HashSet<string> Iso6391 = new(
        ("aa ab ae af ak am an ar as av ay az ba be bg bh bi bm bn bo br bs ca ce ch co cr cs cu cv cy da de dv dz "
        + "ee el en eo es et eu fa ff fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id ie ig ii ik "
        + "io is it iu ja jv ka kg ki kj kk kl km kn ko kr ks ku kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml "
        + "mn mr ms mt my na nb nd ne ng nl nn no nr nv ny oc oj om or os pa pi pl ps pt qu rm rn ro ru rw sa sc sd "
        + "se sg si sk sl sm sn so sq sr ss st su sv sw ta te tg th ti tk tl tn to tr ts tt tw ty ug uk ur uz ve vi "
        + "vo wa wo xh yi yo za zh zu").Split(' '), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// For subtitle files, the trailing language and flag tokens of the stem (for example <c>.en.hi</c> in
    /// <c>Episode.en.hi.srt</c>), which belong to the compound extension and must survive renaming.
    /// The first token is always the name itself and is never treated as a suffix.
    /// </summary>
    internal static string SubtitleSuffix(string stem, string extension)
    {
        if (!SubtitleExtensions.Contains(extension)) return "";
        var tokens = stem.Split('.');
        var start = tokens.Length;
        while (start > 1 && IsSubtitleToken(tokens[start - 1])) start--;
        return start == tokens.Length ? "" : "." + string.Join('.', tokens[start..]);
    }

    private static bool IsSubtitleToken(string token)
    {
        if (SubtitleFlags.Contains(token)) return true;
        var separator = token.IndexOfAny(['-', '_']);
        var language = separator < 0 ? token : token[..separator];
        if (separator >= 0)
        {
            var region = token[(separator + 1)..];
            var validRegion = region.Length == 2 && region.All(char.IsAsciiLetter)
                || region.Length == 4 && region.All(char.IsAsciiLetter);
            if (!validRegion) return false;
        }
        return language.Length == 2 ? Iso6391.Contains(language)
            : language.Length == 3 && language.All(char.IsAsciiLetterLower);
    }

    private static string Render(string template, TmdbMatch match)
    {
        if (template.Length > 1000) throw new RenameException("InvalidTemplate", "Template is too long.");
        var result = Regex.Replace(template, @"\{([^{}]+)\}", m => m.Groups[1].Value switch
        {
            "title" => MetadataText(match.Title),
            "year" => match.Year is null ? "" : MetadataText(match.Year),
            "season" => match.Season?.ToString("00") ?? throw new RenameException("InvalidTemplate", "Season is unavailable."),
            "seasonNumber" => match.Season?.ToString(CultureInfo.InvariantCulture)
                ?? throw new RenameException("InvalidTemplate", "Season is unavailable."),
            "episode" => match.Episode?.ToString("00") ?? throw new RenameException("InvalidTemplate", "Episode is unavailable."),
            "episodeTitle" => MetadataText(match.EpisodeTitle ?? throw new RenameException("InvalidTemplate", "Episode title is unavailable.")),
            _ => throw new RenameException("InvalidTemplate", "Unknown template field.")
        }, RegexOptions.CultureInvariant, NamingRules.RegexTimeout);
        if (result.Contains('{') || result.Contains('}') || result.Contains('/') || result.Contains('\\'))
            throw new RenameException("InvalidTemplate", "Templates must render a single path segment without braces.");
        return result;
    }

    private static string MetadataText(string value)
    {
        var result = new string(value.Select(c => c switch
        {
            '{' => '(',
            '}' => ')',
            _ when c < 32 || "<>:\"/\\|?*".Contains(c) => '-',
            _ => c
        }).ToArray()).TrimEnd(' ', '.');
        if (result.Length == 0)
            throw new RenameException("InvalidMetadataName", "TMDB returned text that cannot form a filename.");
        if (result.StartsWith(".namerre-", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(result, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, NamingRules.RegexTimeout))
            result = "_" + result;
        return result;
    }
}
