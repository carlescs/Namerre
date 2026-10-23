using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming;

public interface IRenameFileSystem
{
    string RootBinding { get; }
    void Validate(string path);
    bool Exists(string path);
    string ActualPath(string path);
    bool SamePath(string first, string second);
    Task<(string Hash, long Size)> Hash(string path, CancellationToken cancellationToken);
    void CheckSameDevice(string source, string target);
    void Move(string source, string destination);
    IReadOnlyList<string> MissingDirectories(string path);
    void CreateDirectory(string path);
    void DeleteEmptyDirectory(string path);
}

public sealed class PipelineEngine(ITmdbMetadata metadata, IRenameFileSystem fileSystem)
{
    public static IReadOnlyList<PluginDescriptor> Catalog { get; } =
    [
        new("literal", 1, "Literal replacement", new { search = "", replacement = "", ignoreCase = false, scope = "all" }),
        new("regex", 1, "Regular expression", new { pattern = "", replacement = "", ignoreCase = false }),
        new("case", 1, "Case change", new { mode = "lower" }),
        new("tmdb", 1, "TMDB metadata", new { nameTemplate = "", changeFolder = false,
            baseFolder = "", folderTemplate = "{title} ({year})", seasonFolder = false,
            seasonFolderTemplate = "Season {season}", language = "en-US" })
    ];

