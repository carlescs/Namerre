namespace Namerre.Application.Renaming.Plugins;

/// <summary>Shows a field only while another field of the same form holds one of some values.</summary>
/// <param name="Key">A <see cref="OptionFieldKind.Select"/> field outside lists.</param>
/// <param name="Values">Choices of that field that show the field; hidden values are still sent.</param>
public sealed record OptionCondition(string Key, IReadOnlyList<string> Values);
