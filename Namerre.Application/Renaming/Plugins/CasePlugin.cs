using Namerre.Domain.Renaming;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Changes the case of the name. <c>title</c> capitalizes every word; <c>smartTitle</c> keeps the language's short
/// words lowercase unless they are first or last. The language only affects <c>smartTitle</c>, so adding it kept
/// version 1: options saved without it use English.
/// </summary>
public sealed class CasePlugin() : RenamePlugin<CasePlugin.Options>("case", 1, "Case change",
    "UPPER, lower, Title, Sentence, snake_case, kebab-case, camelCase or PascalCase",
    [
        new([
            new("mode", "Case", OptionFieldKind.Select,
            [
                new("upper", "UPPERCASE"), new("lower", "lowercase"), new("title", "Title Case (Every Word)"),
                new("sentence", "Sentence case"), new("smartTitle", "Title Case (short words lowercase)"),
                new("snake", "snake_case"), new("kebab", "kebab-case"), new("camel", "camelCase"),
                new("pascal", "PascalCase")
            ]),
            new("language", "Short words", OptionFieldKind.Select,
            [
                new("en", "English"), new("es", "Spanish"), new("ca", "Catalan"), new("fr", "French"),
                new("it", "Italian"), new("pt", "Portuguese")
            ], Hint: "Articles and prepositions kept lowercase by Title Case (short words lowercase)")
        ])
    ])
{
    public sealed class Options
    {
        public string Mode { get; init; } = "lower";
        public string Language { get; init; } = CaseStyles.DefaultLanguage;
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with { Stem = NamingRules.ChangeCase(context.Path.Stem, options.Mode, options.Language) };
}
