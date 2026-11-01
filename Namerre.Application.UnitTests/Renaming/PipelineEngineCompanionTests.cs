using System.Text.Json;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plugins;
using Namerre.Domain.Renaming;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class PipelineEngineCompanionTests
{
    private static readonly string[] Paths =
    [
        "Films/Movie.mkv", "Films/Movie.en.srt", "Films/Movie.nfo", "Films/Movie-poster.jpg",
        "Films/Movie 2.mkv", "Films/Movie 2.srt"
    ];

    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public PipelineEngineCompanionTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(call => Paths.Contains((string)call[0]));
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => (string)call[0] == (string)call[1]);
        fileSystem.When(fs => fs.Validate(Arg.Any<string>()))
            .Do(call => NamingRules.ValidateRelativePath((string)call[0]));
        fileSystem.Facts(Arg.Any<string>()).Returns(new FileFacts(1, DateTimeOffset.UnixEpoch, null));
    }

    [Fact]
    public async Task RelatedFilesTakeTheVideoNameAndKeepTheirSuffixWithoutRunningSteps()
    {
        var preview = await Engine().Preview(Request([
            Plugin("literal", "literal", new { search = "Movie", replacement = "Film" })
        ]), default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(
            ["Films/Film.mkv", "Films/Film.en.srt", "Films/Film.nfo", "Films/Film-poster.jpg",
                "Films/Film 2.mkv", "Films/Film 2.srt"],
            preview.Files.Select(f => f.TargetPath));
        Assert.Equal([null, "0", "0", "0", null, "4"], preview.Files.Select(f => f.CompanionOf));
        var subtitle = preview.Files[1].Steps;
        Assert.Equal(PipelineEngine.CompanionStepId, Assert.Single(subtitle).InstanceId);
    }

    [Fact]
    public async Task WithoutTheOptionEveryFileRunsTheSteps()
    {
        var preview = await Engine().Preview(Request([
            Plugin("affix", "affix", new { prefix = "x-" })
        ]) with { IncludeCompanions = false }, default);

        Assert.All(preview.Files, f => Assert.Null(f.CompanionOf));
        Assert.Equal("Films/x-Movie.en.srt", preview.Files[1].TargetPath);
    }

    [Fact]
    public async Task ConfirmedMatchAndFolderMoveOfTheVideoApplyToItsCompanions()
    {
        var plugin = Plugin("tmdb", "tmdb", new
        {
            nameTemplate = "{title} ({year})", changeFolder = true, baseFolder = "Library",
            folderTemplate = "{title} ({year})"
        });
        var selection = new MetadataSelection("0", "tmdb", "Films/Movie.mkv", TmdbPlugin.OptionsHash(plugin.Options),
            "confirmed");
        metadata.Resolve(selection).Returns(new TmdbMatch(10, "movie", "Resolved", "2000", null, null, null));

        var preview = await Engine().Preview(new([
            new("0", "Films/Movie.mkv"), new("1", "Films/Movie.en.srt"), new("2", "Films/Movie-poster.jpg")
        ], [plugin], [selection], IncludeCompanions: true), default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(
            ["Library/Resolved (2000)/Resolved (2000).mkv", "Library/Resolved (2000)/Resolved (2000).en.srt",
                "Library/Resolved (2000)/Resolved (2000)-poster.jpg"],
            preview.Files.Select(f => f.TargetPath));
        metadata.Received(1).Resolve(Arg.Any<MetadataSelection>());
    }

    [Fact]
    public async Task CompanionsFollowAnOverriddenVideoAndAcceptTheirOwnOverride()
    {
        var preview = await Engine().Preview(Request([]) with
        {
            Overrides = [new("0", "Other/Renamed.mkv"), new("2", "Films/Kept.nfo")]
        }, default);

        Assert.Equal("Other/Renamed.mkv", preview.Files[0].TargetPath);
        Assert.Equal("Other/Renamed.en.srt", preview.Files[1].TargetPath);
        Assert.Equal("Films/Kept.nfo", preview.Files[2].TargetPath);
        Assert.Equal("Other/Renamed-poster.jpg", preview.Files[3].TargetPath);
        Assert.True(preview.Files[4].Unchanged);
        Assert.True(preview.Files[5].Unchanged);
    }

    [Fact]
    public async Task CompanionsWaitForAVideoThatCannotBeRenamed()
    {
        var preview = await Engine().Preview(Request([Plugin("tmdb", "tmdb", new { })]), default);

        Assert.False(preview.CanPrepare);
        Assert.Equal("MetadataRequired", Assert.Single(preview.Files[0].Issues).Code);
        var blocked = preview.Files[1];
        Assert.Equal("CompanionBlocked", Assert.Single(blocked.Issues).Code);
        Assert.Contains("Movie.mkv", blocked.Issues[0].Message);
        Assert.Equal("Films/Movie.en.srt", blocked.TargetPath);
    }

    [Fact]
    public async Task PositionsCountTheVideosOnly()
    {
        var recorder = new RecordingPlugin();

        await new PipelineEngine(fileSystem, [recorder]).Preview(Request([Plugin("p", "record", new { })]), default);

        Assert.Equal(["Films/Movie.mkv", "Films/Movie 2.mkv"], recorder.Contexts.Select(c => c.InputPath));
        Assert.Equal([new(0, 2, 0, 2), new(1, 2, 1, 2)], recorder.Contexts.Select(c => c.Position));
    }

    private sealed class RecordingPlugin() : RenamePlugin<RecordingPlugin.Options>("record", 1, "Record", "Records contexts", [])
    {
        public List<PluginContext> Contexts { get; } = [];

        public sealed class Options;

        protected override PathParts Apply(PluginContext context, Options options)
        {
            Contexts.Add(context);
            return context.Path;
        }
    }

    private PipelineEngine Engine() => TestPipeline.Create(metadata, fileSystem);

    private static PipelineRequest Request(IReadOnlyList<PluginInstance> plugins)
        => new(Paths.Select((path, i) => new FileInput(i.ToString(), path)).ToArray(), plugins,
            IncludeCompanions: true);

    private static PluginInstance Plugin(string id, string type, object options)
        => new(id, type, 1, true, JsonSerializer.SerializeToElement(options));
}