    public Task<PreviewResult> Preview(PipelineRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var results = new List<PreviewFile>();
        foreach (var file in request.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = file.Path;
            var issues = new List<RenameIssue>();
            var steps = new List<StepTrace>();
            try
            {
                fileSystem.Validate(current);
                if (!fileSystem.Exists(current))
                    throw new RenameException("SourceMissing", "The source file is unavailable.");
            }
            catch (Exception exception) when (exception is RenameException or ArgumentException)
            {
                issues.Add(Issue(exception));
            }
            foreach (var plugin in request.Plugins.Where(p => p.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (issues.Count > 0) break;
                var input = current;
                try
                {
                    current = Apply(file, plugin, input, request.Selections ?? []);
                    fileSystem.Validate(current);
                    steps.Add(new(plugin.InstanceId, plugin.TypeId, input, current, null));
                }
                catch (Exception exception) when (exception is RenameException or ArgumentException
                    or RegexMatchTimeoutException or InvalidOperationException or KeyNotFoundException
                    or JsonException)
                {
                    var issue = Issue(exception);
                    issues.Add(issue);
                    steps.Add(new(plugin.InstanceId, plugin.TypeId, input, null, issue));
                }
            }
            if (issues.Count == 0)
            {
                try { fileSystem.CheckSameDevice(file.Path, current); }
                catch (RenameException exception) { issues.Add(Issue(exception)); }
            }
            results.Add(new(file.Id, file.Path, current, file.Path == current, steps, issues));
        }
        for (var i = 0; i < results.Count; i++)
        {
            var item = results[i];
            if (item.Issues.Count > 0) continue;
            var issues = item.Issues.ToList();
            if (results.Any(other => other.Id != item.Id && fileSystem.SamePath(other.SourcePath, item.SourcePath)))
                issues.Add(new("DuplicateSource", "The same source file appears more than once."));
            if (results.Any(other => other.Id != item.Id && fileSystem.SamePath(other.TargetPath, item.TargetPath)))
                issues.Add(new("DuplicateTarget", "Multiple files resolve to this destination."));
            if (!item.Unchanged && fileSystem.Exists(item.TargetPath)
                && !results.Any(other => fileSystem.SamePath(other.SourcePath, item.TargetPath)
                    && !other.Unchanged && other.Issues.Count == 0))
                issues.Add(new("TargetExists", "The destination already exists and will not be overwritten."));
            results[i] = item with { Issues = issues };
        }
        return Task.FromResult(new PreviewResult(results, results.Count > 0 && results.All(f => f.Issues.Count == 0)));
    }

    public static void ValidateRequest(PipelineRequest request)
    {
        if (request.Files is null || request.Plugins is null || request.Files.Count is < 1 or > NamingRules.MaxFiles
            || request.Plugins.Count > NamingRules.MaxPlugins)
            throw new RenameException("BatchLimit", "Supply 1–200 files and at most 32 plugin instances.");
        if (request.Files.Any(f => string.IsNullOrWhiteSpace(f.Id) || f.Id.Length > 100 || f.Path is null)
            || request.Files.Select(f => f.Id).Distinct().Count() != request.Files.Count
            || request.Files.Select(f => f.Path).Distinct().Count() != request.Files.Count)
            throw new RenameException("DuplicateFile", "File IDs and source paths must be unique.");
        if (request.Plugins.Any(p => string.IsNullOrWhiteSpace(p.InstanceId) || p.InstanceId.Length > 100)
            || request.Plugins.Select(p => p.InstanceId).Distinct().Count() != request.Plugins.Count)
            throw new RenameException("DuplicateInstance", "Each plugin instance needs a unique ID.");
        foreach (var p in request.Plugins)
        {
            if (p.Version != 1 || !Catalog.Any(c => c.TypeId == p.TypeId) || p.Options.ValueKind != JsonValueKind.Object
                || p.Options.GetRawText().Length > 8192)
                throw new RenameException("InvalidPlugin", "Unknown plugin type, version, or invalid options.");
        }
        if ((request.Selections?.Count ?? 0) > NamingRules.MaxFiles * NamingRules.MaxPlugins)
            throw new RenameException("BatchLimit", "Too many metadata selections.");
    }

    public static string OptionsHash(JsonElement options)
    {
        // Match approval binds metadata settings, not the templates used to render it.
        var language = options.TryGetProperty("language", out var value)
            ? value.GetString() ?? "en-US" : "en-US";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(language)));
    }

    private string Apply(FileInput file, PluginInstance plugin, string input, IReadOnlyList<MetadataSelection> selections)
    {
        var slash = input.LastIndexOf('/');
        var folder = slash < 0 ? "" : input[..(slash + 1)];
        var name = input[(slash + 1)..];
        var extension = Path.GetExtension(name);
        var stem = name[..(name.Length - extension.Length)];
        var options = plugin.Options;
        string S(string key, string fallback = "") => options.TryGetProperty(key, out var value)
            ? value.GetString() ?? fallback : fallback;
        bool B(string key) => options.TryGetProperty(key, out var value) && value.GetBoolean();
        switch (plugin.TypeId)
        {
            case "literal":
                stem = NamingRules.Literal(stem, S("search"), S("replacement"), B("ignoreCase"), S("scope", "all"));
                break;
            case "regex":
                stem = NamingRules.RegexReplace(stem, S("pattern"), S("replacement"), B("ignoreCase"));
                break;
            case "case":
                stem = NamingRules.ChangeCase(stem, S("mode", "lower"));
                break;
            case "tmdb":
                var selection = selections.SingleOrDefault(s => s.FileId == file.Id && s.InstanceId == plugin.InstanceId);
                if (selection is null || selection.InputPath != input || selection.OptionsHash != OptionsHash(options))
                    throw new RenameException("MetadataRequired", "Explicitly confirm a TMDB match for this step's current input and metadata language.");
                var match = metadata.Resolve(selection);
                var defaultTemplate = match.MediaType == "tv"
                    ? "{title} - S{season}E{episode} - {episodeTitle}" : "{title} ({year})";
                var nameTemplate = S("nameTemplate");
                stem = Render(string.IsNullOrWhiteSpace(nameTemplate) ? defaultTemplate : nameTemplate, match);
                if (B("changeFolder"))
                {
                    var baseFolder = S("baseFolder");
                    if (baseFolder.Length > 0) fileSystem.Validate(baseFolder);
                    folder = (baseFolder.Length > 0 ? baseFolder + "/" : "")
                        + Render(S("folderTemplate", "{title} ({year})"), match) + "/";
                    // Season folders nest inside the show folder and only apply to episode matches.
                    if (B("seasonFolder") && match.Season is not null)
                        folder += Render(S("seasonFolderTemplate", "Season {season}"), match) + "/";
                }
                break;
        }
        if (stem.Length == 0 || stem.Contains('/') || stem.Contains('\\'))
            throw new RenameException("InvalidName", "The plugin produced an empty name or path separator.");
        return folder + stem + extension;
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

    private static RenameIssue Issue(Exception exception) => exception switch
    {
        RenameException rename => new(rename.Code, rename.Message),
        RegexMatchTimeoutException => new("RegexTimeout", "The regular expression exceeded its 100ms match timeout."),
        _ => new("InvalidConfiguration", "The plugin configuration or resulting path is invalid.")
    };
}
