using System.Text.RegularExpressions;
using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming;

/// <summary>
/// Limits a step to some files. A file matches when its current extension is one of <see cref="Extensions"/>
/// (case-insensitive, with or without the dot) and its current file name matches <see cref="Pattern"/>; an empty
/// list or pattern does not restrict. <see cref="Invert"/> applies the step to the files that do not match.
/// Both parts look at the step's input, so earlier steps can change which files a later step applies to.
/// </summary>
public sealed record StepCondition(IReadOnlyList<string>? Extensions = null, string? Pattern = null,
    bool IgnoreCase = true, bool Invert = false)
{
    public const int MaxExtensions = 32;
    public const int MaxExtensionLength = 20;
    public const int MaxPatternLength = 500;

    /// <summary>Whether the condition restricts anything; an empty one applies the step to every file.</summary>
    public bool Restricts => (Extensions?.Count ?? 0) > 0 || !string.IsNullOrEmpty(Pattern);

    /// <summary>Rejects lists and patterns the engine would not evaluate; the pattern must compile.</summary>
    public void Validate()
    {
        var extensions = Extensions ?? [];
        if (extensions.Count > MaxExtensions || extensions.Any(e => e is null || Normalize(e).Length is 0
                || e.Length > MaxExtensionLength || Normalize(e).Any(c => c is '.' or '/' or '\\' || char.IsWhiteSpace(c))))
            throw new RenameException("InvalidCondition",
                $"List up to {MaxExtensions} extensions such as srt or .mkv for a step's \"Apply to\" filter.");
        if (Pattern is null || Pattern.Length == 0) return;
        if (Pattern.Length > MaxPatternLength)
            throw new RenameException("InvalidCondition", $"Keep \"Apply to\" patterns under {MaxPatternLength} characters.");
        try { _ = new Regex(Pattern, Options, NamingRules.RegexTimeout); }
        catch (ArgumentException)
        {
            throw new RenameException("InvalidCondition", "The \"Apply to\" pattern is not a valid regular expression.");
        }
    }

    /// <summary>Whether the step applies to a file whose current path is <paramref name="path"/>.</summary>
    /// <exception cref="RegexMatchTimeoutException">The pattern exceeded the 100 ms match timeout.</exception>
    public bool Applies(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var extension = Path.GetExtension(name);
        var matches = (Extensions is not { Count: > 0 } list
                || list.Any(e => string.Equals(Normalize(e), extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase)))
            && (string.IsNullOrEmpty(Pattern) || Regex.IsMatch(name, Pattern, Options, NamingRules.RegexTimeout));
        return matches != Invert;
    }

    // Regex.IsMatch keeps recently used patterns in the framework's regex cache, so files share one parse.
    private RegexOptions Options => RegexOptions.CultureInvariant | (IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);

    private static string Normalize(string extension) => extension.Trim().TrimStart('.');
}
