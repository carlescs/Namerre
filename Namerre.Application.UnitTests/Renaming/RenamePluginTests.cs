using System.Text.Json;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class RenamePluginTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public RenamePluginTests() => fileSystem.Exists("src/name.txt").Returns(true);

    [Fact]
    public void DefaultOptionsKeepTheirWireShape()
    {
        var catalog = TestPipeline.Create(metadata, fileSystem).Catalog;

        Assert.Equal(
            [
                """{"search":"","replacement":"","ignoreCase":false,"scope":"all"}""",
                """{"pattern":"","replacement":"","ignoreCase":false}""",
                """{"mode":"lower","language":"en"}""",
                """{"prefix":"","suffix":""}""",
                """{"width":2,"target":"all","skipYears":false}""",
                """{"start":1,"step":1,"width":2,"perFolder":false,"position":"suffix","separator":" ","template":"{name} {n}"}""",
                """{"source":"modified","format":"yyyy-MM-dd","timeZone":"","position":"prefix","separator":" ","template":"{date} {name}"}""",
                """{"nameTemplate":"","changeFolder":false,"baseFolder":"","folderTemplate":"{title} ({year})","seasonFolder":false,"seasonFolderTemplate":"Season {season}","language":"en-US"}"""
            ],
            catalog.Select(p => JsonSerializer.Serialize(p.DefaultOptions, Web)));
    }

    [Fact]
    public void FormsOnlyDescribeDefaultOptionsOfTheMatchingType()
    {
        foreach (var descriptor in TestPipeline.Create(metadata, fileSystem).Catalog)
        {
            var defaults = JsonSerializer.SerializeToElement(descriptor.DefaultOptions, Web);
            var fields = descriptor.Form.SelectMany(g => g.Fields).ToArray();
            Assert.NotEmpty(descriptor.Description);
            Assert.Equal(fields.Length, fields.Select(f => f.Key).Distinct().Count());
            foreach (var field in fields)
            {
                Assert.True(defaults.TryGetProperty(field.Key, out var value), $"{descriptor.TypeId}.{field.Key}");
                Assert.Equal(field.Kind switch
                {
                    OptionFieldKind.Checkbox => JsonValueKind.False,
                    OptionFieldKind.Number => JsonValueKind.Number,
                    OptionFieldKind.List or OptionFieldKind.MultiSelect => JsonValueKind.Array,
                    _ => JsonValueKind.String
                }, value.ValueKind);
                if (field.Kind == OptionFieldKind.Select)
                    Assert.Contains(value.GetString(), field.Choices!.Select(c => c.Value));
            }
            OptionBinding.Validate(defaults, descriptor.Form);
            foreach (var key in descriptor.Form.SelectMany(g => g.Requires ?? []))
                Assert.Contains(fields, f => f.Key == key && f.Kind == OptionFieldKind.Checkbox);
        }
    }

    [Fact]
    public void FormsSerializeWithoutUnsetProperties()
    {
        var field = JsonSerializer.Serialize(new OptionField("mode", "Case", OptionFieldKind.Select,
            [new("upper", "UPPERCASE")]), Web);

        Assert.Equal("""{"key":"mode","label":"Case","kind":"select","choices":[{"value":"upper","label":"UPPERCASE"}]}""", field);
        Assert.Equal("""{"fields":[]}""", JsonSerializer.Serialize(new OptionGroup([]), Web));
    }

    [Fact]
    public async Task RegisteredPluginsJoinTheCatalogAndRun()
    {
        var engine = new PipelineEngine(fileSystem, [new CasePlugin(), new SuffixPlugin()]);

        var preview = await engine.Preview(new([new("a", "src/name.txt")],
            [Plugin("p", "suffix", new { suffix = "-x" }), Plugin("q", "case", new { mode = "upper" })]), default);

        Assert.Equal(["case", "suffix"], engine.Catalog.Select(p => p.TypeId));
        Assert.Equal("src/NAME-X.txt", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public void PluginsAreAddressedByTypeAndVersion()
    {
        var engine = new PipelineEngine(fileSystem, [new SuffixPlugin()]);

        engine.ValidateRequest(new([new("a", "src/name.txt")], [Plugin("p", "suffix", new { })]));
        Assert.Equal("InvalidPlugin", Assert.Throws<RenameException>(() => engine.ValidateRequest(
            new([new("a", "src/name.txt")], [Plugin("p", "suffix", new { }) with { Version = 2 }]))).Code);
        Assert.Throws<InvalidOperationException>(() => new PipelineEngine(fileSystem, [new SuffixPlugin(), new SuffixPlugin()]));
    }

    [Fact]
    public async Task CaseStepUsesTheChosenLanguageAndDefaultsToEnglish()
    {
        var engine = TestPipeline.Create(metadata, fileSystem);
        fileSystem.Exists("src/love in the time.txt").Returns(true);

        var preview = await engine.Preview(new([new("a", "src/love in the time.txt")],
            [Plugin("p", "case", new { mode = "smartTitle" }), Plugin("q", "case", new { mode = "smartTitle", language = "fr" })]),
            default);

        Assert.Equal("src/Love In The Time.txt", Assert.Single(preview.Files).TargetPath);
        Assert.Equal("src/Love in the Time.txt", preview.Files[0].Steps[0].OutputPath);
    }

    [Fact]
    public async Task CaseStepRejectsUnknownLanguages()
    {
        var engine = TestPipeline.Create(metadata, fileSystem);

        var preview = await engine.Preview(new([new("a", "src/name.txt")],
            [Plugin("p", "case", new { mode = "smartTitle", language = "xx" })]), default);

        Assert.Equal("InvalidConfiguration", Assert.Single(preview.Files[0].Issues).Code);
    }

    [Fact]
    public async Task NullStringOptionsKeepTheirDefaults()
    {
        var engine = TestPipeline.Create(metadata, fileSystem);
        var plugin = new PluginInstance("p", "case", 1, true, JsonDocument.Parse("""{"mode":null}""").RootElement.Clone());

        var preview = await engine.Preview(new([new("a", "src/name.txt")], [plugin]), default);

        Assert.Equal("src/name.txt", Assert.Single(preview.Files).TargetPath);
        Assert.Empty(preview.Files[0].Issues);
    }

    [Fact]
    public async Task PluginsCannotChangeTheExtension()
    {
        var engine = new PipelineEngine(fileSystem, [new SuffixPlugin()]);

        var preview = await engine.Preview(new([new("a", "src/name.txt")],
            [Plugin("p", "suffix", new { suffix = "x", extension = ".exe" })]), default);

        Assert.Equal("InvalidConfiguration", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    private static PluginInstance Plugin(string id, string type, object options)
        => new(id, type, 1, true, JsonSerializer.SerializeToElement(options));

    private sealed class SuffixPlugin() : RenamePlugin<SuffixPlugin.Options>("suffix", 1, "Suffix", "Append text",
        [new([new("suffix", "Suffix")])])
    {
        public sealed class Options
        {
            public string Suffix { get; init; } = "";
            public string? Extension { get; init; }
        }

        protected override PathParts Apply(PluginContext context, Options options) => context.Path with
        {
            Stem = context.Path.Stem + options.Suffix,
            Extension = options.Extension ?? context.Path.Extension
        };
    }
}
