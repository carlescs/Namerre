using System.Text.Json.Serialization;

namespace Namerre.Application.Renaming.Plugins;

/// <param name="Choices">Options of a <see cref="OptionFieldKind.Select"/> field.</param>
/// <param name="Suggestions">Values offered while typing in a <see cref="OptionFieldKind.Text"/> field.</param>
/// <param name="Wide">Spans the whole row instead of one column; checkboxes always do.</param>
public sealed record OptionField(
    string Key,
    string Label,
    OptionFieldKind Kind = OptionFieldKind.Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<OptionChoice>? Choices = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hint = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Placeholder = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Suggestions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Wide = false);
