namespace Namerre.Application.Renaming;

/// <param name="IncludeCompanions">Renames subtitles, <c>.nfo</c> files and artwork in the request together with
/// the video they belong to (see <see cref="CompanionFiles"/>) instead of running the steps on them.</param>
/// <param name="OnCollision"><see cref="CollisionReport"/> (default, also when null) reports names that collide
/// with each other or with an existing file; <see cref="CollisionAddNumber"/> appends <c> (1)</c>, <c> (2)</c>, … to
/// the stem instead. Numbers are chosen by the preview that prepares a plan, so execution never picks names.</param>
public sealed record PipelineRequest(IReadOnlyList<FileInput> Files, IReadOnlyList<PluginInstance> Plugins,
    IReadOnlyList<MetadataSelection>? Selections = null, bool VerifyContent = true,
    bool DeleteEmptyFolders = false, IReadOnlyList<TargetOverride>? Overrides = null,
    bool IncludeCompanions = false, string? OnCollision = null)
{
    public const string CollisionReport = "Report";
    public const string CollisionAddNumber = "AddNumber";
}
