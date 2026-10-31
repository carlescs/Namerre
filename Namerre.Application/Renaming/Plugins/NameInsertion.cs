namespace Namerre.Application.Renaming.Plugins;

/// <summary>Places a generated value before or after the name, or into a template with a placeholder.</summary>
internal static class NameInsertion
{
    public const string NamePlaceholder = "{name}";

    public static OptionField PositionField(string placeholder) => new("position", "Position", OptionFieldKind.Select,
        [new("prefix", "Before the name"), new("suffix", "After the name"), new("template", $"Template with {placeholder}")]);

    public static OptionField SeparatorField() => new("separator", "Separator",
        Hint: "Placed between the name and the value when adding before or after");

    public static OptionField TemplateField(string placeholder, string example) => new("template", "Template",
        Hint: $"Used when Position is Template; {placeholder} is the value and {NamePlaceholder} the current name",
        Placeholder: example);

    /// <exception cref="RenameException"><c>InvalidConfiguration</c> for a template without the placeholder.</exception>
    public static string Insert(string stem, string value, string position, string separator, string template,
        string placeholder) => position switch
    {
        "prefix" => value + separator + stem,
        "suffix" => stem + separator + value,
        _ => template.Contains(placeholder, StringComparison.Ordinal)
            ? template.Replace(NamePlaceholder, stem, StringComparison.Ordinal)
                .Replace(placeholder, value, StringComparison.Ordinal)
            : throw new RenameException("InvalidConfiguration", $"Template must contain {placeholder}.")
    };
}
