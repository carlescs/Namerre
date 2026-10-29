using System.Text.Json;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Metadata;
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
        var engine = TestPipeline.Create(metadata, fileSystem);
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
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(Request(plugins), default);
        var file = Assert.Single(preview.Files);
        Assert.Equal("folder/last.BETA.MKV", file.TargetPath);
        Assert.Equal(["first", "second", "third"], file.Steps.Select(s => s.InstanceId));
        Assert.Equal(file.Steps[0].OutputPath, file.Steps[1].InputPath);
        var reordered = await TestPipeline.Create(metadata, fileSystem).Preview(Request(plugins.Reverse().ToArray()), default);
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
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(Request([
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
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(new(
            [new("file", input)], [Plugin("r", "regex", new { pattern = "(a+)+$", replacement = "" })]), default);
        Assert.Equal("RegexTimeout", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Fact]
    public async Task MetadataSurvivesTemplateEditsButRequiresExactFileInstanceInputAndLanguageBinding()
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{title} ({year})" });
        var engine = TestPipeline.Create(metadata, fileSystem);
        var absent = await engine.Preview(Request([plugin]), default);
        Assert.Equal("MetadataRequired", Assert.Single(Assert.Single(absent.Files).Issues).Code);
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", "Resolved", "2000", null, null, null));
        var confirmed = await engine.Preview(Request([plugin]) with { Selections = [selection] }, default);
        Assert.Equal("folder/Resolved (2000).MKV", Assert.Single(confirmed.Files).TargetPath);
        var changed = await engine.Preview(Request([plugin with
        {
            Options = JsonSerializer.SerializeToElement(new { nameTemplate = "{title}" })
        }]) with { Selections = [selection] }, default);
        Assert.True(changed.CanPrepare);
        Assert.Equal("folder/Resolved.MKV", Assert.Single(changed.Files).TargetPath);
        var folderChanged = await engine.Preview(Request([plugin with
        {
            Options = JsonSerializer.SerializeToElement(new {
                nameTemplate = "{title}", changeFolder = true, folderTemplate = "{year}"
            })
        }]) with { Selections = [selection] }, default);
        Assert.True(folderChanged.CanPrepare);
        Assert.Equal("2000/Resolved.MKV", Assert.Single(folderChanged.Files).TargetPath);
        var baseFolderChanged = await engine.Preview(Request([plugin with
        {
            Options = JsonSerializer.SerializeToElement(new {
                nameTemplate = "{title}", changeFolder = true, baseFolder = "Library/Movies",
                folderTemplate = "{year}"
            })
        }]) with { Selections = [selection] }, default);
        Assert.True(baseFolderChanged.CanPrepare);
        Assert.Equal("Library/Movies/2000/Resolved.MKV", Assert.Single(baseFolderChanged.Files).TargetPath);
        var languageChanged = await engine.Preview(Request([plugin with
        {
            Options = JsonSerializer.SerializeToElement(new { nameTemplate = "{title}", language = "fr" })
        }]) with { Selections = [selection] }, default);
        Assert.Equal("MetadataRequired", Assert.Single(Assert.Single(languageChanged.Files).Issues).Code);
        foreach (var invalidSelection in new[] {
            selection with { FileId = "other" },
            selection with { InstanceId = "other" },
            selection with { InputPath = "other.mkv" }
        })
        {
            var invalid = await engine.Preview(Request([plugin]) with { Selections = [invalidSelection] }, default);
            Assert.Equal("MetadataRequired", Assert.Single(Assert.Single(invalid.Files).Issues).Code);
        }
        metadata.Received(4).Resolve(selection);
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
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "tv", "Show: A/B\\C?", "2000",
            2, 3, "Episode <one> \"two\"|*\n{end}. "));
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(Request([
            plugin, Plugin("case", "case", new { mode = "lower" })
        ]) with { Selections = [selection] }, default);
        Assert.True(preview.CanPrepare);
        Assert.Equal("Show- A-B-C- (2000)/show- a-b-c- - s02e03 - episode -one- -two----(end).MKV",
            Assert.Single(preview.Files).TargetPath);
    }

    [Theory]
    [InlineData("", true, "Resolved (2000)/Resolved.MKV")]
    [InlineData("Movies", true, "Movies/Resolved (2000)/Resolved.MKV")]
    [InlineData("Library/Movies", true, "Library/Movies/Resolved (2000)/Resolved.MKV")]
    [InlineData("Library/Movies", false, "folder/Resolved.MKV")]
    [InlineData("../ignored", false, "folder/Resolved.MKV")]
    public async Task FolderChangesStartUnderTheOptionalBaseFolder(
        string baseFolder, bool changeFolder, string expected)
    {
        fileSystem.When(fs => fs.Validate(Arg.Any<string>()))
            .Do(call => NamingRules.ValidateRelativePath((string)call[0]));
        var plugin = Plugin("tmdb", "tmdb", new
        {
            nameTemplate = "{title}", changeFolder, baseFolder, folderTemplate = "{title} ({year})"
        });
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", "Resolved", "2000", null, null, null));

        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
            Request([plugin]) with { Selections = [selection] }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(expected, Assert.Single(preview.Files).TargetPath);
    }

    [Theory]
    [InlineData("Ep.en.hi", ".srt", "Resolved.en.hi")]
    [InlineData("Ep.es", ".srt", "Resolved.es")]
    [InlineData("Ep.pt-BR", ".vtt", "Resolved.pt-BR")]
    [InlineData("Ep.eng.forced", ".SRT", "Resolved.eng.forced")]
    [InlineData("Ep.Forced", ".ass", "Resolved.Forced")]
    [InlineData("Ep.en.SDH", ".ssa", "Resolved.en.SDH")]
    [InlineData("Ep.Part.Two", ".srt", "Resolved")]
    [InlineData("Ep.en.Part", ".srt", "Resolved")]
    [InlineData("en.hi", ".srt", "Resolved")]
    [InlineData("Ep.en.hi", ".mkv", "Resolved")]
    [InlineData("Ep.en", ".nfo", "Resolved")]
    public async Task SubtitleLanguageAndFlagSuffixesAreKept(string stem, string extension, string expectedStem)
    {
        var path = "folder/" + stem + extension;
        fileSystem.Exists(path).Returns(true);
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{title}" });
        var selection = new MetadataSelection("file", "tmdb", path,
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", "Resolved", "2000", null, null, null));

        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
            Request([plugin], path) with { Selections = [selection] }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal("folder/" + expectedStem + extension, Assert.Single(preview.Files).TargetPath);
    }

    [Theory]
    [InlineData(true, "Season {season}", 2, "TV/Show/Season 02/Show - S02E05.MKV")]
    [InlineData(true, "Season {seasonNumber}", 2, "TV/Show/Season 2/Show - S02E05.MKV")]
    [InlineData(true, "S{season}", 2, "TV/Show/S02/Show - S02E05.MKV")]
    [InlineData(true, "Season {season}", 0, "TV/Show/Season 00/Show - S00E05.MKV")]
    [InlineData(false, "Season {season}", 2, "TV/Show/Show - S02E05.MKV")]
    public async Task SeasonFoldersNestInsideTheShowFolderUsingTheirTemplate(
        bool seasonFolder, string seasonFolderTemplate, int season, string expected)
    {
        var plugin = Plugin("tmdb", "tmdb", new
        {
            nameTemplate = "{title} - S{season}E{episode}", changeFolder = true, baseFolder = "TV",
            folderTemplate = "{title}", seasonFolder, seasonFolderTemplate
        });
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "tv", "Show", "2000", season, 5, "Pilot"));

        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
            Request([plugin]) with { Selections = [selection] }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(expected, Assert.Single(preview.Files).TargetPath);
    }

    [Theory]
    [InlineData(false, "movie", "folder/Resolved.MKV")]
    [InlineData(true, "movie", "Resolved (2000)/Resolved.MKV")]
    public async Task SeasonFoldersRequireFolderChangesAndAnEpisodeMatch(
        bool changeFolder, string mediaType, string expected)
    {
        var plugin = Plugin("tmdb", "tmdb", new
        {
            nameTemplate = "{title}", changeFolder, seasonFolder = true, seasonFolderTemplate = "Season {season}"
        });
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, mediaType, "Resolved", "2000", null, null, null));

        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
            Request([plugin]) with { Selections = [selection] }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(expected, Assert.Single(preview.Files).TargetPath);
    }

    [Theory]
    [InlineData("Season/{season}")]
    [InlineData("{unknown}")]
    [InlineData("Season {season")]
    public async Task InvalidSeasonFolderTemplatesBlockRenaming(string seasonFolderTemplate)
    {
        var plugin = Plugin("tmdb", "tmdb", new
        {
            changeFolder = true, folderTemplate = "{title}", seasonFolder = true, seasonFolderTemplate
        });
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "tv", "Show", "2000", 1, 2, "Pilot"));

        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
            Request([plugin]) with { Selections = [selection] }, default);

        Assert.False(preview.CanPrepare);
        Assert.Equal("InvalidTemplate", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:\\Movies")]
    [InlineData("Movies\\Archive")]
    [InlineData("Movies/../Archive")]
    [InlineData("Movies/")]
    [InlineData("Movies//Archive")]
    [InlineData("Movies/CON")]
    [InlineData(" ")]
    public async Task InvalidBaseFoldersBlockRenaming(string baseFolder)
    {
        fileSystem.When(fs => fs.Validate(Arg.Any<string>()))
            .Do(call => NamingRules.ValidateRelativePath((string)call[0]));
        var plugin = Plugin("tmdb", "tmdb", new { changeFolder = true, baseFolder });
        var selection = new MetadataSelection("file", "tmdb", "folder/alpha.beta.MKV",
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", "Resolved", "2000", null, null, null));

        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
            Request([plugin]) with { Selections = [selection] }, default);

        Assert.False(preview.CanPrepare);
        Assert.Equal("InvalidConfiguration", Assert.Single(Assert.Single(preview.Files).Issues).Code);
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
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", title, "2000", null, null, null));
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
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
            TmdbPlugin.OptionsHash(plugin.Options), "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", "A/B", "2000", null, null, null));
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
            Request([plugin]) with { Selections = [selection] }, default);
        Assert.Equal(code, Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Fact]
    public async Task RejectsLimitsDuplicateIdsAndVersionsBeforeProcessing()
    {
        var engine = TestPipeline.Create(metadata, fileSystem);
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
        var engine = TestPipeline.Create(metadata, fileSystem);
        var preview = await engine.Preview(new(files, plugins), default);
        Assert.True(preview.CanPrepare);
        Assert.Equal(200, preview.Files.Count);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.Preview(new(files, plugins),
            new CancellationToken(true)));
    }

    [Theory]
    [InlineData("new-", "", "folder/new-alpha.beta.MKV")]
    [InlineData("", " (copy)", "folder/alpha.beta (copy).MKV")]
    [InlineData("[", "]", "folder/[alpha.beta].MKV")]
    [InlineData("", "", "folder/alpha.beta.MKV")]
    public async Task AffixAddsTextAroundTheNameAndKeepsFolderAndExtension(string prefix, string suffix, string expected)
    {
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(Request([
            Plugin("affix", "affix", new { prefix, suffix })]), default);

        var file = Assert.Single(preview.Files);
        Assert.Equal(expected, file.TargetPath);
        Assert.Empty(file.Issues);
    }

    [Fact]
    public async Task AffixRunsInUserOrderWithOtherPlugins()
    {
        var plugins = new[]
        {
            Plugin("affix", "affix", new { prefix = "x-" }),
            Plugin("case", "case", new { mode = "upper" })
        };
        var engine = TestPipeline.Create(metadata, fileSystem);

        var preview = await engine.Preview(Request(plugins), default);
        var reordered = await engine.Preview(Request(plugins.Reverse().ToArray()), default);

        Assert.Equal("folder/X-ALPHA.BETA.MKV", Assert.Single(preview.Files).TargetPath);
        Assert.Equal("folder/x-ALPHA.BETA.MKV", Assert.Single(reordered.Files).TargetPath);
    }

    [Theory]
    [InlineData("sub/", "")]
    [InlineData("", "\\x")]
    public async Task AffixCannotIntroducePathSeparators(string prefix, string suffix)
    {
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(Request([
            Plugin("affix", "affix", new { prefix, suffix })]), default);

        Assert.Equal("InvalidName", Assert.Single(Assert.Single(preview.Files).Issues).Code);
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
    private static PipelineRequest Request(IReadOnlyList<PluginInstance> plugins, string path)
        => new([new("file", path)], plugins);
    private static PluginInstance Plugin(string id, string type, object options)
        => new(id, type, 1, true, JsonSerializer.SerializeToElement(options));
}
