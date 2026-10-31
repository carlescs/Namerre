namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Shows a field only while another field of the same form holds one of some values: a select equals one of them,
/// or a multi-select includes at least one.
/// </summary>
/// <param name="Key">A <see cref="OptionFieldKind.Select"/> or <see cref="OptionFieldKind.MultiSelect"/> field outside lists.</param>
/// <param name="Values">Choices of that field that show the field; hidden values are still sent.</param>
public sealed record OptionCondition(string Key, IReadOnlyList<string> Values);
