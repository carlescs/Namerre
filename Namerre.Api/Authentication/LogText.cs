namespace Namerre.Authentication;

/// <summary>Makes user-supplied text safe to log: line breaks are removed so a value cannot forge log entries.</summary>
public static class LogText
{
    public static string Clean(string? value)
        => (value ?? "").Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
}
