namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Removes a run of characters (text elements) from the name: the first or last ones, or a range that starts some
/// characters in from either end. Ranges past the end of the name remove only what is there.
/// </summary>
public sealed class RemovePlugin() : RenamePlugin<RemovePlugin.Options>("remove", 1, "Remove characters",
    "Remove characters from the start, the end or a range of the name",
    [
        new([
            new("count", "Characters to remove", OptionFieldKind.Number, Min: 1, Max: 255, Step: 1),
            new("position", "Skip first", OptionFieldKind.Number,
                Hint: "Characters to keep before the removed range; 0 removes from the very start or end", Min: 0,
                Max: 255, Step: 1),
            new("from", "Count from", OptionFieldKind.Select, [new("start", "Start"), new("end", "End")])
        ])
    ])
{
    public sealed class Options
    {
        public int Count { get; init; } = 1;
        public int Position { get; init; }
        public string From { get; init; } = "start";
    }

    protected override PathParts Apply(PluginContext context, Options options) => context.Path with
    {
        Stem = Remove(context.Path.Stem, options.Count, options.Position, options.From == "end")
    };

    internal static string Remove(string stem, int count, int position, bool fromEnd)
    {
        var elements = TextElements.Split(stem);
        // From the end, the range ends `position` elements before the end and extends towards the start.
        var start = fromEnd ? TextElements.Index(elements.Length, position + count, true)
            : TextElements.Index(elements.Length, position, false);
        var end = fromEnd ? TextElements.Index(elements.Length, position, true)
            : TextElements.Index(elements.Length, position + count, false);
        return string.Concat(elements[..start]) + string.Concat(elements[end..]);
    }
}
