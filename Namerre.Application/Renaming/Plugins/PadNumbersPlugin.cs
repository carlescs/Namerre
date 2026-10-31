using System.Text.RegularExpressions;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Zero-pads runs of ASCII digits so names sort naturally. Numbers already at least as wide are left untouched,
/// leading zeros included; numbers that look like years can be skipped (they are then never chosen as the first
/// or last number either).
/// </summary>
public sealed partial class PadNumbersPlugin() : RenamePlugin<PadNumbersPlugin.Options>("pad", 1, "Pad numbers",
    "Add leading zeros so numbers sort correctly",
    [
        new([
            new("width", "Width", OptionFieldKind.Number, Hint: "Minimum number of digits", Min: 1, Max: 10,
                Step: 1),
            new("target", "Numbers", OptionFieldKind.Select,
                [new("all", "Every number"), new("first", "First number"), new("last", "Last number")]),
            new("skipYears", "Skip numbers that look like years (1900–2099)", OptionFieldKind.Checkbox)
        ])
    ])
{
    public sealed class Options
    {
        public int Width { get; init; } = 2;
        public string Target { get; init; } = "all";
        public bool SkipYears { get; init; }
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with { Stem = Pad(context.Path.Stem, options.Width, options.Target, options.SkipYears) };

    internal static string Pad(string stem, int width, string target, bool skipYears)
    {
        var numbers = Digits().Matches(stem).Where(m => !(skipYears && IsYear(m.Value))).ToArray();
        if (numbers.Length == 0) return stem;
        var chosen = target switch
        {
            "first" => numbers[..1],
            "last" => numbers[^1..],
            _ => numbers
        };
        var result = stem;
        // Replace from the end so earlier match indexes stay valid.
        foreach (var number in chosen.Reverse())
            result = result[..number.Index] + number.Value.PadLeft(width, '0') + result[(number.Index + number.Length)..];
        return result;
    }

    private static bool IsYear(string digits)
        => digits.Length == 4 && digits[0] != '0' && int.Parse(digits) is >= 1900 and <= 2099;

    [GeneratedRegex("[0-9]+")]
    private static partial Regex Digits();
}
