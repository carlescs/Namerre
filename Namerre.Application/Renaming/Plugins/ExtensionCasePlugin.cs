namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Changes the case of the extension (<c>.JPG</c> to <c>.jpg</c>). It is the only built-in step that touches the
/// extension: the engine accepts case-only extension changes and still rejects any other.
/// </summary>
public sealed class ExtensionCasePlugin() : RenamePlugin<ExtensionCasePlugin.Options>("extensionCase", 1,
    "Extension case", "Lowercase or uppercase the file extension, e.g. .JPG to .jpg",
    [
        new([
            new("mode", "Extension", OptionFieldKind.Select,
                [new("lower", "lowercase (.jpg)"), new("upper", "UPPERCASE (.JPG)")])
        ])
    ])
{
    public sealed class Options
    {
        public string Mode { get; init; } = "lower";
    }

    protected override PathParts Apply(PluginContext context, Options options)
        => context.Path with
        {
            Extension = options.Mode == "upper"
                ? context.Path.Extension.ToUpperInvariant() : context.Path.Extension.ToLowerInvariant()
        };
}
