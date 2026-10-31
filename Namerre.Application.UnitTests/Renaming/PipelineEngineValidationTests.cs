using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Metadata;
using Namerre.Domain.Renaming;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class PipelineEngineValidationTests
{
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();
    private readonly HashSet<string> existing =
    [
        "src/a.txt", "src/b.txt", "src/One.txt", "src/one.TXT", "src/name.txt", "src/MiXeD.txt",
        "src/taken.txt", "src/show.mkv", "src/movie.mkv", "README"
    ];
    private readonly PipelineEngine engine;

    public PipelineEngineValidationTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(call => existing.Contains((string)call[0]));
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => string.Equals((string)call[0], (string)call[1], StringComparison.OrdinalIgnoreCase));
        fileSystem.When(fs => fs.Validate(Arg.Any<string>()))
            .Do(call => NamingRules.ValidateRelativePath((string)call[0]));
        engine = TestPipeline.Create(metadata, fileSystem);
    }

    [Fact]
    public async Task MissingSourceIsReportedWithoutRunningPlugins()
    {
        var preview = await engine.Preview(new([new("a", "missing/a.txt")],
            [Plugin("p", "case", new { mode = "upper" })]), default);

        var file = Assert.Single(preview.Files);
        Assert.False(preview.CanPrepare);
        Assert.Equal("SourceMissing", Assert.Single(file.Issues).Code);
        Assert.Empty(file.Steps);
        Assert.Equal("missing/a.txt", file.TargetPath);
    }

    [Fact]
    public async Task InvalidSourcePathIsReportedAsConfigurationIssue()
    {
        var preview = await engine.Preview(new([new("a", "../escape.txt")], []), default);

        Assert.Equal("InvalidConfiguration", Assert.Single(Assert.Single(preview.Files).Issues).Code);
        fileSystem.DidNotReceive().Exists("../escape.txt");
    }

    [Fact]
    public async Task FilesResolvingToTheSameTargetAreBlocked()
    {
        var preview = await engine.Preview(new([new("a", "src/One.txt"), new("b", "src/one.TXT")],
            [Plugin("p", "case", new { mode = "lower" })]), default);

        Assert.False(preview.CanPrepare);
        Assert.All(preview.Files, file =>
        {
            Assert.Contains(file.Issues, issue => issue.Code == "DuplicateSource");
            Assert.Contains(file.Issues, issue => issue.Code == "DuplicateTarget");
        });
    }

    [Fact]
    public async Task DistinctTargetsInTheSameFolderArePrepared()
    {
        var preview = await engine.Preview(new([new("a", "src/a.txt"), new("b", "src/b.txt")],
            [Plugin("p", "case", new { mode = "upper" })]), default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(["src/A.txt", "src/B.txt"], preview.Files.Select(f => f.TargetPath));
        Assert.All(preview.Files, file => Assert.False(file.Unchanged));
    }

    [Fact]
    public async Task ExistingDestinationIsNeverOverwritten()
    {
        var preview = await engine.Preview(new([new("a", "src/a.txt")],
            [Plugin("p", "literal", new { search = "a", replacement = "taken" })]), default);

        var file = Assert.Single(preview.Files);
        Assert.Equal("src/taken.txt", file.TargetPath);
        Assert.Equal("TargetExists", Assert.Single(file.Issues).Code);
    }

    [Fact]
    public async Task DestinationVacatedByAnotherFileInTheBatchIsAllowed()
    {
        // b -> c while a -> b: the destination of "a" is moved away within the same run.
        var preview = await engine.Preview(new([new("a", "src/a.txt"), new("b", "src/b.txt")],
            [
                Plugin("first", "literal", new { search = "b", replacement = "c" }),
                Plugin("second", "literal", new { search = "a", replacement = "b" })
            ]), default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(["src/b.txt", "src/c.txt"], preview.Files.Select(f => f.TargetPath));
    }

    [Fact]
    public async Task UnchangedFilesDoNotConflictWithThemselves()
    {
        var preview = await engine.Preview(new([new("a", "src/a.txt")],
            [Plugin("p", "literal", new { search = "zzz", replacement = "y" })]), default);

        var file = Assert.Single(preview.Files);
        Assert.True(preview.CanPrepare);
        Assert.True(file.Unchanged);
        Assert.Single(file.Steps);
    }

    [Fact]
    public async Task CrossDeviceMovesAreReported()
    {
        fileSystem.When(fs => fs.CheckSameDevice("src/a.txt", "src/A.txt"))
            .Do(_ => throw new RenameException("CrossDevice", "Different device."));

        var preview = await engine.Preview(new([new("a", "src/a.txt")],
            [Plugin("p", "case", new { mode = "upper" })]), default);

        Assert.Equal("CrossDevice", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Theory]
    [InlineData("literal", "{\"search\":\"name\",\"replacement\":\"\"}", "InvalidName")]
    [InlineData("regex", "{\"pattern\":\".+\",\"replacement\":\"a/b\"}", "InvalidName")]
    [InlineData("regex", "{\"pattern\":\".+\",\"replacement\":\"a\\\\b\"}", "InvalidName")]
    [InlineData("case", "{\"mode\":\"sideways\"}", "InvalidConfiguration")]
    [InlineData("literal", "{\"search\":\"n\",\"replacement\":\"x\",\"scope\":\"some\"}", "InvalidConfiguration")]
    [InlineData("literal", "{\"search\":\"n\",\"replacement\":\"x\",\"ignoreCase\":\"yes\"}", "InvalidConfiguration")]
    [InlineData("literal", "{\"search\":5,\"replacement\":\"x\"}", "InvalidConfiguration")]
    public async Task InvalidPluginOutputIsReportedOnItsStep(string type, string options, string code)
    {
        var plugin = new PluginInstance("p", type, 1, true, JsonDocument.Parse(options).RootElement.Clone());

        var preview = await engine.Preview(new([new("a", "src/name.txt")], [plugin]), default);

        var file = Assert.Single(preview.Files);
        var step = Assert.Single(file.Steps);
        Assert.Equal(code, step.Error!.Code);
        Assert.Null(step.OutputPath);
        Assert.Equal(code, Assert.Single(file.Issues).Code);
    }

    [Fact]
    public async Task OptionDefaultsApplyWhenKeysAreAbsent()
    {
        var preview = await engine.Preview(new([new("a", "src/MiXeD.txt")],
            [Plugin("p", "case", new { })]), default);

        Assert.Equal("src/mixed.txt", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task FilesWithoutFolderOrExtensionAreRenamed()
    {
        var preview = await engine.Preview(new([new("a", "README")],
            [Plugin("p", "case", new { mode = "title" })]), default);

        Assert.Equal("Readme", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task TvMatchesUseTheEpisodeTemplateByDefault()
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "" });
        var selection = Selection(plugin, "src/show.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(1, "tv", "Show", "2020", 1, 2, "Pilot"));

        var preview = await engine.Preview(new([new("file", "src/show.mkv")], [plugin], [selection]), default);

        Assert.Equal("src/Show - S01E02 - Pilot.mkv", Assert.Single(preview.Files).TargetPath);
    }

    [Theory]
    [InlineData("{title} {episodeTitle}")]
    [InlineData("{title} {episode}")]
    [InlineData("{title} {seasonNumber}")]
    public async Task EpisodeFieldsAreRequiredWhenTemplatesUseThem(string template)
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = template });
        var selection = Selection(plugin, "src/movie.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(1, "movie", "Movie", "2020", null, null, null));

        var preview = await engine.Preview(new([new("file", "src/movie.mkv")], [plugin], [selection]), default);

        Assert.Equal("InvalidTemplate", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Theory]
    [InlineData("{title} ({year}) [tmdbid-{tmdbId}]", "src/Show (2008) [tmdbid-1396].mkv")]
    [InlineData("{originalTitle}", "src/Shō- One.mkv")]
    [InlineData("{title} {airDate}", "src/Show 2010-03-21.mkv")]
    [InlineData("{title} ({showYear})", "src/Show (2008).mkv")]
    [InlineData("{title} S{season:0}E{episode:000}", "src/Show S3E007.mkv")]
    [InlineData("{seasonNumber:00}x{episode:0}", "src/03x7.mkv")]
    [InlineData("{tmdbId:000000}", "src/001396.mkv")]
    public async Task ExtendedFieldsAndNumberFormatsRender(string template, string? expected)
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = template });
        var selection = Selection(plugin, "src/show.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(1396, "tv", "Show", "2008", 3, 7, "Pilot",
            OriginalTitle: "Shō: One", AirDate: "2010-03-21", ShowYear: "2008"));

        var preview = await engine.Preview(new([new("file", "src/show.mkv")], [plugin], [selection]), default);

        var file = Assert.Single(preview.Files);
        if (expected is null) Assert.Equal("InvalidTemplate", Assert.Single(file.Issues).Code);
        else Assert.Equal(expected, file.TargetPath);
    }

    [Theory]
    [InlineData("{title} {airDate}")]
    [InlineData("{title} {showYear}")]
    [InlineData("{title} {originalTitle}")]
    [InlineData("{title} {episode:000}")]
    [InlineData("{title:00}")]
    [InlineData("{tmdbId:}")]
    [InlineData("{tmdbId:0000000}")]
    [InlineData("{tmdbId:#0}")]
    [InlineData("{rating}")]
    public async Task UnavailableFieldsAndInvalidFormatsAreRejected(string template)
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = template });
        var selection = Selection(plugin, "src/movie.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(1, "movie", "Movie", "2020", null, null, null));

        var preview = await engine.Preview(new([new("file", "src/movie.mkv")], [plugin], [selection]), default);

        Assert.Equal("InvalidTemplate", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Fact]
    public async Task MovieMatchesRenderTheirReleaseDateAndOriginalTitle()
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{originalTitle} [{airDate}] {tmdbId}" });
        var selection = Selection(plugin, "src/movie.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(348, "movie", "Alien", "1979", null, null, null,
            OriginalTitle: "Alien", AirDate: "1979-05-25"));

        var preview = await engine.Preview(new([new("file", "src/movie.mkv")], [plugin], [selection]), default);

        Assert.Equal("src/Alien [1979-05-25] 348.mkv", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task MissingYearRendersAsEmptyText()
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{title}{year}" });
        var selection = Selection(plugin, "src/movie.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(1, "movie", "Movie", null, null, null, null));

        var preview = await engine.Preview(new([new("file", "src/movie.mkv")], [plugin], [selection]), default);

        Assert.Equal("src/Movie.mkv", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task OverlongTemplatesAreRejected()
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = new string('x', 1001) });
        var selection = Selection(plugin, "src/movie.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(1, "movie", "Movie", "2020", null, null, null));

        var preview = await engine.Preview(new([new("file", "src/movie.mkv")], [plugin], [selection]), default);

        Assert.Equal("InvalidTemplate", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Theory]
    [InlineData("...")]
    [InlineData("  ")]
    public async Task MetadataThatCannotFormANameIsRejected(string title)
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{title}" });
        var selection = Selection(plugin, "src/movie.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(1, "movie", title, "2020", null, null, null));

        var preview = await engine.Preview(new([new("file", "src/movie.mkv")], [plugin], [selection]), default);

        Assert.Equal("InvalidMetadataName", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Fact]
    public async Task MetadataBracesAreReplacedSoTheyCannotFormTemplateFields()
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{title}" });
        var selection = Selection(plugin, "src/movie.mkv");
        metadata.Resolve(selection).Returns(new TmdbMatch(1, "movie", "A {year} B", "2020", null, null, null));

        var preview = await engine.Preview(new([new("file", "src/movie.mkv")], [plugin], [selection]), default);

        Assert.Equal("src/A (year) B.mkv", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task MetadataResolutionFailuresAreReportedOnTheStep()
    {
        var plugin = Plugin("tmdb", "tmdb", new { nameTemplate = "{title}" });
        var selection = Selection(plugin, "src/movie.mkv");
        metadata.Resolve(selection).Throws(new RenameException("MetadataExpired", "Search again."));

        var preview = await engine.Preview(new([new("file", "src/movie.mkv")], [plugin], [selection]), default);

        Assert.Equal("MetadataExpired", Assert.Single(Assert.Single(preview.Files).Steps).Error!.Code);
    }

    [Fact]
    public void OptionsHashBindsOnlyTheLanguageAndDefaultsToEnglish()
    {
        static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        Assert.Equal(Sha("en-US"), TmdbPlugin.OptionsHash(JsonSerializer.SerializeToElement(new { })));
        Assert.Equal(Sha("en-US"), TmdbPlugin.OptionsHash(JsonDocument.Parse("{\"language\":null}").RootElement));
        Assert.Equal(Sha("es-ES"), TmdbPlugin.OptionsHash(JsonSerializer.SerializeToElement(
            new { language = "es-ES", nameTemplate = "{title}" })));
        Assert.Equal(
            TmdbPlugin.OptionsHash(JsonSerializer.SerializeToElement(new { language = "fr", nameTemplate = "a" })),
            TmdbPlugin.OptionsHash(JsonSerializer.SerializeToElement(new { language = "fr", nameTemplate = "b" })));
    }

    [Fact]
    public void OptionsHashMatchesTheFrontendFixture()
        => Assert.Equal("5c49f88dafe66e0ecdca8f682ae0b38c38ccd3ad464e3358e899beca88c18560",
            TmdbPlugin.OptionsHash(JsonSerializer.SerializeToElement(new { language = "en-US" })));

    public static TheoryData<PipelineRequest, string> InvalidRequests => new()
    {
        { new([], []), "BatchLimit" },
        { new(null!, []), "BatchLimit" },
        { new([new("a", "src/a.txt")], null!), "BatchLimit" },
        { new([new(" ", "src/a.txt")], []), "DuplicateFile" },
        { new([new(new string('x', 101), "src/a.txt")], []), "DuplicateFile" },
        { new([new("a", null!)], []), "DuplicateFile" },
        { new([new("a", "src/a.txt"), new("b", "src/a.txt")], []), "DuplicateFile" },
        { new([new("a", "src/a.txt"), new("a", "src/b.txt")], []), "DuplicateFile" },
        { new([new("a", "src/a.txt")], [Plugin("", "case", new { })]), "DuplicateInstance" },
        { new([new("a", "src/a.txt")], [Plugin(new string('x', 101), "case", new { })]), "DuplicateInstance" },
        { new([new("a", "src/a.txt")], [Plugin("p", "unknown", new { })]), "InvalidPlugin" },
        { new([new("a", "src/a.txt")], [new("p", "case", 1, true, JsonSerializer.SerializeToElement("text"))]), "InvalidPlugin" },
        { new([new("a", "src/a.txt")], [Plugin("p", "regex", new { pattern = new string('x', 8200) })]), "InvalidPlugin" },
        {
            new([new("a", "src/a.txt")],
                [Plugin("t1", "tmdb", new { }), Plugin("t2", "tmdb", new { }) with { Enabled = false }]),
            "SingleInstancePlugin"
        },
        {
            new([new("a", "src/a.txt")], [], Enumerable.Range(0, NamingRules.MaxFiles * NamingRules.MaxPlugins + 1)
                .Select(i => new MetadataSelection("a", i.ToString(), "src/a.txt", "", "")).ToArray()),
            "BatchLimit"
        },
        { new([new("a", "src/a.txt")], [], Overrides: [new("b", "src/x.txt")]), "InvalidOverride" },
        { new([new("a", "src/a.txt")], [], Overrides: [new("a", null!)]), "InvalidOverride" },
        { new([new("a", "src/a.txt")], [], Overrides: [new("a", new string('x', 2049))]), "InvalidOverride" },
        { new([new("a", "src/a.txt")], [], Overrides: [new("a", "src/x.txt"), new("a", "src/y.txt")]), "InvalidOverride" }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void ValidateRequestRejectsMalformedRequests(PipelineRequest request, string code)
        => Assert.Equal(code, Assert.Throws<RenameException>(() => engine.ValidateRequest(request)).Code);

    [Fact]
    public async Task OverridesReplaceTheComputedPathIncludingFolderAndExtension()
    {
        var preview = await engine.Preview(new([new("a", "src/a.txt"), new("b", "src/b.txt")],
            [Plugin("p", "case", new { mode = "upper" })], Overrides: [new("a", "other/Manual name.md")]), default);

        Assert.True(preview.CanPrepare);
        var file = preview.Files[0];
        Assert.Equal("other/Manual name.md", file.TargetPath);
        Assert.False(file.Unchanged);
        var step = file.Steps[^1];
        Assert.Equal(PipelineEngine.OverrideStepId, step.TypeId);
        Assert.Equal(("src/A.txt", "other/Manual name.md"), (step.InputPath, step.OutputPath));
        Assert.Equal("src/B.txt", preview.Files[1].TargetPath);
    }

    [Fact]
    public async Task OverrideBackToTheSourceLeavesTheFileUnchanged()
    {
        var preview = await engine.Preview(new([new("a", "src/a.txt")],
            [Plugin("p", "case", new { mode = "upper" })], Overrides: [new("a", "src/a.txt")]), default);

        Assert.True(Assert.Single(preview.Files).Unchanged);
    }

    [Theory]
    [InlineData("../escape.txt", "InvalidConfiguration")]
    [InlineData("src/", "InvalidConfiguration")]
    [InlineData("src/taken.txt", "TargetExists")]
    [InlineData("src/b.txt", "DuplicateTarget")]
    public async Task InvalidOrConflictingOverridesBlockPreparation(string target, string code)
    {
        var preview = await engine.Preview(new([new("a", "src/a.txt"), new("b", "src/b.txt")], [],
            Overrides: [new("a", target)]), default);

        Assert.False(preview.CanPrepare);
        Assert.Contains(preview.Files[0].Issues, issue => issue.Code == code);
    }

    [Fact]
    public void ValidateRequestAcceptsDisabledPluginsAndTheMaximumSelectionCount()
        => engine.ValidateRequest(new([new("a", "src/a.txt")],
            [Plugin("p", "tmdb", new { }) with { Enabled = false }],
            Enumerable.Range(0, NamingRules.MaxFiles * NamingRules.MaxPlugins)
                .Select(i => new MetadataSelection("a", i.ToString(), "src/a.txt", "", "")).ToArray()));

    [Fact]
    public void ValidateRequestAcceptsRepeatedPluginsThatAllowSeveralInstances()
        => engine.ValidateRequest(new([new("a", "src/a.txt")],
            [Plugin("c1", "case", new { }), Plugin("t", "tmdb", new { }), Plugin("c2", "case", new { })]));

    [Fact]
    public void OnlyTheTmdbPluginIsLimitedToOneInstance()
        => Assert.Equal(["tmdb"], engine.Catalog.Where(d => d.SingleInstance).Select(d => d.TypeId));

    private static MetadataSelection Selection(PluginInstance plugin, string input)
        => new("file", plugin.InstanceId, input, TmdbPlugin.OptionsHash(plugin.Options), "confirmed");

    private static PluginInstance Plugin(string id, string type, object options)
        => new(id, type, 1, true, JsonSerializer.SerializeToElement(options));
}
