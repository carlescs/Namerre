using System.Globalization;
using System.Text;

namespace Namerre.Domain.Renaming;

/// <summary>
/// Case styles of the case change step. Casing is culture-invariant; accents and letters of scripts without case
/// (CJK, Arabic…) are kept as they are.
/// </summary>
public static class CaseStyles
{
    public const string DefaultLanguage = "en";

    /// <summary>Modes accepted by <see cref="Apply"/>, in catalog order.</summary>
    public static readonly IReadOnlyList<string> Modes =
        ["upper", "lower", "title", "sentence", "smartTitle", "snake", "kebab", "camel", "pascal"];

    /// <summary>
    /// Short words (articles, conjunctions and prepositions) kept lowercase by smart title case unless they are the
    /// first or last word. In Catalan, French and Italian they also mark elisions such as <c>l'</c> or <c>dell'</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> SmallWords =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["en"] = Set("a an and as at but by for from if in into nor of off on onto or per so than the to up upon via vs with yet"),
            ["es"] = Set("a al ante bajo con contra de del desde e el en entre hacia hasta la las lo los ni o para pero por que según sin so sobre tras u un una unas unos y"),
            ["ca"] = Set("a al als amb cap d de del dels des el els en entre fins i l la les m n ni o per pel pels que s sense sobre sota t un una unes uns"),
            ["fr"] = Set("à au aux avec c chez d dans de des du en entre et j l la le les m mais n ni ou par pour qu que s sans sous sur t un une vers"),
            ["it"] = Set("a ad agli ai al all alla alle allo col con d da dagli dai dal dall dalla dalle dallo de dei degli del dell della delle dello di e ed fra gli i il in l la le lo ma negli nei nel nell nella nelle nello o od per su sugli sui sul sull sulla sulle sullo tra un una uno"),
            ["pt"] = Set("a à ao aos as às com da das de do dos e em mas na nas nem no nos o os ou para pela pelas pelo pelos por sem sob sobre um uma umas uns")
        };

    private static readonly HashSet<string> Eliding = ["ca", "fr", "it"];

    /// <exception cref="ArgumentException">Unknown mode or language.</exception>
    public static string Apply(string stem, string mode, string language = DefaultLanguage) => mode switch
    {
        "upper" => stem.ToUpperInvariant(),
        "lower" => stem.ToLowerInvariant(),
        "title" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.ToLowerInvariant()),
        "sentence" => Sentence(stem),
        "smartTitle" => SmartTitle(stem, language),
        "snake" => string.Join('_', Words(stem).Select(w => w.ToLowerInvariant())),
        "kebab" => string.Join('-', Words(stem).Select(w => w.ToLowerInvariant())),
        "camel" => string.Concat(Words(stem).Select((w, i) => i == 0 ? w.ToLowerInvariant() : Capitalize(w))),
        "pascal" => string.Concat(Words(stem).Select(Capitalize)),
        _ => throw new ArgumentException($"Case mode must be one of: {string.Join(", ", Modes)}.")
    };

    /// <summary>Lowercases the name and capitalizes its first letter.</summary>
    private static string Sentence(string stem)
    {
        var lower = stem.ToLowerInvariant();
        for (var i = 0; i < lower.Length; i++)
        {
            if (Rune.TryGetRuneAt(lower, i, out var rune) && Rune.IsLetter(rune))
                return lower[..i] + Capitalize(lower[i..]);
        }
        return lower;
    }

    /// <summary>
    /// Lowercases the name, then capitalizes every word except short words that are neither first nor last. A word
    /// following <c>:</c>, <c>.</c>, <c>!</c>, <c>?</c>, a dash or an opening bracket starts afresh and is capitalized.
    /// Letters after an apostrophe stay lowercase (<c>Don't</c>) unless the part before it is an elided short word
    /// in Catalan, French or Italian (<c>l'Home</c>, <c>Dell'Anno</c>).
    /// </summary>
    private static string SmartTitle(string stem, string language)
    {
        if (!SmallWords.TryGetValue(language, out var small))
            throw new ArgumentException($"Language must be one of: {string.Join(", ", SmallWords.Keys)}.");
        var text = stem.ToLowerInvariant();
        var tokens = Tokens(text);
        var result = new StringBuilder(text.Length);
        var position = 0;
        for (var t = 0; t < tokens.Count; t++)
        {
            var (start, length) = tokens[t];
            var gap = text[position..start];
            result.Append(gap);
            var first = t == 0 || StartsAfresh(gap);
            var last = t == tokens.Count - 1;
            var parts = text.Substring(start, length).Split('\'', '’');
            var elided = parts.Length > 1 && Eliding.Contains(language) && small.Contains(parts[0]);
            var offset = start;
            for (var p = 0; p < parts.Length; p++)
            {
                if (p > 0) result.Append(text[offset - 1]);
                var part = parts[p];
                // Without an elision the token is one word, so only its first part can be capitalized.
                var capitalize = p == 0
                    ? first || last && !elided || !small.Contains(elided ? part : text.Substring(start, length))
                    : elided && (last && p == parts.Length - 1 || !small.Contains(part));
                result.Append(capitalize ? Capitalize(part) : part);
                offset += part.Length + 1;
            }
            position = start + length;
        }
        result.Append(text[position..]);
        return result.ToString();
    }

    private static bool StartsAfresh(string gap) => gap.AsSpan().IndexOfAny(":.!?([{–—") >= 0
        || gap.Contains('-') && gap.Trim().Length < gap.Length;

    /// <summary>Words joined by apostrophes, as (start, length) pairs.</summary>
    private static List<(int Start, int Length)> Tokens(string text)
    {
        var tokens = new List<(int, int)>();
        var i = 0;
        while (i < text.Length)
        {
            if (!IsWordChar(text, i)) { i++; continue; }
            var start = i;
            while (i < text.Length && (IsWordChar(text, i)
                || text[i] is '\'' or '’' && i + 1 < text.Length && IsWordChar(text, i + 1) && i > start))
                i++;
            tokens.Add((start, i - start));
        }
        return tokens;
    }

    /// <summary>
    /// Splits a name into words at anything other than letters, marks and digits, and at
    /// case changes such as <c>myFile</c> or <c>XMLFile</c>.
    /// </summary>
    private static List<string> Words(string stem)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < stem.Length; i++)
        {
            // Apostrophes join short endings (don't, we're) and otherwise separate words (l'home, o'brien).
            if (stem[i] is '\'' or '’')
            {
                var ending = 0;
                while (i + 1 + ending < stem.Length && IsWordChar(stem, i + 1 + ending)) ending++;
                if (ending > 2) Flush();
                continue;
            }
            if (!IsWordChar(stem, i))
            {
                Flush();
                continue;
            }
            if (current.Length > 0 && char.IsUpper(stem[i]))
            {
                var previous = PreviousLetter(current);
                var nextIsLower = i + 1 < stem.Length && char.IsLower(stem[i + 1]);
                if (previous is { } c && (char.IsLower(c) || char.IsDigit(c) || char.IsUpper(c) && nextIsLower))
                    Flush();
            }
            current.Append(stem[i]);
        }
        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0) words.Add(current.ToString());
            current.Clear();
        }
    }

    private static char? PreviousLetter(StringBuilder word)
    {
        for (var i = word.Length - 1; i >= 0; i--)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(word[i]) is not (UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
                return word[i];
        }
        return null;
    }

    private static bool IsWordChar(string text, int index)
        => CharUnicodeInfo.GetUnicodeCategory(text, index) is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber
           || char.IsLowSurrogate(text[index]) && index > 0 && IsWordChar(text, index - 1);

    /// <summary>Uppercases the first character (a whole surrogate pair) and lowercases the rest.</summary>
    private static string Capitalize(string word)
    {
        if (word.Length == 0) return word;
        if (Rune.DecodeFromUtf16(word, out var rune, out var length) != System.Buffers.OperationStatus.Done)
            return word.ToLowerInvariant();
        return Rune.ToUpperInvariant(rune).ToString() + word[length..].ToLowerInvariant();
    }

    private static HashSet<string> Set(string words) => [.. words.Split(' ')];
}
