using System.Text.Json.Serialization;

namespace Namerre.Application.Renaming.Plugins;

[JsonConverter(typeof(JsonStringEnumConverter<OptionFieldKind>))]
public enum OptionFieldKind
{
    [JsonStringEnumMemberName("text")] Text,
    [JsonStringEnumMemberName("checkbox")] Checkbox,
    [JsonStringEnumMemberName("select")] Select,
    /// <summary>A JSON number, optionally bounded by <see cref="OptionField.Min"/>, <see cref="OptionField.Max"/> and <see cref="OptionField.Step"/>.</summary>
    [JsonStringEnumMemberName("number")] Number,
    /// <summary>Multi-line text; a JSON string.</summary>
    [JsonStringEnumMemberName("textarea")] Textarea,
    /// <summary>Repeatable rows: a JSON array of objects described by <see cref="OptionField.Fields"/>.</summary>
    [JsonStringEnumMemberName("list")] List,
    /// <summary>A JSON array of distinct <see cref="OptionField.Choices"/> values.</summary>
    [JsonStringEnumMemberName("multiSelect")] MultiSelect
}
