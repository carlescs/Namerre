using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Several literal replacements in one step: rows apply in order, each to the previous row's result, with the same
/// case and scope rules as <see cref="LiteralPlugin"/>.
/// </summary>
public sealed class ReplacementTablePlugin() : RenamePlugin<ReplacementTablePlugin.Options>("replacements", 1,
    "Replacement table", "Replace several pieces of text in one step, in order",
    [
        new([
            new("replacements", "Replacements", OptionFieldKind.List, Hint: "Applied from top to bottom",
                Fields: [new("search", "Find text"), new("replacement", "Replace with")]),
            new("scope", "Replace", OptionFieldKind.Select, [new("all", "Every match"), new("first", "First match only")]),
            new("ignoreCase", "Ignore case", OptionFieldKind.Checkbox)
        ])
    ])
{
    public sealed class Row
    {
        public string Search { get; init; } = "";
        public string Replacement { get; init; } = "";
    }

    public sealed class Options
    {
        public IReadOnlyList<Row> Replacements { get; init; } = [];
        public bool IgnoreCase { get; init; }
        public string Scope { get; init; } = "all";
    }

    protected override PathParts Apply(PluginContext context, Options options)
    {
        var stem = context.Path.Stem;
        for (var i = 0; i < options.Replacements.Count; i++)
        {
            var row = options.Replacements[i] ?? throw Invalid(i);
            if (row.Search.Length == 0) throw Invalid(i);
            stem = NamingRules.Literal(stem, row.Search, row.Replacement, options.IgnoreCase, options.Scope);
        }
        return context.Path with { Stem = stem };
    }

    private static RenameException Invalid(int row)
        => new("InvalidConfiguration", $"Replacements row {row + 1}: Find text must not be empty.");
}
