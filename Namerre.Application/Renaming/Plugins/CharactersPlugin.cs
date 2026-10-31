using System.Globalization;
using System.Text;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Normalises characters for filesystems and devices that handle them badly: transliterates ligatures and typographic
/// symbols (<c>æ</c> → <c>ae</c>, <c>ß</c> → <c>ss</c>, <c>’</c> → <c>'</c>), removes diacritics (<c>é</c> → <c>e</c>),
/// replaces or removes what is still outside ASCII (per text element, so a decomposed accent counts once) and
/// finally applies the chosen Unicode normalisation form.
/// </summary>
public sealed class CharactersPlugin() : RenamePlugin<CharactersPlugin.Options>("characters", 1,
    "Remove accents and normalise characters", "Strip diacritics, transliterate ligatures and normalise Unicode",
    [
        new([
            new("changes", "Convert", OptionFieldKind.MultiSelect,
            [
                new("diacritics", "Remove accents and other diacritics (é → e, ç → c, ñ → n)"),
                new("transliterate", "Transliterate ligatures and symbols (æ → ae, ß → ss, ’ → ')")
            ]),
            new("nonAscii", "Other non-ASCII characters", OptionFieldKind.Select,
                [new("keep", "Keep"), new("replace", "Replace"), new("remove", "Remove")]),
            new("replacement", "Replace with", Hint: "Takes the place of each other non-ASCII character",
                VisibleWhen: new("nonAscii", ["replace"])),
            new("form", "Unicode form", OptionFieldKind.Select,
                [new("nfc", "NFC (composed, usual on Windows and Linux)"), new("nfd", "NFD (decomposed, macOS style)")])
        ])
    ])
{
    private static readonly Dictionary<string, string> Transliterations = new(StringComparer.Ordinal)
    {
        ["æ"] = "ae", ["Æ"] = "AE", ["œ"] = "oe", ["Œ"] = "OE", ["ß"] = "ss", ["ẞ"] = "SS", ["ø"] = "o", ["Ø"] = "O",
        ["đ"] = "d", ["Đ"] = "D", ["ð"] = "d", ["Ð"] = "D", ["þ"] = "th", ["Þ"] = "Th", ["ł"] = "l", ["Ł"] = "L",
        ["ı"] = "i", ["ĳ"] = "ij", ["Ĳ"] = "IJ", ["ŀ"] = "l", ["Ŀ"] = "L", ["ﬁ"] = "fi", ["ﬂ"] = "fl", ["ﬀ"] = "ff",
        ["‘"] = "'", ["’"] = "'", ["‚"] = "'", ["‛"] = "'", ["′"] = "'", ["“"] = "\"", ["”"] = "\"", ["„"] = "\"",
        ["″"] = "\"", ["«"] = "\"", ["»"] = "\"", ["‹"] = "'", ["›"] = "'", ["–"] = "-", ["—"] = "-", ["‐"] = "-",
        ["‑"] = "-", ["−"] = "-", ["…"] = "...", ["·"] = ".", ["×"] = "x", [" "] = " ", [" "] = " ",
        [" "] = " ", ["©"] = "(c)", ["®"] = "(R)", ["™"] = "TM", ["€"] = "EUR", ["£"] = "GBP", ["¿"] = "",
        ["¡"] = "", ["º"] = "o", ["ª"] = "a", ["½"] = "1-2", ["¼"] = "1-4", ["¾"] = "3-4"
    };

    public sealed class Options
    {
        public IReadOnlyList<string> Changes { get; init; } = ["diacritics", "transliterate"];
        public string NonAscii { get; init; } = "keep";
        public string Replacement { get; init; } = "_";
        public string Form { get; init; } = "nfc";
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with { Stem = Normalise(context.Path.Stem, options) };

    internal static string Normalise(string stem, Options options)
    {
        var text = stem.Normalize(NormalizationForm.FormC);
        if (options.Changes.Contains("transliterate")) text = MapElements(text, e => Transliterations.GetValueOrDefault(e));
        if (options.Changes.Contains("diacritics")) text = RemoveMarks(text);
        if (options.NonAscii != "keep")
        {
            var replacement = options.NonAscii == "replace" ? options.Replacement : "";
            text = MapElements(text, e => e.All(char.IsAscii) ? null : replacement);
        }
        return text.Normalize(options.Form == "nfd" ? NormalizationForm.FormD : NormalizationForm.FormC);
    }

    /// <summary>Drops combining marks from every letter, keeping the base letters.</summary>
    private static string RemoveMarks(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(c);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Replaces each text element for which <paramref name="map"/> returns a value.</summary>
    private static string MapElements(string text, Func<string, string?> map)
    {
        var result = new StringBuilder(text.Length);
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            result.Append(map(element) ?? element);
        }
        return result.ToString();
    }
}
