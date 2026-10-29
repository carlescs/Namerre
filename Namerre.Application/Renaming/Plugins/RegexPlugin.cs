using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming.Plugins;

public sealed class RegexPlugin() : RenamePlugin<RegexPlugin.Options>("regex", 1, "Regular expression",
    "Advanced pattern replace",
    [
        new([
            new("pattern", "Pattern", Hint: ".NET regular expression syntax"),
            new("replacement", "Replace with", Hint: "Use $1 or ${name} for captured groups"),
            new("ignoreCase", "Ignore case", OptionFieldKind.Checkbox)
        ])
    ])
{
    public sealed class Options
    {
        public string Pattern { get; init; } = "";
        public string Replacement { get; init; } = "";
        public bool IgnoreCase { get; init; }
    }

    protected override PathParts Apply(PluginContext context, Options options) => context.Path with
    {
        Stem = NamingRules.RegexReplace(context.Path.Stem, options.Pattern, options.Replacement, options.IgnoreCase)
    };
}
