using System.Text.Json.Serialization;

namespace Namerre.Application.Renaming.Plugins;

/// <param name="Choices">Options of a <see cref="OptionFieldKind.Select"/> or <see cref="OptionFieldKind.MultiSelect"/> field.</param>
/// <param name="Suggestions">Values offered while typing in a <see cref="OptionFieldKind.Text"/> field.</param>
/// <param name="Wide">Spans the whole row instead of one column; checkboxes, text areas and lists always do.</param>
/// <param name="Min">Lowest value of a <see cref="OptionFieldKind.Number"/> field.</param>
/// <param name="Max">Highest value of a <see cref="OptionFieldKind.Number"/> field.</param>
/// <param name="Step">Values of a <see cref="OptionFieldKind.Number"/> field are <paramref name="Min"/> (or 0) plus a
/// multiple of this; 1 allows whole numbers only.</param>
/// <param name="Fields">Columns of each row of a <see cref="OptionFieldKind.List"/> field; lists do not nest.</param>
public sealed record OptionField(
    string Key,
    string Label,
    OptionFieldKind Kind = OptionFieldKind.Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<OptionChoice>? Choices = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hint = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Placeholder = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Suggestions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Wide = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Min = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Max = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Step = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<OptionField>? Fields = null);
