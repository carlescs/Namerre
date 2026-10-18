using System.Text.Json;
using Namerre.Application.Renaming;
using Namerre.Domain.Renaming;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class PipelineEngineTests
{
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public PipelineEngineTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(false);
        fileSystem.Exists("folder/alpha.beta.MKV").Returns(true);
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => (string)call[0] == (string)call[1]);
    }

    [Fact]
    public async Task EmptyPipelineAndDisabledPipelinePreserveOriginal()
    {
        var engine = new PipelineEngine(metadata, fileSystem);
        foreach (var plugins in new IReadOnlyList<PluginInstance>[] { [],
            [Plugin("x", "literal", new { search = "", replacement = "invalid" }) with { Enabled = false }] })
        {
            var preview = await engine.Preview(Request(plugins), default);
            Assert.True(preview.CanPrepare);
            var file = Assert.Single(preview.Files);
            Assert.True(file.Unchanged);
            Assert.Empty(file.Steps);
            Assert.Equal("folder/alpha.beta.MKV", file.TargetPath);
        }
    }

    [Fact]
    public async Task AppliesRepeatedPluginsInUserOrderAndPreservesExtensionAndFolder()
    {
        var plugins = new[]
        {
            Plugin("first", "literal", new { search = "alpha", replacement = "omega" }),
            Plugin("second", "case", new { mode = "upper" }),
            Plugin("third", "literal", new { search = "OMEGA", replacement = "last" })
        };
        var preview = await new PipelineEngine(metadata, fileSystem).Preview(Request(plugins), default);
        var file = Assert.Single(preview.Files);
        Assert.Equal("folder/last.BETA.MKV", file.TargetPath);
        Assert.Equal(["first", "second", "third"], file.Steps.Select(s => s.InstanceId));
        Assert.Equal(file.Steps[0].OutputPath, file.Steps[1].InputPath);
        var reordered = await new PipelineEngine(metadata, fileSystem).Preview(Request(plugins.Reverse().ToArray()), default);
        Assert.Equal("folder/ALPHA.BETA.MKV", Assert.Single(reordered.Files).TargetPath);
    }

    [Theory]
    [InlineData("all", false, "A--")]
    [InlineData("first", false, "A-a")]
    [InlineData("all", true, "---")]
    public void LiteralScopeAndCaseAreExplicit(string scope, bool ignoreCase, string expected)
        => Assert.Equal(expected, NamingRules.Literal("Aaa", "a", "-", ignoreCase, scope));

    [Fact]
    public void RegexSupportsNamedAndNumberedCaptureReplacement()
        => Assert.Equal("name:22", NamingRules.RegexReplace("22-name", @"(\d+)-(?<word>\w+)", "${word}:$1", false));

    [Fact]
    public async Task InvalidPluginBlocksDownstreamWithTrace()
    {
        var preview = await new PipelineEngine(metadata, fileSystem).Preview(Request([
            Plugin("bad", "regex", new { pattern = "[", replacement = "" }),
            Plugin("skipped", "case", new { mode = "lower" })
        ]), default);
        Assert.False(preview.CanPrepare);
        Assert.Equal("InvalidConfiguration", Assert.Single(Assert.Single(preview.Files).Steps).Error!.Code);
    }

    [Fact]
    public async Task RegexTimeoutIsAnExplicitError()
    {
        var input = new string('a', 30000) + "!.mkv";
        fileSystem.Exists(input).Returns(true);
        var preview = await new PipelineEngine(metadata, fileSystem).Preview(new(
            [new("file", input)], [Plugin("r", "regex", new { pattern = "(a+)+$", replacement = "" })]), default);
        Assert.Equal("RegexTimeout", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Fact]
    public async Task MetadataRequiresExactFileInstanceInputAndOptionsBinding()
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{title} ({year})" });
        var engine = new PipelineEngine(metadata, fileSystem);
        var absent = await engine.Preview(Request([plugin]), default);
        Assert.Equal("MetadataRequired", Assert.Single(Assert.Single(absent.Files).Issues).Code);
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            PipelineEngine.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", "Resolved", "2000", null, null, null));
        var confirmed = await engine.Preview(Request([plugin]) with { Selections = [selection] }, default);
        Assert.Equal("folder/Resolved (2000).MKV", Assert.Single(confirmed.Files).TargetPath);
        var changed = await engine.Preview(Request([plugin with
        {
            Options = JsonSerializer.SerializeToElement(new { nameTemplate = "{title}" })
        }]) with { Selections = [selection] }, default);
        Assert.Equal("MetadataRequired", Assert.Single(Assert.Single(changed.Files).Issues).Code);
        metadata.Received(1).Resolve(selection);
    }

    [Fact]
    public async Task MetadataTextIsFilenameSafeBeforeDownstreamStepsAndFolderChanges()
    {
        fileSystem.When(fs => fs.Validate(Arg.Any<string>()))
            .Do(call => NamingRules.ValidateRelativePath((string)call[0]));
        var plugin = Plugin("tmdb", "tmdb", new
        {
            nameTemplate = "",
            changeFolder = true,
            folderTemplate = "{title} ({year})"
        });
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            PipelineEngine.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "tv", "Show: A/B\\C?", "2000",
            2, 3, "Episode <one> \"two\"|*\n{end}. "));
        var preview = await new PipelineEngine(metadata, fileSystem).Preview(Request([
            plugin, Plugin("case", "case", new { mode = "lower" })
        ]) with { Selections = [selection] }, default);
        Assert.True(preview.CanPrepare);
        Assert.Equal("Show- A-B-C- (2000)/show- a-b-c- - s02e03 - episode -one- -two----(end).MKV",
            Assert.Single(preview.Files).TargetPath);
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("aux.txt", "_aux.txt")]
    [InlineData(".namerre-title", "_.namerre-title")]
    [InlineData("Title... ", "Title")]
    [InlineData("Mission: Impossible", "Mission- Impossible")]
    public async Task MetadataOnlyTemplatesHandleReservedNamesAndTrailingCharacters(string title, string expected)
    {
        fileSystem.When(fs => fs.Validate(Arg.Any<string>()))
            .Do(call => NamingRules.ValidateRelativePath((string)call[0]));
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{title}" });
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            PipelineEngine.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", title, "2000", null, null, null));
        var preview = await new PipelineEngine(metadata, fileSystem).Preview(
            Request([plugin]) with { Selections = [selection] }, default);
        Assert.True(preview.CanPrepare);
        Assert.Equal($"folder/{expected}.MKV", Assert.Single(preview.Files).TargetPath);
    }

    [Theory]
    [InlineData("{title}/escape", "InvalidTemplate")]
    [InlineData("{unknown}", "InvalidTemplate")]
    [InlineData("../{title}", "InvalidTemplate")]
    [InlineData("{title}:invalid", "InvalidConfiguration")]
    public async Task MetadataSanitizationDoesNotRelaxTemplateValidation(string template, string code)
    {
        fileSystem.When(fs => fs.Validate(Arg.Any<string>()))
            .Do(call => NamingRules.ValidateRelativePath((string)call[0]));
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = template });
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            PipelineEngine.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", "A/B", "2000", null, null, null));
        var preview = await new PipelineEngine(metadata, fileSystem).Preview(
            Request([plugin]) with { Selections = [selection] }, default);
        Assert.Equal(code, Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Fact]
    public async Task RejectsLimitsDuplicateIdsAndVersionsBeforeProcessing()
    {
        var engine = new PipelineEngine(metadata, fileSystem);
        var files = Enumerable.Range(0, 201).Select(i => new FileInput(i.ToString(), i + ".txt")).ToArray();
        await Assert.ThrowsAsync<RenameException>(() => engine.Preview(new(files, []), default));
        await Assert.ThrowsAsync<RenameException>(() => engine.Preview(Request(
            Enumerable.Range(0, 33).Select(i => Plugin(i.ToString(), "case", new { mode = "upper" })).ToArray()), default));
        await Assert.ThrowsAsync<RenameException>(() => engine.Preview(Request([
            Plugin("same", "case", new { mode = "upper" }), Plugin("same", "case", new { mode = "lower" })]), default));
        await Assert.ThrowsAsync<RenameException>(() => engine.Preview(Request([
            Plugin("bad", "case", new { mode = "lower" }) with { Version = 2 }]), default));
    }

    [Fact]
    public async Task AcceptsExactFileAndPluginLimitsAndCancelsBeforeAnyStep()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(true);
        var files = Enumerable.Range(0, 200).Select(i => new FileInput(i.ToString(), i + ".txt")).ToArray();
        var plugins = Enumerable.Range(0, 32).Select(i => Plugin(i.ToString(), "case", new { mode = "upper" })
            with { Enabled = false }).ToArray();
        var engine = new PipelineEngine(metadata, fileSystem);
        var preview = await engine.Preview(new(files, plugins), default);
        Assert.True(preview.CanPrepare);
        Assert.Equal(200, preview.Files.Count);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.Preview(new(files, plugins),
            new CancellationToken(true)));
    }

    [Theory]
    [InlineData("Queued", "Running", true)]
    [InlineData("Running", "Completed", true)]
    [InlineData("Running", "RecoveryRequired", true)]
    [InlineData("RecoveryRequired", "RecoveryQueued", true)]
    [InlineData("RecoveryQueued", "Recovering", true)]
    [InlineData("Recovering", "Recovered", true)]
    [InlineData("RecoveryRequired", "Completed", false)]
    [InlineData("Completed", "Running", false)]
    [InlineData("Recovered", "Running", false)]
    public void RunTransitionsRequireExplicitRecovery(string from, string to, bool allowed)
        => Assert.Equal(allowed, RunTransitions.Allows(from, to));

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("safe\\bad.txt")]
    [InlineData("safe/CON.txt")]
    [InlineData("safe/bad.")]
    [InlineData("safe/bad ")]
    [InlineData("safe/file:stream")]
    [InlineData(".namerre-stage-123")]
    public void RejectsInvalidMutationNames(string path)
        => Assert.Throws<ArgumentException>(() => NamingRules.ValidateRelativePath(path));

    private static PipelineRequest Request(IReadOnlyList<PluginInstance> plugins)
        => new([new("file", "folder/alpha.beta.MKV")], plugins);
    private static PluginInstance Plugin(string id, string type, object options)
        => new(id, type, 1, true, JsonSerializer.SerializeToElement(options));
}
