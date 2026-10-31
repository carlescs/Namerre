using System.Text.Json;

namespace Namerre.Application.Renaming;

/// <param name="Condition">Applies the step only to matching files; null (or an empty condition) applies it to every file.</param>
public sealed record PluginInstance(string InstanceId, string TypeId, int Version, bool Enabled, JsonElement Options,
    StepCondition? Condition = null);
