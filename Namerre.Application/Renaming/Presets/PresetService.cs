namespace Namerre.Application.Renaming.Presets;

/// <summary>
/// Validates and stores pipeline presets. A preset's steps must be valid for this server when saved (as a pipeline
/// request would check them); a stored step whose plugin version is later removed is reported in
/// <see cref="PresetView.UnknownSteps"/> instead of failing the read.
/// </summary>
public sealed class PresetService(IPresetStore store, PipelineEngine pipeline, TimeProvider? clock = null)
{
    public const int MaxPresets = 100;
    public const int MaxNameLength = 100;
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public IReadOnlyList<PresetSummary> List()
        => store.List().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new PresetSummary(p.Id, p.Name, p.Plugins.Count, p.UpdatedAt)).ToArray();

    public PresetView Get(string id) => View(store.Get(ValidId(id)));

    public PresetView Create(SavePresetRequest request)
    {
        var name = Validate(request);
        if (store.Count() >= MaxPresets)
            throw new RenameException("PresetLimit", $"Keep at most {MaxPresets} presets; delete one first.", 409);
        var now = time.GetUtcNow();
        var preset = new PresetRecord(Guid.NewGuid().ToString("N"), name, Copy(request.Plugins), request.VerifyContent,
            request.DeleteEmptyFolders, request.OnCollision, now, now);
        store.Add(preset);
        return View(preset);
    }

    public PresetView Update(string id, SavePresetRequest request)
    {
        var name = Validate(request);
        var preset = store.Get(ValidId(id)) with
        {
            Name = name, Plugins = Copy(request.Plugins), VerifyContent = request.VerifyContent,
            DeleteEmptyFolders = request.DeleteEmptyFolders, OnCollision = request.OnCollision,
            UpdatedAt = time.GetUtcNow()
        };
        store.Replace(preset);
        return View(preset);
    }

    public PresetView Rename(string id, RenamePresetRequest request)
    {
        var preset = store.Get(ValidId(id)) with { Name = ValidName(request?.Name), UpdatedAt = time.GetUtcNow() };
        store.Replace(preset);
        return View(preset);
    }

    public void Delete(string id) => store.Delete(ValidId(id));

    /// <summary>The pipeline a preset runs for <paramref name="files"/>; unknown steps make it invalid.</summary>
    public PipelineRequest Pipeline(PresetView preset, IReadOnlyList<FileInput> files, bool includeCompanions)
    {
        if (preset.UnknownSteps.Count > 0)
            throw new RenameException("PresetOutdated",
                $"Preset {preset.Name} has steps this server no longer knows. Edit and save it again.", 409);
        return new(files, preset.Plugins, [], preset.VerifyContent, preset.DeleteEmptyFolders, null,
            includeCompanions, preset.OnCollision);
    }

    private string Validate(SavePresetRequest request)
    {
        if (request is null) throw new RenameException("InvalidPreset", "Send a preset name and steps.");
        var name = ValidName(request.Name);
        if (request.Plugins is null)
            throw new RenameException("InvalidPreset", "Send the preset's steps (an empty list is allowed).");
        pipeline.ValidatePlugins(request.Plugins);
        if (request.OnCollision is not (null or PipelineRequest.CollisionReport or PipelineRequest.CollisionAddNumber))
            throw new RenameException("InvalidCollisionMode", "Use Report or AddNumber for onCollision.");
        return name;
    }

    private static string ValidName(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength || name.Any(char.IsControl))
            throw new RenameException("InvalidPresetName", $"Name the preset with 1–{MaxNameLength} characters.");
        return name;
    }

    private static string ValidId(string id)
        => id is { Length: > 0 and <= 100 } ? id : throw new RenameException("NotFound", "The requested resource was not found.", 404);

    private PresetView View(PresetRecord preset)
        => new(preset.Id, preset.Name, preset.Plugins, preset.VerifyContent, preset.DeleteEmptyFolders,
            preset.OnCollision, preset.CreatedAt, preset.UpdatedAt,
            preset.Plugins.Where(p => !pipeline.Knows(p)).Select(p => p.InstanceId).ToArray());

    // Options arrive as JsonElements tied to the request's document; clone them so the stored copy owns its data.
    private static PluginInstance[] Copy(IReadOnlyList<PluginInstance> plugins)
        => plugins.Select(p => p with { Options = p.Options.Clone() }).ToArray();
}
