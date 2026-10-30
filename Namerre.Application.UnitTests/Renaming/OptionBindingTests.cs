using System.Text.Json;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Previews;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class OptionBindingTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();

    public OptionBindingTests() => fileSystem.Exists("src/name.txt").Returns(true);

    [Fact]
    public void NewKindsSerializeAsCamelCaseWithTheirSettings()
    {
        Assert.Equal(["number", "textarea", "list", "multiSelect"],
            new[] { OptionFieldKind.Number, OptionFieldKind.Textarea, OptionFieldKind.List, OptionFieldKind.MultiSelect }
                .Select(kind => JsonSerializer.Serialize(kind, Web).Trim('"')));
        Assert.Equal("""{"key":"width","label":"Width","kind":"number","min":1,"max":10,"step":1}""",
            JsonSerializer.Serialize(new OptionField("width", "Width", OptionFieldKind.Number, Min: 1, Max: 10, Step: 1), Web));
        Assert.Equal("""{"key":"pairs","label":"Pairs","kind":"list","fields":[{"key":"find","label":"Find","kind":"text"}]}""",
            JsonSerializer.Serialize(new OptionField("pairs", "Pairs", OptionFieldKind.List, Fields: [new("find", "Find")]), Web));
    }

    [Fact]
    public async Task EveryKindBindsIntoTypedOptions()
    {
        var preview = await Preview("""
            {"count":3,"notes":"a\nb","tags":["b","a"],
             "pairs":[{"find":"name","replace":"N","whole":true},{"find":"x","replace":"y"}]}
            """);

        Assert.Empty(preview.Issues);
        Assert.Equal("src/N-3-b+a-2-a b.txt", preview.TargetPath);
    }

    [Fact]
    public async Task AbsentKeysKeepTheDefaults()
        => Assert.Equal("src/name-1--0-.txt", (await Preview("{}")).TargetPath);

    [Theory]
    [InlineData("""{"count":"3"}""", "Count must be a number.")]
    [InlineData("""{"count":null}""", "Count must be a number.")]
    [InlineData("""{"count":0}""", "Count must be at least 1.")]
    [InlineData("""{"count":11}""", "Count must be at most 10.")]
    [InlineData("""{"count":2.5}""", "Count must be a whole number.")]
    [InlineData("""{"notes":["a"]}""", "Notes must be text.")]
    [InlineData("""{"tags":"a"}""", "Tags must be a list of choices.")]
    [InlineData("""{"tags":["a","z"]}""", "Tags may only contain: a, b, c.")]
    [InlineData("""{"tags":["a","a"]}""", "Tags lists a more than once.")]
    [InlineData("""{"pairs":{}}""", "Pairs must be a list of rows.")]
    [InlineData("""{"pairs":["x"]}""", "Pairs row 1 must be an object.")]
    [InlineData("""{"pairs":[{"find":"a"},{"find":1}]}""", "Pairs row 2: Find must be text.")]
    [InlineData("""{"pairs":[{"whole":"yes"}]}""", "Pairs row 1: Whole name must be true or false.")]
    public async Task MalformedValuesFailWithAMessageNamingTheField(string options, string message)
    {
        var preview = await Preview(options);

        var issue = Assert.Single(preview.Issues);
        Assert.Equal("InvalidConfiguration", issue.Code);
        Assert.Equal(message, issue.Message);
    }

    [Fact]
    public void ValuesThatPassTheFormButNotTheTypeFailClearly()
    {
        var plugin = new KindsPlugin();
        var context = new PluginContext(new("a", "src/name.txt"), "p", "src/name.txt", new("src/", "name", ".txt"),
            JsonDocument.Parse("""{"extra":"x"}""").RootElement, []);

        var exception = Assert.Throws<RenameException>(() => plugin.Apply(context));
        Assert.Equal("InvalidConfiguration", exception.Code);
        Assert.Equal("The Kinds options do not match their expected types.", exception.Message);
    }

    [Theory]
    [InlineData(OptionFieldKind.MultiSelect, "needs choices")]
    [InlineData(OptionFieldKind.List, "needs columns")]
    public void FormsTheEditorCannotRenderAreRejected(OptionFieldKind kind, string message)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new PipelineEngine(fileSystem, [new FormPlugin([new("bad", "Bad", kind)])]));
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void NestedListsAndEmptyRangesAreRejected()
    {
        Assert.Contains("nests", Assert.Throws<InvalidOperationException>(() => new PipelineEngine(fileSystem,
            [new FormPlugin([new("outer", "Outer", OptionFieldKind.List,
                Fields: [new("inner", "Inner", OptionFieldKind.List, Fields: [new("a", "A")])])])])).Message);
        Assert.Contains("empty range", Assert.Throws<InvalidOperationException>(() => new PipelineEngine(fileSystem,
            [new FormPlugin([new("n", "N", OptionFieldKind.Number, Min: 2, Max: 1)])])).Message);
    }

    private async Task<PreviewFile> Preview(string options)
    {
        var engine = new PipelineEngine(fileSystem, [new KindsPlugin()]);
        var plugin = new PluginInstance("p", "kinds", 1, true, JsonDocument.Parse(options).RootElement.Clone());
        return Assert.Single((await engine.Preview(new([new("a", "src/name.txt")], [plugin]), default)).Files);
    }

    private sealed class KindsPlugin() : RenamePlugin<KindsPlugin.Options>("kinds", 1, "Kinds", "Every field kind",
        [
            new([
                new("count", "Count", OptionFieldKind.Number, Min: 1, Max: 10, Step: 1),
                new("notes", "Notes", OptionFieldKind.Textarea),
                new("tags", "Tags", OptionFieldKind.MultiSelect, [new("a", "A"), new("b", "B"), new("c", "C")]),
                new("pairs", "Pairs", OptionFieldKind.List, Fields: [
                    new("find", "Find"),
                    new("replace", "Replace"),
                    new("whole", "Whole name", OptionFieldKind.Checkbox)
                ])
            ])
        ])
    {
        public sealed class Options
        {
            public int Count { get; init; } = 1;
            public string Notes { get; init; } = "";
            public IReadOnlyList<string> Tags { get; init; } = [];
            public IReadOnlyList<Pair> Pairs { get; init; } = [];
            public int Extra { get; init; }
        }

        public sealed class Pair
        {
            public string Find { get; init; } = "";
            public string Replace { get; init; } = "";
            public bool Whole { get; init; }
        }

        protected override PathParts Apply(PluginContext context, Options options)
        {
            var stem = context.Path.Stem;
            foreach (var pair in options.Pairs.Where(p => p.Find.Length > 0))
                stem = pair.Whole && stem == pair.Find ? pair.Replace : stem.Replace(pair.Find, pair.Replace);
            return context.Path with
            {
                Stem = $"{stem}-{options.Count}-{string.Join('+', options.Tags)}-{options.Pairs.Count}-{options.Notes.Replace('\n', ' ')}"
            };
        }
    }

    private sealed class FormPlugin(IReadOnlyList<OptionField> fields)
        : RenamePlugin<FormPlugin.Options>("form", 1, "Form", "Form only", [new(fields)])
    {
        public sealed class Options;

        protected override PathParts Apply(PluginContext context, Options options) => context.Path;
    }
}
