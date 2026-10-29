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
        ])
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
        return context.Path with { Folder = folder, Stem = stem };
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
