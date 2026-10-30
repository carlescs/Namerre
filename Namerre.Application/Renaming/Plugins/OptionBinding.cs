using System.Globalization;
using System.Text.Json;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Checks plugin options against the descriptor's form before they are bound, so malformed values fail with a
/// message naming the field. Keys outside the form are left to the plugin's own binding.
/// </summary>
internal static class OptionBinding
{
    /// <exception cref="RenameException"><c>InvalidConfiguration</c> naming the first malformed field.</exception>
    public static void Validate(JsonElement options, IReadOnlyList<OptionGroup> form)
    {
        if (options.ValueKind != JsonValueKind.Object)
            throw Invalid("Plugin options must be an object.");
        ValidateFields(options, form.SelectMany(g => g.Fields), "");
    }

    /// <summary>Rejects forms the editor cannot render; called once per plugin when the engine is built.</summary>
    /// <exception cref="InvalidOperationException">A choice field without choices, a list without columns or a nested list.</exception>
    public static void ValidateForm(PluginDescriptor descriptor)
    {
        foreach (var field in descriptor.Form.SelectMany(g => g.Fields))
        {
            CheckField(descriptor, field);
            if (field.Kind != OptionFieldKind.List) continue;
            if (field.Fields is not { Count: > 0 } columns
                || columns.Select(c => c.Key).Distinct().Count() != columns.Count)
                throw new InvalidOperationException(
                    $"Rename plugin {descriptor.TypeId} list {field.Key} needs columns with unique keys.");
            foreach (var column in columns)
            {
                if (column.Kind == OptionFieldKind.List)
                    throw new InvalidOperationException($"Rename plugin {descriptor.TypeId} nests list {column.Key}.");
                CheckField(descriptor, column);
            }
        }
    }

    private static void CheckField(PluginDescriptor descriptor, OptionField field)
    {
        if (field.Kind is OptionFieldKind.Select or OptionFieldKind.MultiSelect && field.Choices is not { Count: > 0 })
            throw new InvalidOperationException($"Rename plugin {descriptor.TypeId} field {field.Key} needs choices.");
        if (field is { Min: { } min, Max: { } max } && min > max || field.Step <= 0)
            throw new InvalidOperationException($"Rename plugin {descriptor.TypeId} field {field.Key} has an empty range.");
    }

    private static void ValidateFields(JsonElement values, IEnumerable<OptionField> fields, string prefix)
    {
        foreach (var field in fields)
        {
            if (values.TryGetProperty(field.Key, out var value))
                ValidateValue(value, field, prefix + field.Label);
        }
    }

    private static void ValidateValue(JsonElement value, OptionField field, string label)
    {
        switch (field.Kind)
        {
            // JSON nulls keep the default of text options (see RenamePlugin).
            case OptionFieldKind.Text or OptionFieldKind.Textarea:
                if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw Invalid($"{label} must be text.");
                break;
            case OptionFieldKind.Select:
                if (value.ValueKind == JsonValueKind.Null) break;
                if (value.ValueKind != JsonValueKind.String || !IsChoice(field, value.GetString()))
                    throw Invalid($"{label} must be one of: {Choices(field)}.");
                break;
            case OptionFieldKind.Checkbox:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw Invalid($"{label} must be true or false.");
                break;
            case OptionFieldKind.Number:
                ValidateNumber(value, field, label);
                break;
            case OptionFieldKind.MultiSelect:
                if (value.ValueKind != JsonValueKind.Array)
                    throw Invalid($"{label} must be a list of choices.");
                var selected = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || !IsChoice(field, item.GetString()))
                        throw Invalid($"{label} may only contain: {Choices(field)}.");
                    if (!selected.Add(item.GetString()!))
                        throw Invalid($"{label} lists {item.GetString()} more than once.");
                }
                break;
            case OptionFieldKind.List:
                if (value.ValueKind != JsonValueKind.Array)
                    throw Invalid($"{label} must be a list of rows.");
                var row = 0;
                foreach (var item in value.EnumerateArray())
                {
                    row++;
                    if (item.ValueKind != JsonValueKind.Object)
                        throw Invalid($"{label} row {row} must be an object.");
                    ValidateFields(item, field.Fields ?? [], $"{label} row {row}: ");
                }
                break;
        }
    }

    private static void ValidateNumber(JsonElement value, OptionField field, string label)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw Invalid($"{label} must be a number.");
        if (number < field.Min)
            throw Invalid($"{label} must be at least {Format(field.Min.Value)}.");
        if (number > field.Max)
            throw Invalid($"{label} must be at most {Format(field.Max.Value)}.");
        if (field.Step is { } step)
        {
            var steps = (number - (field.Min ?? 0)) / step;
            if (Math.Abs(steps - Math.Round(steps)) > 1e-9)
                throw Invalid(step == 1 && (field.Min ?? 0) % 1 == 0
                    ? $"{label} must be a whole number."
                    : $"{label} must be in steps of {Format(step)}.");
        }
    }

    private static bool IsChoice(OptionField field, string? value)
        => field.Choices?.Any(c => c.Value == value) == true;

    private static string Choices(OptionField field)
        => string.Join(", ", field.Choices?.Select(c => c.Value) ?? []);

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static RenameException Invalid(string message) => new("InvalidConfiguration", message);
}
