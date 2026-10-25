namespace Namerre.Application.Renaming;

public sealed record PipelineRequest(IReadOnlyList<FileInput> Files, IReadOnlyList<PluginInstance> Plugins,
    IReadOnlyList<MetadataSelection>? Selections = null, bool VerifyContent = true);
