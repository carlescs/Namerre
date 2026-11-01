using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming.Plugins;

public sealed class TmdbPlugin(ITmdbMetadata metadata, IRenameFileSystem fileSystem)
    : RenamePlugin<TmdbPlugin.Options>("tmdb", 1, "TMDB metadata",
        "Name movies and episodes from The Movie Database",
        [
            new([
                new("preset", "Naming preset", OptionFieldKind.Select,
                [
                    new(CustomPreset, "Custom"), new("plexMovies", "Plex — movies"), new("plexTv", "Plex — TV shows"),
                    new("jellyfinMovies", "Jellyfin — movies"), new("jellyfinTv", "Jellyfin — TV shows"),
                    new("embyMovies", "Emby — movies"), new("embyTv", "Emby — TV shows"),
                    new("kodiMovies", "Kodi — movies"), new("kodiTv", "Kodi — TV shows")
                ], Hint: "Fills in the templates with a media server's recommended layout. Editing a template switches back to Custom."),
                new("nameTemplate", "Filename template", Wide: true,
                    Placeholder: "Leave empty to use the default",
                    Hint: "Fields: {title}, {originalTitle}, {year}, {showYear}, {airDate}, {tmdbId}, {season}, {seasonNumber}, {episode}, {episodeTitle}. "
                        + "Pad numbers with zeros, e.g. {episode:000}. Write {{ and }} for literal braces. "
                        + "Defaults: {title} ({year}) for movies, {title} - S{season}E{episode} - {episodeTitle} for episodes."),
                new("episodeRange", "Multi-episode numbers", OptionFieldKind.Select,
                [
                    new(RangeRepeat, "S01E01-E02"), new(RangeConcatenate, "S01E01E02"), new(RangeHyphen, "S01E01-02")
                ], Hint: "How {episode} writes a file that holds several episodes. {episodeTitle} then joins their titles."),
                new("language", "Metadata language",
                    Hint: "Sets the language of titles. Pick one or type your own.",
                    Suggestions: ["en-US", "en-GB", "es-ES", "ca-ES", "fr-FR", "de-DE", "it-IT", "pt-PT", "pt-BR",
                        "ja-JP", "ko-KR", "zh-CN"])
            ], "File name"),
            new([new("changeFolder", "Allow folder change", OptionFieldKind.Checkbox)], "Folders"),
            new([
                new("baseFolder", "Base folder", Placeholder: "Library root",
                    Hint: "Relative to the library root, with forward slashes, e.g. Movies/Archive."),
                new("folderTemplate", "Folder template",
                    Hint: "Creates a folder inside the base folder, e.g. {title} ({year}) [tmdbid-{tmdbId}]."),
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
    /// <summary>The <c>preset</c> value of hand-written templates.</summary>
    public const string CustomPreset = "custom";

    /// <summary><c>episodeRange</c> values: <c>01-E02</c>, <c>01E02</c> and <c>01-02</c> for episodes 1 to 2.</summary>
    public const string RangeRepeat = "repeat";
    public const string RangeConcatenate = "concatenate";
    public const string RangeHyphen = "hyphen";

    /// <remarks>
    /// <see cref="Preset"/> only records which layout the editor filled the templates from; rendering ignores it
    /// and so does <see cref="OptionsHash"/>, so choosing a preset never invalidates confirmed matches.
    /// </remarks>
    public sealed class Options
    {
        public string Preset { get; init; } = CustomPreset;
        public string NameTemplate { get; init; } = "";
        public bool ChangeFolder { get; init; }
        public string BaseFolder { get; init; } = "";
        public string FolderTemplate { get; init; } = "{title} ({year})";
        public bool SeasonFolder { get; init; }
        public string SeasonFolderTemplate { get; init; } = "Season {season}";
        public string Language { get; init; } = "en-US";
        public string EpisodeRange { get; init; } = RangeRepeat;
    }

    /// <summary>
    /// Binds a confirmed match to the metadata settings, not the templates used to render it: SHA-256 of
    /// <c>language</c> only (the episode range style is rendering, too). The frontend mirrors this in <c>options-hash.ts</c>.
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
        var stem = Render(string.IsNullOrWhiteSpace(options.NameTemplate) ? defaultTemplate : options.NameTemplate, match,
            options.EpisodeRange);
        var folder = context.Path.Folder;
        if (options.ChangeFolder)
        {
            if (options.BaseFolder.Length > 0) fileSystem.Validate(options.BaseFolder);
            folder = (options.BaseFolder.Length > 0 ? options.BaseFolder + "/" : "")
                + Render(options.FolderTemplate, match, options.EpisodeRange) + "/";
            // Season folders nest inside the show folder and only apply to episode matches.
            if (options.SeasonFolder && match.Season is not null)
                folder += Render(options.SeasonFolderTemplate, match, options.EpisodeRange) + "/";
        }
        return context.Path with
        {
            Folder = folder,
            Stem = stem + CompanionFiles.SubtitleSuffix(context.Path.Stem, context.Path.Extension)
        };
    }

    /// <summary>
    /// Renders <c>{field}</c> and <c>{field:000}</c> tokens; <c>{{</c> and <c>}}</c> stand for literal braces
    /// (Plex reads ids such as <c>{tmdb-603}</c>). <c>{episode}</c> of a multi-episode match writes the range in
    /// <paramref name="episodeRange"/> style. The frontend mirrors this in <c>tmdb-template-preview.ts</c>.
    /// </summary>
    private static string Render(string template, TmdbMatch match, string episodeRange)
    {
        if (template.Length > 1000) throw new RenameException("InvalidTemplate", "Template is too long.");
        var result = new StringBuilder();
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if ((c is '{' or '}') && i + 1 < template.Length && template[i + 1] == c)
            {
                result.Append(c);
                i++;
            }
            else if (c == '}')
                throw new RenameException("InvalidTemplate", "Write }} for a literal closing brace.");
            else if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                var open = template.IndexOf('{', i + 1);
                if (end < 0 || (open >= 0 && open < end) || end == i + 1)
                    throw new RenameException("InvalidTemplate", "Close each field with }, or write {{ for a literal brace.");
                result.Append(Field(template[(i + 1)..end], match, episodeRange));
                i = end;
            }
            else result.Append(c);
        }
        var rendered = result.ToString();
        if (rendered.Contains('/') || rendered.Contains('\\'))
            throw new RenameException("InvalidTemplate", "Templates must render a single path segment.");
        return rendered;
    }

    private static string Field(string token, TmdbMatch match, string episodeRange)
    {
        var colon = token.IndexOf(':');
        var field = colon < 0 ? token : token[..colon];
        var format = colon < 0 ? null : token[(colon + 1)..];
        return field switch
        {
            "season" => Number(match.Season ?? throw Unavailable("Season"), format, "00"),
            "seasonNumber" => Number(match.Season ?? throw Unavailable("Season"), format, "0"),
            "episode" => Episodes(match, format, episodeRange),
            "tmdbId" => Number(match.Id, format, "0"),
            _ when format is not null => throw new RenameException("InvalidTemplate", "Only numeric fields take a format."),
            "title" => MetadataText(match.Title),
            "originalTitle" => MetadataText(match.OriginalTitle ?? throw Unavailable("Original title")),
            "year" => match.Year is null ? "" : MetadataText(match.Year),
            "showYear" => MetadataText(match.ShowYear ?? throw Unavailable("Show year")),
            "airDate" => MetadataText(match.AirDate ?? throw Unavailable("Air date")),
            "episodeTitle" => MetadataText(match.EpisodeTitle ?? throw Unavailable("Episode title")),
            _ => throw new RenameException("InvalidTemplate", "Unknown template field.")
        };
    }

    private static string Episodes(TmdbMatch match, string? format, string episodeRange)
    {
        var first = Number(match.Episode ?? throw Unavailable("Episode"), format, "00");
        if (match.EpisodeEnd is not { } end || end == match.Episode) return first;
        var last = Number(end, format, "00");
        return episodeRange switch
        {
            RangeConcatenate => first + "E" + last,
            RangeHyphen => first + "-" + last,
            RangeRepeat => first + "-E" + last,
            _ => throw new RenameException("InvalidConfiguration", "Choose how multi-episode numbers are written.")
        };
    }

    /// <summary>Formats with a zero-padding pattern such as <c>0</c>, <c>00</c> or <c>000</c>.</summary>
    private static string Number(int value, string? format, string defaultFormat)
    {
        format ??= defaultFormat;
        if (format.Length is 0 or > 6 || format.Any(c => c != '0'))
            throw new RenameException("InvalidTemplate", "Number formats are 1 to 6 zeros, e.g. {episode:000}.");
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static RenameException Unavailable(string field)
        => new("InvalidTemplate", $"{field} is unavailable.");

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
