using System.Text.Json;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Domain.Renaming;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class PipelineEngineCollisionTests
{
    private readonly HashSet<string> existing = new(StringComparer.Ordinal);
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();
    private StringComparison comparison = StringComparison.Ordinal;

    public PipelineEngineCollisionTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(call =>
            existing.Any(path => string.Equals(path, (string)call[0], comparison)));
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => string.Equals((string)call[0], (string)call[1], comparison));
        fileSystem.When(fs => fs.Validate(Arg.Any<string>()))
            .Do(call => NamingRules.ValidateRelativePath((string)call[0]));
        fileSystem.Facts(Arg.Any<string>()).Returns(new FileFacts(1, DateTimeOffset.UnixEpoch, null));
    }

    [Fact]
    public async Task ReportIsTheDefaultAndKeepsCollidingNames()
    {
        Exist("a/one.txt", "a/two.txt");

        var preview = await Engine().Preview(Request(["a/one.txt", "a/two.txt"], Literal("one|two")), default);

        Assert.False(preview.CanPrepare);
        Assert.All(preview.Files, file => Assert.False(file.Numbered));
        Assert.All(preview.Files, file => Assert.Equal("DuplicateTarget", Assert.Single(file.Issues).Code));
    }

    [Fact]
    public async Task CollidingNamesInTheBatchGetNumbersInRequestOrder()
    {
        Exist("a/one.txt", "a/two.txt", "a/three.txt");

        var preview = await Engine().Preview(
            Request(["a/one.txt", "a/two.txt", "a/three.txt"], Literal("one|two|three")) with
            {
                OnCollision = PipelineRequest.CollisionAddNumber
            }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(["a/Name.txt", "a/Name (1).txt", "a/Name (2).txt"], preview.Files.Select(f => f.TargetPath));
        Assert.Equal([false, true, true], preview.Files.Select(f => f.Numbered));
        var step = preview.Files[1].Steps[^1];
        Assert.Equal((PipelineEngine.CollisionStepId, "a/Name.txt", "a/Name (1).txt"),
            (step.TypeId, step.InputPath, step.OutputPath));
    }

    [Fact]
    public async Task ExistingTargetsAndTakenNumbersAreSkipped()
    {
        Exist("a/one.txt", "a/Name.txt", "a/Name (1).txt");

        var preview = await Engine().Preview(Request(["a/one.txt"], Literal("one")) with
        {
            OnCollision = PipelineRequest.CollisionAddNumber
        }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal("a/Name (2).txt", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task UnchangedFilesKeepTheirPathAndPathsBeingVacatedAreFree()
    {
        // "Name.txt" stays, so "one.txt" is numbered; "two.txt" takes the name "one.txt" leaves behind.
        Exist("a/Name.txt", "a/one.txt", "a/two.txt");

        var preview = await Engine().Preview(Request(["a/Name.txt", "a/one.txt", "a/two.txt"], Literal("one"),
            Plugin("second", "regex", new { pattern = "^two$", replacement = "one" })) with
        {
            OnCollision = PipelineRequest.CollisionAddNumber
        }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(["a/Name.txt", "a/Name (1).txt", "a/one.txt"], preview.Files.Select(f => f.TargetPath));
        Assert.Equal([false, true, false], preview.Files.Select(f => f.Numbered));
    }

    [Fact]
    public async Task SwapsAreNotNumbered()
    {
        Exist("a/x.txt", "a/y.txt");

        var preview = await Engine().Preview(Request(["a/x.txt", "a/y.txt"],
            Plugin("swap", "replacements", new
            {
                replacements = new[]
                {
                    new { search = "x", replacement = "_t" }, new { search = "y", replacement = "x" },
                    new { search = "_t", replacement = "y" }
                }
            })) with { OnCollision = PipelineRequest.CollisionAddNumber }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(["a/y.txt", "a/x.txt"], preview.Files.Select(f => f.TargetPath));
        Assert.All(preview.Files, file => Assert.False(file.Numbered));
    }

    [Fact]
    public async Task ManualOverridesKeepTheirNameAndOthersAvoidIt()
    {
        Exist("a/one.txt", "a/two.txt");

        var preview = await Engine().Preview(Request(["a/one.txt", "a/two.txt"], Literal("one|two")) with
        {
            OnCollision = PipelineRequest.CollisionAddNumber,
            Overrides = [new("1", "a/Name.txt")]
        }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(["a/Name (1).txt", "a/Name.txt"], preview.Files.Select(f => f.TargetPath));
        Assert.Equal([true, false], preview.Files.Select(f => f.Numbered));
    }

    [Fact]
    public async Task CaseInsensitiveFileSystemsTreatCaseVariantsAsTheSameName()
    {
        comparison = StringComparison.OrdinalIgnoreCase;
        Exist("a/one.txt", "a/NAME.txt");

        var preview = await Engine().Preview(Request(["a/one.txt"], Literal("one")) with
        {
            OnCollision = PipelineRequest.CollisionAddNumber
        }, default);

        Assert.Equal("a/Name (1).txt", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task CaseOnlyRenamesDoNotCollideWithThemselves()
    {
        comparison = StringComparison.OrdinalIgnoreCase;
        Exist("a/Photo.JPG");

        var preview = await Engine().Preview(Request(["a/Photo.JPG"],
            Plugin("ext", "extensionCase", new { mode = "lower" })) with
        {
            OnCollision = PipelineRequest.CollisionAddNumber
        }, default);

        Assert.True(preview.CanPrepare);
        var file = Assert.Single(preview.Files);
        Assert.Equal("a/Photo.jpg", file.TargetPath);
        Assert.False(file.Numbered);
    }

    [Fact]
    public async Task RelatedFilesFollowTheirNumberedVideo()
    {
        Exist("a/one.mkv", "a/one.en.srt", "a/Name.mkv");

        var preview = await Engine().Preview(Request(["a/one.mkv", "a/one.en.srt"], Literal("one")) with
        {
            OnCollision = PipelineRequest.CollisionAddNumber, IncludeCompanions = true
        }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(["a/Name (1).mkv", "a/Name (1).en.srt"], preview.Files.Select(f => f.TargetPath));
        Assert.All(preview.Files, file => Assert.True(file.Numbered));
    }

    [Fact]
    public async Task SubtitlesAreNumberedBeforeTheirLanguageTokens()
    {
        Exist("a/Show.67e7d01.en.srt", "a/Show.68b944f.en.srt", "a/Show.a2139ce.en.hi.srt", "a/Show.x.srt",
            "a/Show.y.srt");

        var preview = await Engine().Preview(Request(
            ["a/Show.67e7d01.en.srt", "a/Show.68b944f.en.srt", "a/Show.a2139ce.en.hi.srt", "a/Show.x.srt",
                "a/Show.y.srt"],
            Plugin("hash", "regex", new { pattern = @"\.(?:[0-9a-f]{7}|x|y)(?=\.|$)", replacement = "" }))
            with { OnCollision = PipelineRequest.CollisionAddNumber }, default);

        Assert.True(preview.CanPrepare);
        Assert.Equal(["a/Show.en.srt", "a/Show.1.en.srt", "a/Show.en.hi.srt", "a/Show.srt", "a/Show.1.srt"],
            preview.Files.Select(f => f.TargetPath));
    }

    [Fact]
    public async Task NamesThatCannotTakeANumberAreReported()
    {
        var stem = new string('n', 236);
        Exist("a/one.txt", $"a/{stem}.txt");

        var preview = await Engine().Preview(Request(["a/one.txt"],
            Plugin("long", "regex", new { pattern = "^one$", replacement = stem })) with
        {
            OnCollision = PipelineRequest.CollisionAddNumber
        }, default);

        Assert.Equal("TargetExists", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    [Fact]
    public void UnknownCollisionModesAreRejected()
    {
        var exception = Assert.Throws<RenameException>(() => Engine().ValidateRequest(
            Request(["a/one.txt"], Literal("one")) with { OnCollision = "Overwrite" }));

        Assert.Equal("InvalidCollisionMode", exception.Code);
    }

    private void Exist(params string[] paths) => existing.UnionWith(paths);

    private PipelineEngine Engine() => TestPipeline.Create(metadata, fileSystem);

    private static PluginInstance Literal(string pattern)
        => Plugin("name", "regex", new { pattern = $"^({pattern})$", replacement = "Name" });

    private static PipelineRequest Request(string[] paths, params PluginInstance[] plugins)
        => new(paths.Select((path, i) => new FileInput(i.ToString(), path)).ToArray(), plugins);

    private static PluginInstance Plugin(string id, string type, object options)
        => new(id, type, 1, true, JsonSerializer.SerializeToElement(options));
}
