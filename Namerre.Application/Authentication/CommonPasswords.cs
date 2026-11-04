using System.Collections.Frozen;

namespace Namerre.Application.Authentication;

/// <summary>
/// The bundled list of common and breached passwords (<c>Resources/common-passwords.txt</c>), loaded on first use.
/// </summary>
internal static class CommonPasswords
{
    private const string ResourceName = "Namerre.Application.Authentication.CommonPasswords";
    // Shorter entries would match almost any password once its digits and symbols are trimmed.
    private const int MinimumCoreLength = 4;
    private static readonly Lazy<FrozenSet<string>> Entries = new(Load);

    public static int Count => Entries.Value.Count;

    /// <summary>
    /// True when the password, or what is left after trimming the digits and symbols around it (<c>Password2024!</c>
    /// is <c>password</c>), is on the list. Comparison ignores case.
    /// </summary>
    public static bool Contains(string password)
    {
        var entries = Entries.Value;
        var candidate = password.Trim();
        if (entries.Contains(candidate))
            return true;
        var start = 0;
        while (start < candidate.Length && !char.IsLetter(candidate[start]))
            start++;
        var end = candidate.Length;
        while (end > start && !char.IsLetter(candidate[end - 1]))
            end--;
        var core = candidate[start..end];
        return core.Length >= MinimumCoreLength && core.Length < candidate.Length && entries.Contains(core);
    }

    private static FrozenSet<string> Load()
    {
        using var stream = typeof(CommonPasswords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            // The header is "# " comments; some passwords on the list start with "#" too.
            if (line.Length == 0 || line.StartsWith("# ", StringComparison.Ordinal))
                continue;
            entries.Add(line);
        }
        return entries.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
