namespace Namerre.Application.Renaming;

/// <param name="IncludeCompanions">Renames subtitles, <c>.nfo</c> files and artwork in the request together with
/// the video they belong to (see <see cref="CompanionFiles"/>) instead of running the steps on them.</param>
public sealed record PipelineRequest(IReadOnlyList<FileInput> Files, IReadOnlyList<PluginInstance> Plugins,
    IReadOnlyList<MetadataSelection>? Selections = null, bool VerifyContent = true,
    bool DeleteEmptyFolders = false, IReadOnlyList<TargetOverride>? Overrides = null,
    bool IncludeCompanions = false);
