using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming.Plugins;

public sealed class CasePlugin() : RenamePlugin<CasePlugin.Options>("case", 1, "Case change",
    "UPPER, lower or Title Case",
    [
        new([
            new("mode", "Case", OptionFieldKind.Select,
                [new("upper", "UPPERCASE"), new("lower", "lowercase"), new("title", "Title Case")])
        ])
    ])
{
    public sealed class Options
    {
        public string Mode { get; init; } = "lower";
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with { Stem = NamingRules.ChangeCase(context.Path.Stem, options.Mode) };
}
