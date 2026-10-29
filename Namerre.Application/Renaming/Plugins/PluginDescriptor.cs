namespace Namerre.Application.Renaming.Plugins;

/// <param name="Form">Option editor layout rendered by the frontend; keys refer to <paramref name="DefaultOptions"/>.</param>
/// <param name="SingleInstance">A pipeline may hold at most one instance of this type, enabled or not.</param>
public sealed record PluginDescriptor(string TypeId, int Version, string Name, string Description,
    object DefaultOptions, IReadOnlyList<OptionGroup> Form, bool SingleInstance = false);
