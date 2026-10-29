namespace Namerre.Application.Renaming.Plugins;

public sealed class AffixPlugin() : RenamePlugin<AffixPlugin.Options>("affix", 1, "Prefix and suffix",
    "Add text before or after the name",
    [
        new([
            new("prefix", "Prefix", Hint: "Added before the name"),
            new("suffix", "Suffix", Hint: "Added after the name, before the extension")
        ])
    ])
{
    public sealed class Options
    {
        public string Prefix { get; init; } = "";
        public string Suffix { get; init; } = "";
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with { Stem = options.Prefix + context.Path.Stem + options.Suffix };
}
