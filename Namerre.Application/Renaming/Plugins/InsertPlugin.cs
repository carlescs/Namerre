namespace Namerre.Application.Renaming.Plugins;

/// <summary>Inserts text at a position counted in text elements from the start or the end of the name.</summary>
public sealed class InsertPlugin() : RenamePlugin<InsertPlugin.Options>("insert", 1, "Insert text",
    "Insert text at a position in the name",
    [
        new([
            new("text", "Text"),
            new("position", "Position", OptionFieldKind.Number,
                Hint: "Characters to skip first; past the end inserts at the far end", Min: 0, Max: 255, Step: 1),
            new("from", "Count from", OptionFieldKind.Select, [new("start", "Start"), new("end", "End")])
        ])
    ])
{
    public sealed class Options
    {
        public string Text { get; init; } = "";
        public int Position { get; init; }
        public string From { get; init; } = "start";
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with { Stem = Insert(context.Path.Stem, options.Text, options.Position, options.From == "end") };

    internal static string Insert(string stem, string text, int position, bool fromEnd)
    {
        var elements = TextElements.Split(stem);
        var index = TextElements.Index(elements.Length, position, fromEnd);
        return string.Concat(elements[..index]) + text + string.Concat(elements[index..]);
    }
}
