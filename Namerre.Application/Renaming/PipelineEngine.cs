using System.Text.Json;
using System.Text.RegularExpressions;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Previews;
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
    /// <summary>Cheap size and modification-time fingerprint, prefixed with <c>stat:</c>; reads no file content.</summary>
    (string Hash, long Size) Stamp(string path);
    void CheckSameDevice(string source, string target);
    void Move(string source, string destination);
    IReadOnlyList<string> MissingDirectories(string path);
    void CreateDirectory(string path);
    bool IsEmptyDirectory(string path);
    /// <summary>Deletes the directory only when it exists and is empty; returns whether it was deleted.</summary>
    bool DeleteEmptyDirectory(string path);
}

public sealed class PipelineEngine
{
    private readonly IRenameFileSystem fileSystem;
    private readonly Dictionary<(string TypeId, int Version), IRenamePlugin> plugins = [];

    public PipelineEngine(IRenameFileSystem fileSystem, IEnumerable<IRenamePlugin> plugins)
    {
        this.fileSystem = fileSystem;
        var registered = plugins.ToArray();
        foreach (var plugin in registered)
        {
            var descriptor = plugin.Descriptor;
            if (!this.plugins.TryAdd((descriptor.TypeId, descriptor.Version), plugin))
                throw new InvalidOperationException(
                    $"Rename plugin {descriptor.TypeId} v{descriptor.Version} is registered more than once.");
        }
        Catalog = registered.Select(p => p.Descriptor).ToArray();
    }

    /// <summary>Registered plugins in registration order, which is unrelated to execution order.</summary>
    public IReadOnlyList<PluginDescriptor> Catalog { get; }

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

    public void ValidateRequest(PipelineRequest request)
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
            if (!plugins.ContainsKey((p.TypeId, p.Version)) || p.Options.ValueKind != JsonValueKind.Object
                || p.Options.GetRawText().Length > 8192)
                throw new RenameException("InvalidPlugin", "Unknown plugin type, version, or invalid options.");
        }
        if ((request.Selections?.Count ?? 0) > NamingRules.MaxFiles * NamingRules.MaxPlugins)
            throw new RenameException("BatchLimit", "Too many metadata selections.");
    }

    private string Apply(FileInput file, PluginInstance plugin, string input, IReadOnlyList<MetadataSelection> selections)
    {
        var slash = input.LastIndexOf('/');
        var name = input[(slash + 1)..];
        var extension = Path.GetExtension(name);
        var parts = new PathParts(slash < 0 ? "" : input[..(slash + 1)], name[..(name.Length - extension.Length)], extension);
        var result = plugins[(plugin.TypeId, plugin.Version)]
            .Apply(new(file, plugin.InstanceId, input, parts, plugin.Options, selections));
        if (result.Extension != parts.Extension)
            throw new InvalidOperationException($"Rename plugin {plugin.TypeId} changed the file extension.");
        if (result.Stem.Length == 0 || result.Stem.Contains('/') || result.Stem.Contains('\\'))
            throw new RenameException("InvalidName", "The plugin produced an empty name or path separator.");
        return result.Folder + result.Stem + result.Extension;
    }

    private static RenameIssue Issue(Exception exception) => exception switch
    {
        RenameException rename => new(rename.Code, rename.Message),
        RegexMatchTimeoutException => new("RegexTimeout", "The regular expression exceeded its 100ms match timeout."),
        _ => new("InvalidConfiguration", "The plugin configuration or resulting path is invalid.")
    };
}
