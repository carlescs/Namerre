using System.Text.Json;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plugins;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class ExtensionCasePluginTests
{
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public ExtensionCasePluginTests()
    {
        // A case-insensitive filesystem, where the renamed file already "exists" under its new name.
        fileSystem.Exists(Arg.Any<string>()).Returns(true);
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
            string.Equals((string)call[0], (string)call[1], StringComparison.OrdinalIgnoreCase));
        fileSystem.Facts(Arg.Any<string>()).Returns(new FileFacts(1, DateTimeOffset.UnixEpoch, null));
    }

    [Theory]
    [InlineData("lower", "Photos/IMG_01.JPG", "Photos/IMG_01.jpg")]
    [InlineData("upper", "Photos/IMG_01.jpg", "Photos/IMG_01.JPG")]
    [InlineData("lower", "Photos/archive.Tar.GZ", "Photos/archive.Tar.gz")]
    [InlineData("lower", "Photos/README", "Photos/README")]
    public async Task ChangesOnlyTheCaseOfTheExtension(string mode, string path, string expected)
    {
        var preview = await TestPipeline.Create(metadata, fileSystem).Preview(
            Request(path, Plugin("extensionCase", new { mode })), default);

        var file = Assert.Single(preview.Files);
        Assert.Empty(file.Issues);
        Assert.Equal(expected, file.TargetPath);
    }

    [Fact]
    public async Task LaterStepsSeeTheNewExtension()
    {
        var recorder = new RecordingPlugin();
        var engine = new PipelineEngine(fileSystem, [new ExtensionCasePlugin(), recorder]);

        await engine.Preview(Request("a/b.MKV", Plugin("extensionCase", new { mode = "lower" }),
            Plugin("record", new { })), default);

        Assert.Equal(".mkv", recorder.Extension);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpeg")]
    [InlineData("")]
    public async Task OtherExtensionChangesAreStillRejected(string extension)
    {
        var engine = new PipelineEngine(fileSystem, [new ExtensionPlugin(extension)]);

        var preview = await engine.Preview(Request("a/b.jpg", Plugin("extension", new { })), default);

        Assert.Equal("InvalidConfiguration", Assert.Single(Assert.Single(preview.Files).Issues).Code);
    }

    private static PipelineRequest Request(string path, params PluginInstance[] plugins)
        => new([new("file", path)], plugins);

    private static PluginInstance Plugin(string type, object options)
        => new(type, type, 1, true, JsonSerializer.SerializeToElement(options));

    private sealed class Empty;

    private sealed class RecordingPlugin() : RenamePlugin<Empty>("record", 1, "Record", "Records the extension", [])
    {
        public string? Extension { get; private set; }

        protected override PathParts Apply(PluginContext context, Empty options)
        {
            Extension = context.Path.Extension;
            return context.Path;
        }
    }

    private sealed class ExtensionPlugin(string extension)
        : RenamePlugin<Empty>("extension", 1, "Extension", "Replaces the extension", [])
    {
        protected override PathParts Apply(PluginContext context, Empty options)
            => context.Path with { Extension = extension };
    }
}
