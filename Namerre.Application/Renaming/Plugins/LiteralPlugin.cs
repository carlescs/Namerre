using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming.Plugins;

public sealed class LiteralPlugin() : RenamePlugin<LiteralPlugin.Options>("literal", 1, "Literal replacement",
    "Replace a piece of text",
    [
        new([
            new("search", "Find text"),
            new("replacement", "Replace with"),
            new("scope", "Replace", OptionFieldKind.Select, [new("all", "Every match"), new("first", "First match only")]),
            new("ignoreCase", "Ignore case", OptionFieldKind.Checkbox)
        ])
    ])
{
    public sealed class Options
    {
        public string Search { get; init; } = "";
        public string Replacement { get; init; } = "";
        public bool IgnoreCase { get; init; }
        public string Scope { get; init; } = "all";
    }

    protected override PathParts Apply(PluginContext context, Options options) => context.Path with
    {
        Stem = NamingRules.Literal(context.Path.Stem, options.Search, options.Replacement, options.IgnoreCase,
            options.Scope)
    };
}
