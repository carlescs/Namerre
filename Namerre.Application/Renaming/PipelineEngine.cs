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
    /// <summary>Size and dates of an existing file; reads no file content.</summary>
    FileFacts Facts(string path);
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
    /// <summary>Instance and type ID of the trace step recording a manual target override.</summary>
    public const string OverrideStepId = "override";

    /// <summary>Instance and type ID of the trace step that names a related file after its video.</summary>
    public const string CompanionStepId = "companion";

    /// <summary>Instance and type ID of the trace step that numbers a colliding name.</summary>
    public const string CollisionStepId = "collision";

    /// <summary>Highest <c> (n)</c> suffix tried before a collision is reported instead.</summary>
    public const int MaxCollisionNumber = 9999;

    private readonly IRenameFileSystem fileSystem;
    private readonly Dictionary<(string TypeId, int Version), IRenamePlugin> plugins = [];

    public PipelineEngine(IRenameFileSystem fileSystem, IEnumerable<IRenamePlugin> plugins, RenameLimits? limits = null)
    {
        this.fileSystem = fileSystem;
        Limits = limits ?? RenameLimits.Default;
        var registered = plugins.ToArray();
        foreach (var plugin in registered)
        {
            var descriptor = plugin.Descriptor;
            OptionBinding.ValidateForm(descriptor);
            if (!this.plugins.TryAdd((descriptor.TypeId, descriptor.Version), plugin))
                throw new InvalidOperationException(
                    $"Rename plugin {descriptor.TypeId} v{descriptor.Version} is registered more than once.");
        }
        Catalog = registered.Select(p => p.Descriptor).ToArray();
    }

    /// <summary>Registered plugins in registration order, which is unrelated to execution order.</summary>
    public IReadOnlyList<PluginDescriptor> Catalog { get; }

    /// <summary>The configured workload limits every request is checked against.</summary>
    public RenameLimits Limits { get; }

    public Task<PreviewResult> Preview(PipelineRequest request, CancellationToken cancellationToken)
        => Preview(request, null, cancellationToken);

    /// <param name="facts">File facts snapshotted into a plan (see <see cref="ReadFacts"/>); files without an
    /// entry are read from the filesystem.</param>
    public Task<PreviewResult> Preview(PipelineRequest request, IReadOnlyDictionary<string, FileFacts>? facts,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var companions = request.IncludeCompanions
            ? CompanionFiles.Detect(request.Files) : new Dictionary<string, Companion>();
        // Related files follow their video and take no position, so counters number the videos only.
        var positions = Positions(request.Files.Where(f => !companions.ContainsKey(f.Id)).ToArray());
        var computed = new PreviewFile[request.Files.Count];
        var position = 0;
        for (var index = 0; index < request.Files.Count; index++)
        {
            var file = request.Files[index];
            if (companions.ContainsKey(file.Id)) continue;
            computed[index] = PreviewOne(request, file, positions[position++], facts, cancellationToken);
        }
        // Numbered before related files are named, so they follow their video's numbered stem.
        if (request.OnCollision == PipelineRequest.CollisionAddNumber)
            NumberCollisions(request, computed, companions, cancellationToken);
        var targets = request.Files.Select((file, index) => (file.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index, StringComparer.Ordinal);
        for (var index = 0; index < request.Files.Count; index++)
        {
            var file = request.Files[index];
            if (companions.TryGetValue(file.Id, out var companion))
                computed[index] = PreviewCompanion(request, file, companion, computed[targets[companion.VideoId]],
                    cancellationToken);
        }
        var results = computed.ToList();
        for (var i = 0; i < results.Count; i++)
        {
            if (results[i].CompanionOf is { } videoId && results[targets[videoId]].Numbered)
                results[i] = results[i] with { Numbered = true };
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

    /// <summary>
    /// Gives each renamed file whose target is taken a free <c>stem (n).ext</c> in the same folder, in request
    /// order, so the first file keeps its name. A path is taken when a file staying in place or an earlier target
    /// holds it, or when it exists and no renamed file leaves it. Files with issues or a manual override keep their
    /// target, and a collision left over is reported as usual.
    /// </summary>
    private void NumberCollisions(PipelineRequest request, PreviewFile?[] computed,
        IReadOnlyDictionary<string, Companion> companions, CancellationToken cancellationToken)
    {
        var primary = computed.Where(file => file is not null).Select(file => file!).ToArray();
        static bool Moving(PreviewFile file) => !file.Unchanged && file.Issues.Count == 0;
        static bool Overridden(PreviewFile file) => file.Steps.Count > 0 && file.Steps[^1].TypeId == OverrideStepId;
        var vacated = primary.Where(Moving).Select(file => file.SourcePath).ToArray();
        // Related files are named later; until then they hold their source paths.
        var taken = primary.Where(file => !Moving(file)).Select(file => file.SourcePath)
            .Concat(request.Files.Where(file => companions.ContainsKey(file.Id)).Select(file => file.Path))
            .Concat(primary.Where(file => Moving(file) && Overridden(file)).Select(file => file.TargetPath))
            .ToList();
        bool Taken(string path) => taken.Any(other => fileSystem.SamePath(other, path))
            || (fileSystem.Exists(path) && !vacated.Any(source => fileSystem.SamePath(source, path)));
        for (var index = 0; index < computed.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = computed[index];
            if (file is null || !Moving(file) || Overridden(file)) continue;
            var target = file.TargetPath;
            try
            {
                for (var number = 1; Taken(target) && number <= MaxCollisionNumber; number++)
                {
                    target = Numbered(file.TargetPath, number);
                    fileSystem.Validate(target);
                }
            }
            catch (Exception exception) when (exception is RenameException or ArgumentException)
            {
                // A numbered name that is too long or invalid leaves the collision to be reported.
                target = file.TargetPath;
            }
            if (target != file.TargetPath && !Taken(target))
            {
                computed[index] = file with
                {
                    TargetPath = target, Numbered = true,
                    Steps = [.. file.Steps, new(CollisionStepId, CollisionStepId, file.TargetPath, target, null)]
                };
            }
            taken.Add(computed[index]!.TargetPath);
        }
    }

    private static string Numbered(string path, int number)
    {
        var name = FileName(path);
        var extension = Path.GetExtension(name);
        return path[..^name.Length] + name[..^extension.Length] + $" ({number})" + extension;
    }

    private PreviewFile PreviewOne(PipelineRequest request, FileInput file, FilePosition position,
        IReadOnlyDictionary<string, FileFacts>? facts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = file.Path;
        var issues = new List<RenameIssue>();
        var steps = new List<StepTrace>();
        FileFacts? fileFacts = null;
        try
        {
            fileSystem.Validate(file.Path);
            if (!fileSystem.Exists(file.Path))
                throw new RenameException("SourceMissing", "The source file is unavailable.");
            fileFacts = facts?.GetValueOrDefault(file.Id) ?? fileSystem.Facts(file.Path);
        }
        catch (Exception exception) when (exception is RenameException or ArgumentException or IOException
            or UnauthorizedAccessException)
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
                current = Apply(file, plugin, input, request.Selections ?? [], position, fileFacts!);
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
        current = Finish(request, file, current, steps, issues);
        return new(file.Id, file.Path, current, file.Path == current, steps, issues);
    }

    /// <summary>
    /// A related file skips the steps: it takes its video's final folder and stem (after any override of the
    /// video) and keeps its own suffix, so a confirmed TMDB match and folder moves of the video apply to it too.
    /// </summary>
    private PreviewFile PreviewCompanion(PipelineRequest request, FileInput file, Companion companion,
        PreviewFile video, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = file.Path;
        var issues = new List<RenameIssue>();
        var steps = new List<StepTrace>();
        try
        {
            fileSystem.Validate(file.Path);
            if (!fileSystem.Exists(file.Path))
                throw new RenameException("SourceMissing", "The source file is unavailable.");
            if (video.Issues.Count > 0)
                throw new RenameException("CompanionBlocked",
                    $"Follows {FileName(video.SourcePath)}, which cannot be renamed yet.");
            var videoName = FileName(video.TargetPath);
            var folder = video.TargetPath[..^videoName.Length];
            current = folder + Path.GetFileNameWithoutExtension(videoName) + companion.Suffix;
            fileSystem.Validate(current);
            steps.Add(new(CompanionStepId, CompanionStepId, file.Path, current, null));
        }
        catch (Exception exception) when (exception is RenameException or ArgumentException or IOException
            or UnauthorizedAccessException)
        {
            var issue = Issue(exception);
            issues.Add(issue);
            if (issue.Code != "SourceMissing" && issue.Code != "SourceUnreadable")
                steps.Add(new(CompanionStepId, CompanionStepId, file.Path, null, issue));
            current = file.Path;
        }
        current = Finish(request, file, current, steps, issues);
        return new(file.Id, file.Path, current, file.Path == current, steps, issues, companion.VideoId);
    }

    /// <summary>Applies a manual override and checks the device; returns the final target.</summary>
    private string Finish(PipelineRequest request, FileInput file, string current, List<StepTrace> steps,
        List<RenameIssue> issues)
    {
        var targetOverride = request.Overrides?.FirstOrDefault(o => o.FileId == file.Id);
        if (issues.Count == 0 && targetOverride is not null)
        {
            // A manual override replaces the computed name and may change any part of the path.
            var input = current;
            try
            {
                fileSystem.Validate(targetOverride.TargetPath);
                current = targetOverride.TargetPath;
                steps.Add(new(OverrideStepId, OverrideStepId, input, current, null));
            }
            catch (Exception exception) when (exception is RenameException or ArgumentException)
            {
                var issue = Issue(exception);
                issues.Add(issue);
                steps.Add(new(OverrideStepId, OverrideStepId, input, null, issue));
            }
        }
        if (issues.Count == 0)
        {
            try { fileSystem.CheckSameDevice(file.Path, current); }
            catch (RenameException exception) { issues.Add(Issue(exception)); }
        }
        return current;
    }

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    public void ValidateRequest(PipelineRequest request)
    {
        if (request.Files is null || request.Plugins is null || request.Files.Count < 1
            || request.Files.Count > Limits.MaxFilesPerRun || request.Plugins.Count > Limits.MaxStepsPerRun)
            throw new RenameException("BatchLimit",
                $"Supply 1–{Limits.MaxFilesPerRun} files and at most {Limits.MaxStepsPerRun} plugin instances.");
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
        var repeated = request.Plugins.Select(p => plugins[(p.TypeId, p.Version)].Descriptor)
            .Where(d => d.SingleInstance).GroupBy(d => d.TypeId).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
            throw new RenameException("SingleInstancePlugin", $"Add {repeated.First().Name} only once per pipeline.");
        if ((request.Selections?.Count ?? 0) > Limits.MaxFilesPerRun * Limits.MaxStepsPerRun)
            throw new RenameException("BatchLimit", "Too many metadata selections.");
        if (request.OnCollision is not (null or PipelineRequest.CollisionReport or PipelineRequest.CollisionAddNumber))
            throw new RenameException("InvalidCollisionMode", "Use Report or AddNumber for onCollision.");
        var overrides = request.Overrides ?? [];
        if (overrides.Any(o => o is null || o.TargetPath is null || o.TargetPath.Length > 2048
                || !request.Files.Any(f => f.Id == o.FileId))
            || overrides.Select(o => o.FileId).Distinct().Count() != overrides.Count)
            throw new RenameException("InvalidOverride", "Each name override needs one known file and a target path.");
    }

    /// <summary>
    /// Reads the facts of every readable source file, for a plan to keep; unreadable files are left out and
    /// reported by the preview that follows.
    /// </summary>
    public IReadOnlyDictionary<string, FileFacts> ReadFacts(PipelineRequest request)
    {
        var facts = new Dictionary<string, FileFacts>(StringComparer.Ordinal);
        foreach (var file in request.Files ?? [])
        {
            try
            {
                fileSystem.Validate(file.Path);
                if (fileSystem.Exists(file.Path)) facts[file.Id] = fileSystem.Facts(file.Path);
            }
            catch (Exception exception) when (exception is RenameException or ArgumentException or IOException
                or UnauthorizedAccessException)
            {
            }
        }
        return facts;
    }

    private static FilePosition[] Positions(IReadOnlyList<FileInput> files)
    {
        var folders = files.Select(f => f.Path[..(f.Path.LastIndexOf('/') + 1)]).ToArray();
        var counts = folders.CountBy(folder => folder, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var positions = new FilePosition[files.Count];
        for (var i = 0; i < folders.Length; i++)
        {
            var folderIndex = seen.GetValueOrDefault(folders[i]);
            seen[folders[i]] = folderIndex + 1;
            positions[i] = new(i, files.Count, folderIndex, counts[folders[i]]);
        }
        return positions;
    }

    private string Apply(FileInput file, PluginInstance plugin, string input, IReadOnlyList<MetadataSelection> selections,
        FilePosition position, FileFacts facts)
    {
        var slash = input.LastIndexOf('/');
        var name = input[(slash + 1)..];
        var extension = Path.GetExtension(name);
        var parts = new PathParts(slash < 0 ? "" : input[..(slash + 1)], name[..(name.Length - extension.Length)], extension);
        var result = plugins[(plugin.TypeId, plugin.Version)]
            .Apply(new(file, plugin.InstanceId, input, parts, plugin.Options, selections, position, facts));
        // Only the case of the extension may change (see ExtensionCasePlugin); the rest is never rewritten.
        if (!string.Equals(result.Extension, parts.Extension, StringComparison.OrdinalIgnoreCase)
            || result.Extension.Length != parts.Extension.Length)
            throw new InvalidOperationException($"Rename plugin {plugin.TypeId} changed the file extension.");
        if (result.Stem.Length == 0 || result.Stem.Contains('/') || result.Stem.Contains('\\'))
            throw new RenameException("InvalidName", "The plugin produced an empty name or path separator.");
        return result.Folder + result.Stem + result.Extension;
    }

    private static RenameIssue Issue(Exception exception) => exception switch
    {
        RenameException rename => new(rename.Code, rename.Message),
        RegexMatchTimeoutException => new("RegexTimeout", "The regular expression exceeded its 100ms match timeout."),
        IOException or UnauthorizedAccessException => new("SourceUnreadable", "The source file's details cannot be read."),
        _ => new("InvalidConfiguration", "The plugin configuration or resulting path is invalid.")
    };
}
