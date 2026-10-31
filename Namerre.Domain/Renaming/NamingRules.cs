using System.Text.RegularExpressions;
using System.Text;

namespace Namerre.Domain.Renaming;

public static class NamingRules
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    public const int MaxFiles = 200;
    public const int MaxPlugins = 32;

    public static string Literal(string stem, string search, string replacement, bool ignoreCase, string scope)
    {
        if (search.Length == 0) throw new ArgumentException("Search text must not be empty.");
        if (scope is not ("all" or "first")) throw new ArgumentException("Scope must be all or first.");
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (scope == "all") return stem.Replace(search, replacement, comparison);
        var index = stem.IndexOf(search, comparison);
        return index < 0 ? stem : stem[..index] + replacement + stem[(index + search.Length)..];
    }

    public static string RegexReplace(string stem, string pattern, string replacement, bool ignoreCase)
        => Regex.Replace(stem, pattern, replacement,
            RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), RegexTimeout);

    /// <inheritdoc cref="CaseStyles.Apply"/>
    public static string ChangeCase(string stem, string mode, string language = CaseStyles.DefaultLanguage)
        => CaseStyles.Apply(stem, mode, language);

    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.StartsWith('/')
            || path.Contains('\\') || path.Contains(':'))
            throw new ArgumentException("Use a nonempty root-relative path with forward slashes.");
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.Length > 240
                || Encoding.UTF8.GetByteCount(segment) > 255
                || segment.TrimEnd(' ', '.') != segment
                || segment.Any(c => c < 32 || "<>:\"\\|?*".Contains(c))
                || segment.StartsWith(".namerre-", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout))
                throw new ArgumentException("A path segment is empty, reserved or contains invalid characters.");
            for (var i = 0; i < segment.Length; i++)
            {
                if (char.IsHighSurrogate(segment[i]))
                {
                    if (i + 1 == segment.Length || !char.IsLowSurrogate(segment[++i]))
                        throw new ArgumentException("Path names must contain valid Unicode.");
                }
                else if (char.IsLowSurrogate(segment[i]))
                    throw new ArgumentException("Path names must contain valid Unicode.");
            }
        }
    }
}
