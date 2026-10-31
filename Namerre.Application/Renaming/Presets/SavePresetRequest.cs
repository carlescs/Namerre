namespace Namerre.Application.Renaming.Presets;

/// <summary>Creates a preset or replaces one's name and content.</summary>
/// <param name="OnCollision">See <see cref="PipelineRequest.OnCollision"/>.</param>
public sealed record SavePresetRequest(string Name, IReadOnlyList<PluginInstance> Plugins, bool VerifyContent = false,
    bool DeleteEmptyFolders = true, string? OnCollision = null);
