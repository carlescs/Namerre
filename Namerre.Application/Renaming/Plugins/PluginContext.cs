using System.Text.Json;

namespace Namerre.Application.Renaming.Plugins;

/// <param name="InputPath">Output of the preceding enabled step (the source path for the first one).</param>
/// <param name="Path"><paramref name="InputPath"/> split into folder, stem and extension.</param>
public sealed record PluginContext(FileInput File, string InstanceId, string InputPath, PathParts Path,
    JsonElement Options, IReadOnlyList<MetadataSelection> Selections);
