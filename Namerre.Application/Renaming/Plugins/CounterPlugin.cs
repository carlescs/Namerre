using System.Globalization;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Inserts a sequence number that follows the order of files in the request, either across the whole batch or
/// restarting in each source folder.
/// </summary>
public sealed class CounterPlugin() : RenamePlugin<CounterPlugin.Options>("counter", 1, "Counter",
    "Number files in collection order",
    [
        new([
            new("start", "Start at", OptionFieldKind.Number, Min: 0, Max: 1_000_000_000, Step: 1),
            new("step", "Increment", OptionFieldKind.Number, Min: 1, Max: 1_000_000, Step: 1),
            new("width", "Width", OptionFieldKind.Number, Hint: "Pad with leading zeros to this many digits", Min: 1,
                Max: 10, Step: 1),
            new("perFolder", "Restart the count in each folder", OptionFieldKind.Checkbox)
        ]),
        new([
            NameInsertion.PositionField(Placeholder),
            NameInsertion.SeparatorField(),
            NameInsertion.TemplateField(Placeholder, "{name} - {n}")
        ])
    ])
{
    public const string Placeholder = "{n}";

    public sealed class Options
    {
        public long Start { get; init; } = 1;
        public long Step { get; init; } = 1;
        public int Width { get; init; } = 2;
        public bool PerFolder { get; init; }
        public string Position { get; init; } = "suffix";
        public string Separator { get; init; } = " ";
        public string Template { get; init; } = "{name} {n}";
    }

    protected override PathParts Apply(PluginContext context, Options options)
    {
        var index = options.PerFolder ? context.Position.FolderIndex : context.Position.Index;
        var value = (options.Start + options.Step * index).ToString(CultureInfo.InvariantCulture)
            .PadLeft(options.Width, '0');
        return context.Path with
        {
            Stem = NameInsertion.Insert(context.Path.Stem, value, options.Position, options.Separator, options.Template,
                Placeholder)
        };
    }
}
