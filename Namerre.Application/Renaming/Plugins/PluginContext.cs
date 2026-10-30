using System.Text.Json;

namespace Namerre.Application.Renaming.Plugins;

/// <param name="InputPath">Output of the preceding enabled step (the source path for the first one).</param>
/// <param name="Path"><paramref name="InputPath"/> split into folder, stem and extension.</param>
/// <param name="Position">The file's place in the request and in its source folder.</param>
/// <param name="Facts">Size and dates of the source file; a prepared plan's snapshot when preparing.</param>
public sealed record PluginContext(FileInput File, string InstanceId, string InputPath, PathParts Path,
    JsonElement Options, IReadOnlyList<MetadataSelection> Selections, FilePosition Position, FileFacts Facts);
