using System.Globalization;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Positions counted in text elements (extended grapheme clusters), so emoji, surrogate pairs and letters with
/// combining accents are never split.
/// </summary>
internal static class TextElements
{
    /// <summary>The text elements of <paramref name="text"/>, in order.</summary>
    public static string[] Split(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var elements = new string[starts.Length];
        for (var i = 0; i < starts.Length; i++)
            elements[i] = text[starts[i]..(i + 1 < starts.Length ? starts[i + 1] : text.Length)];
        return elements;
    }

    /// <summary>The element index <paramref name="position"/> elements from the chosen end, clamped to the text.</summary>
    public static int Index(int count, int position, bool fromEnd)
        => Math.Clamp(fromEnd ? count - position : position, 0, count);
}
