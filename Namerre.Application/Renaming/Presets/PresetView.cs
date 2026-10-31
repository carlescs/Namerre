namespace Namerre.Application.Renaming.Presets;

/// <summary>A named pipeline saved on the server. TMDB matches are per file, so presets never hold them.</summary>
/// <param name="UnknownSteps">Instance IDs of steps whose <c>(TypeId, Version)</c> this server no longer knows; the
/// preset still loads and the editor flags them. Computed on read.</param>
public sealed record PresetView(string Id, string Name, IReadOnlyList<PluginInstance> Plugins, bool VerifyContent,
    bool DeleteEmptyFolders, string? OnCollision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<string> UnknownSteps);
