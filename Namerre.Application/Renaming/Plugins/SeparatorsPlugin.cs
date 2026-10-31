using System.Text;
using System.Text.RegularExpressions;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Turns release-style separators (<c>.</c>, <c>_</c>, <c>-</c>) into spaces, keeping the dot of short numbers
/// (<c>v1.2</c>, <c>5.1</c>) unless told otherwise, then collapses whitespace and trims spaces, dots, underscores and
/// dashes from both ends. Checkboxes default to off, so they opt out of the usual clean-up.
/// </summary>
public sealed partial class SeparatorsPlugin() : RenamePlugin<SeparatorsPlugin.Options>("separators", 1,
    "Clean up separators", "Turn dots, underscores or dashes into spaces and tidy whitespace",
    [
        new([
            new("characters", "Replace with spaces", OptionFieldKind.MultiSelect,
                [new("dots", "Dots (.)"), new("underscores", "Underscores (_)"), new("dashes", "Dashes (-)")]),
            new("splitNumbers", "Also replace dots in short numbers (v1.2, 5.1)", OptionFieldKind.Checkbox),
            new("keepSpaces", "Keep repeated spaces", OptionFieldKind.Checkbox),
            new("keepEnds", "Keep spaces and separators at both ends", OptionFieldKind.Checkbox)
        ])
    ])
{
    private const string Trimmed = " \t._-";

    public sealed class Options
    {
        public IReadOnlyList<string> Characters { get; init; } = ["dots", "underscores"];
        public bool SplitNumbers { get; init; }
        public bool KeepSpaces { get; init; }
        public bool KeepEnds { get; init; }
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with { Stem = Clean(context.Path.Stem, options) };

    internal static string Clean(string stem, Options options)
    {
        var dots = options.Characters.Contains("dots");
        var underscores = options.Characters.Contains("underscores");
        var dashes = options.Characters.Contains("dashes");
        var result = new StringBuilder(stem.Length);
        for (var i = 0; i < stem.Length; i++)
        {
            var c = stem[i];
            var replace = c switch
            {
                '.' => dots && (options.SplitNumbers || !InShortNumber(stem, i)),
                '_' => underscores,
                '-' => dashes,
                _ => false
            };
            result.Append(replace ? ' ' : c);
        }
        var cleaned = result.ToString();
        if (!options.KeepSpaces) cleaned = Whitespace().Replace(cleaned, " ");
        return options.KeepEnds ? cleaned : cleaned.Trim(Trimmed.ToCharArray());
    }

    /// <summary>
    /// Whether the dot joins a short number such as <c>5.1</c>, <c>v1.2</c> or <c>1.10</c>: one or two digits on each
    /// side, not followed by a letter, so <c>S01E02.1080p</c> still splits.
    /// </summary>
    private static bool InShortNumber(string text, int index)
    {
        var left = 0;
        while (index - left - 1 >= 0 && char.IsAsciiDigit(text[index - left - 1])) left++;
        var right = 0;
        while (index + right + 1 < text.Length && char.IsAsciiDigit(text[index + right + 1])) right++;
        var after = index + right + 1;
        return left is 1 or 2 && right is 1 or 2 && (after == text.Length || !char.IsLetter(text[after]));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
