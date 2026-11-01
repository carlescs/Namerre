using System.Text.Json;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class PipelineEngineConditionTests
{
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public PipelineEngineConditionTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(call => (string)call[0] is "in/movie.mkv" or "in/movie.en.srt");
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => (string)call[0] == (string)call[1]);
    }

    [Fact]
    public async Task ExtensionConditionSkipsOtherFilesWithATrace()
    {
        var preview = await Preview(Upper(new StepCondition(Extensions: [".SRT", "ass"])));
        var video = preview.Files.Single(f => f.Id == "video");
        var subtitle = preview.Files.Single(f => f.Id == "subtitle");
        Assert.Equal("in/movie.mkv", video.TargetPath);
        Assert.True(Assert.Single(video.Steps).Skipped);
        Assert.Equal("in/movie.mkv", video.Steps[0].OutputPath);
        Assert.Equal("in/MOVIE.EN.srt", subtitle.TargetPath);
        Assert.False(Assert.Single(subtitle.Steps).Skipped);
        Assert.True(preview.CanPrepare);
    }

    [Fact]
    public async Task InvertedPatternAppliesToFilesThatDoNotMatch()
    {
        var preview = await Preview(Upper(new StepCondition(Pattern: @"\.en$|\.en\.", Invert: true)));
        Assert.Equal("in/MOVIE.mkv", preview.Files.Single(f => f.Id == "video").TargetPath);
        Assert.Equal("in/movie.en.srt", preview.Files.Single(f => f.Id == "subtitle").TargetPath);
    }

    [Fact]
    public async Task PatternIsCaseSensitiveOnRequest()
    {
        var preview = await Preview(Upper(new StepCondition(Pattern: "^MOVIE", IgnoreCase: false)));
        Assert.All(preview.Files, file => Assert.True(file.Unchanged));
    }

    [Fact]
    public async Task ConditionSeesTheOutputOfEarlierSteps()
    {
        var rename = new PluginInstance("rename", "literal", 1, true,
            JsonSerializer.SerializeToElement(new { search = "movie", replacement = "film" }));
        var preview = await Preview([rename, .. Upper(new StepCondition(Pattern: "^film"))]);
        Assert.Equal("in/FILM.mkv", preview.Files.Single(f => f.Id == "video").TargetPath);
    }

    [Fact]
    public async Task SkippedTmdbStepNeedsNoMatch()
    {
        var tmdb = new PluginInstance("tmdb", "tmdb", 1, true, JsonSerializer.SerializeToElement(new { }),
            new StepCondition(Extensions: ["mkv"]));
        var preview = await Preview([tmdb]);
        Assert.Equal("MetadataRequired", preview.Files.Single(f => f.Id == "video").Issues.Single().Code);
        Assert.True(preview.Files.Single(f => f.Id == "subtitle").Steps.Single().Skipped);
        Assert.Empty(preview.Files.Single(f => f.Id == "subtitle").Issues);
    }

    [Fact]
    public async Task EmptyConditionAppliesEverywhere()
    {
        var preview = await Preview(Upper(new StepCondition(Extensions: [], Pattern: "")));
        Assert.All(preview.Files, file => Assert.False(file.Unchanged));
    }

    [Theory]
    [InlineData(new[] { "" }, null)]
    [InlineData(new[] { "a/b" }, null)]
    [InlineData(new[] { "tar.gz" }, null)]
    [InlineData(null, "[")]
    public async Task InvalidConditionsRejectTheRequest(string[]? extensions, string? pattern)
    {
        var exception = await Assert.ThrowsAsync<RenameException>(
            () => Preview(Upper(new StepCondition(extensions, pattern))));
        Assert.Equal("InvalidCondition", exception.Code);
    }

    [Fact]
    public async Task TooManyExtensionsOrLongPatternsAreRejected()
    {
        await Assert.ThrowsAsync<RenameException>(() => Preview(Upper(new StepCondition(
            Enumerable.Range(0, StepCondition.MaxExtensions + 1).Select(i => "e" + i).ToArray()))));
        await Assert.ThrowsAsync<RenameException>(() => Preview(Upper(new StepCondition(
            Pattern: new string('a', StepCondition.MaxPatternLength + 1)))));
    }

    [Fact]
    public async Task CatastrophicPatternTimesOutAsAnIssue()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(true);
        var path = "in/" + new string('a', 200) + "!.mkv";
        var engine = TestPipeline.Create(metadata, fileSystem);
        var preview = await engine.Preview(new PipelineRequest([new("slow", path)],
            [Upper(new StepCondition(Pattern: "^(a+)+$"))[0]]), default);
        Assert.Equal("RegexTimeout", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Fact]
    public void ConditionsSurviveJsonRoundTrips()
    {
        var request = new PipelineRequest([new("video", "in/movie.mkv")], Upper(new StepCondition(["srt"], "x", false, true)));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var copy = JsonSerializer.Deserialize<PipelineRequest>(JsonSerializer.Serialize(request, options), options)!;
        Assert.Equal(["srt"], copy.Plugins[0].Condition!.Extensions!);
        Assert.Equal("x", copy.Plugins[0].Condition!.Pattern);
        Assert.True(copy.Plugins[0].Condition!.Invert);
        Assert.False(copy.Plugins[0].Condition!.IgnoreCase);
        var legacy = JsonSerializer.Deserialize<PluginInstance>(
            """{"instanceId":"a","typeId":"case","version":1,"enabled":true,"options":{}}""", options)!;
        Assert.Null(legacy.Condition);
    }

    private Task<Namerre.Application.Renaming.Previews.PreviewResult> Preview(IReadOnlyList<PluginInstance> plugins)
        => TestPipeline.Create(metadata, fileSystem).Preview(new PipelineRequest(
            [new("video", "in/movie.mkv"), new("subtitle", "in/movie.en.srt")], plugins), default);

    private static PluginInstance[] Upper(StepCondition condition)
        => [new("upper", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }), condition)];
}
