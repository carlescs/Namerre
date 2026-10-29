using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Base for plugins with typed options. Options bind camelCase properties case-sensitively; absent keys and
/// JSON nulls for strings keep the defaults of <typeparamref name="TOptions"/>, and a value of the wrong type
/// fails the step as invalid configuration.
/// </summary>
public abstract class RenamePlugin<TOptions> : IRenamePlugin where TOptions : class, new()
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { KeepStringDefaultsForNull } }
    };

    protected RenamePlugin(string typeId, int version, string name, string description,
        IReadOnlyList<OptionGroup> form, bool singleInstance = false)
        => Descriptor = new(typeId, version, name, description, new TOptions(), form, singleInstance);

    public PluginDescriptor Descriptor { get; }

    public PathParts Apply(PluginContext context)
        => Apply(context, context.Options.Deserialize<TOptions>(Json)
            ?? throw new JsonException("Plugin options must be an object."));

    protected abstract PathParts Apply(PluginContext context, TOptions options);

    private static void KeepStringDefaultsForNull(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            if (property.PropertyType != typeof(string) || property.Set is not { } set) continue;
            property.Set = (target, value) =>
            {
                if (value is not null) set(target, value);
            };
        }
    }
}
