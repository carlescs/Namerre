using System.Globalization;
using System.Text;

namespace Namerre.Application.Authentication;

/// <summary>
/// Rules that keep usernames unambiguous for the administrators who read them (Users page, merge and link
/// choices, run history, logs): new names are normalized to NFKC and must be printable, and two names whose
/// <see cref="Of"/> skeletons are equal are look-alikes of each other.
/// </summary>
/// <remarks>
/// The skeleton is deliberately pragmatic, not the full Unicode confusables table: NFKC, diacritics removed, case
/// folded, then a small table of the Cyrillic, Greek and Latin letters that are routinely mistaken for ASCII
/// ones. It also folds the digits and letters that look alike in common fonts (<c>0</c> to <c>o</c>, and
/// <c>1</c>, <c>i</c>, <c>l</c> and <c>|</c> to <c>l</c>), so <c>adm1n</c> and <c>admin</c> collide. That costs a
/// few harmless refusals (<c>user1</c> next to <c>userl</c>) and closes the cheapest impersonations.
/// </remarks>
public static class UsernameSkeleton
{
    private static readonly Dictionary<char, char> Confusables = Build();

    /// <summary>
    /// Returns the trimmed NFKC form of a new username, or null when it is empty or holds control, format,
    /// separator, private-use, unassigned, surrogate-half or otherwise invisible characters.
    /// </summary>
    public static string? Canonical(string? username)
    {
        if (string.IsNullOrWhiteSpace(username) || !IsWellFormed(username))
            return null;
        // Normalize throws on noncharacters, which are not allowed anyway.
        var normalized = Normalize(username.Trim(), NormalizationForm.FormKC)?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        foreach (var rune in normalized.EnumerateRunes())
            if (!IsPrintable(rune))
                return null;
        return normalized;
    }

    /// <summary>Makes arbitrary text (a single sign-on claim) usable as a username by dropping what is not allowed.</summary>
    public static string Clean(string? text)
    {
        var wellFormed = new StringBuilder();
        text ??= "";
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                wellFormed.Append(text[i]).Append(text[i + 1]);
                i++;
            }
            else if (!char.IsSurrogate(text[i]) && !IsNoncharacter(text[i]))
                wellFormed.Append(text[i]);
        }
        var kept = new StringBuilder();
        foreach (var rune in (Normalize(wellFormed.ToString(), NormalizationForm.FormKC) ?? "").EnumerateRunes())
            if (IsPrintable(rune))
                kept.Append(rune.ToString());
        return kept.ToString().Trim();
    }

    /// <summary>The comparison key under which look-alike names are equal.</summary>
    public static string Of(string username)
    {
        // Stored names predate the printable-text rule, so a malformed one must not break the comparison.
        if (!IsWellFormed(username) || Normalize(username, NormalizationForm.FormKD) is not { } decomposed)
            return username.ToLowerInvariant();
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            // Diacritics and invisible format characters (legacy names may hold them) do not change the look.
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format)
                continue;
            var lower = char.ToLowerInvariant(c);
            builder.Append(Confusables.TryGetValue(lower, out var plain) ? plain : lower);
        }
        return builder.ToString().Normalize(NormalizationForm.FormKC);
    }

    private static bool IsNoncharacter(int value) => value is >= 0xFDD0 and <= 0xFDEF || (value & 0xFFFE) == 0xFFFE;

    private static string? Normalize(string text, NormalizationForm form)
    {
        try
        {
            return text.Normalize(form);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
            else if (char.IsSurrogate(text[i]))
                return false;
        }
        return true;
    }

    private static bool IsPrintable(Rune rune)
    {
        var value = rune.Value;
        // Noncharacters: U+FDD0..U+FDEF and the last two code points of every plane.
        if (IsNoncharacter(value))
            return false;
        // Characters that render as nothing although they are letters or symbols: Hangul fillers, braille blank,
        // combining grapheme joiner.
        if (value is 0x115F or 0x1160 or 0x3164 or 0xFFA0 or 0x2800 or 0x034F)
            return false;
        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or
                UnicodeCategory.OtherNotAssigned => false,
            // Only the plain space is a visible separator; NFKC already turns the other space characters into it.
            UnicodeCategory.SpaceSeparator => value == ' ',
            _ => true,
        };
    }

    private static Dictionary<char, char> Build()
    {
        var map = new Dictionary<char, char>();
        void Add(string from, string to)
        {
            for (var i = 0; i < from.Length; i++)
                map[from[i]] = to[i];
        }

        // Cyrillic: а е о р с у х ј ѕ ԁ ԛ ԝ һ ԍ, and і ӏ which look like i or l.
        Add("аеорсухјѕԁԛԝһԍіӏ", "aeopcyxjsdqwhgll");
        // Greek: ο ρ α ε κ ν υ χ τ ω, and ι which looks like i.
        Add("οραεκνυχτωι", "opaekvuxtwl");
        // Latin look-alikes outside ASCII: ɡ, dotless ı, capital-I-like ɩ ǀ ɪ.
        Add("ɡıɩǀɪ", "gllll");
        // Shapes shared in common fonts.
        Add("01i|", "olll");
        return map;
    }
}
