namespace Namerre.Application.Renaming.Plugins;

/// <param name="Form">Option editor layout rendered by the frontend; keys refer to <paramref name="DefaultOptions"/>.</param>
public sealed record PluginDescriptor(string TypeId, int Version, string Name, string Description,
    object DefaultOptions, IReadOnlyList<OptionGroup> Form);
