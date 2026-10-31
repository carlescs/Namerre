namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Shortens names longer than a maximum number of characters (text elements), optionally at a word boundary and with
/// a trailing marker that counts toward the maximum. Shorter names are left unchanged.
/// </summary>
public sealed class TruncatePlugin() : RenamePlugin<TruncatePlugin.Options>("truncate", 1, "Truncate",
    "Shorten names to a maximum length",
    [
        new([
            new("maxLength", "Maximum length", OptionFieldKind.Number, Hint: "In characters, marker included",
                Min: 1, Max: 255, Step: 1),
            new("marker", "Marker", Hint: "Added where the name was cut", Placeholder: "…"),
            new("wordBoundary", "Cut at a word boundary", OptionFieldKind.Checkbox)
        ])
    ])
{
    public sealed class Options
    {
        public int MaxLength { get; init; } = 100;
        public string Marker { get; init; } = "";
        public bool WordBoundary { get; init; }
    }

    protected override PathParts Apply(PluginContext context, Options options) => context.Path with
    {
        Stem = Truncate(context.Path.Stem, options.MaxLength, options.Marker, options.WordBoundary)
    };

    internal static string Truncate(string stem, int maxLength, string marker, bool wordBoundary)
    {
        var elements = TextElements.Split(stem);
        if (elements.Length <= maxLength) return stem;
        var markerLength = TextElements.Split(marker).Length;
        if (markerLength >= maxLength)
            throw new RenameException("InvalidConfiguration", "The marker must be shorter than the maximum length.");
        var keep = maxLength - markerLength;
        if (wordBoundary)
        {
            // Cut before the last whitespace that fits, when the cut does not already fall on one.
            var boundary = Array.FindLastIndex(elements, keep, keep + 1, IsSpace);
            if (boundary > 0) keep = boundary;
        }
        var kept = string.Concat(elements[..keep]).TrimEnd();
        return kept + marker;
    }

    private static bool IsSpace(string element) => element.Length > 0 && char.IsWhiteSpace(element[0]);
}
