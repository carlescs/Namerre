using System.Text.Json.Serialization;

namespace Namerre.Application.Renaming.Plugins;

/// <param name="Requires">Checkbox keys that must all be on for the group to show; hidden values are still sent.</param>
public sealed record OptionGroup(
    IReadOnlyList<OptionField> Fields,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Heading = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Requires = null);
