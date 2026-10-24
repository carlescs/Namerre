using System.Text.Json;

namespace Namerre.Application.Renaming;

public sealed record PluginInstance(string InstanceId, string TypeId, int Version, bool Enabled, JsonElement Options);
