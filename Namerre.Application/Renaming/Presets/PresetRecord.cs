namespace Namerre.Application.Renaming.Presets;

/// <summary>A stored preset as <see cref="IPresetStore"/> keeps it.</summary>
public sealed record PresetRecord(string Id, string Name, IReadOnlyList<PluginInstance> Plugins, bool VerifyContent,
    bool DeleteEmptyFolders, string? OnCollision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
