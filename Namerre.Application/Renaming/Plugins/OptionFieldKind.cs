using System.Text.Json.Serialization;

namespace Namerre.Application.Renaming.Plugins;

[JsonConverter(typeof(JsonStringEnumConverter<OptionFieldKind>))]
public enum OptionFieldKind
{
    [JsonStringEnumMemberName("text")] Text,
    [JsonStringEnumMemberName("checkbox")] Checkbox,
    [JsonStringEnumMemberName("select")] Select
}
