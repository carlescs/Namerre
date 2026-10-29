namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// A rename step type. Implementations are registered as singletons in <c>AddApplication()</c>; the
/// registration order is the catalog order shown to users, never the execution order.
/// </summary>
public interface IRenamePlugin
{
    /// <summary>Catalog entry; <c>(TypeId, Version)</c> must be unique and stays stable once released.</summary>
    PluginDescriptor Descriptor { get; }

    /// <summary>
    /// Returns the renamed path. Throw <see cref="RenameException"/> or <see cref="ArgumentException"/> to
    /// report an issue on this step; the engine validates the resulting path afterwards.
    /// </summary>
    PathParts Apply(PluginContext context);
}
