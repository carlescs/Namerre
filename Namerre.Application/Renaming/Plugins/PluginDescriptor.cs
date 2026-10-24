namespace Namerre.Application.Renaming.Plugins;

public sealed record PluginDescriptor(string TypeId, int Version, string Name, object DefaultOptions);
