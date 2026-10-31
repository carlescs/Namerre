using System.Text.Json;
using System.Text.Json.Nodes;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plugins;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

/// <summary>
/// The Simple mode's built-in presets live in the frontend (<c>simple-presets.json</c>); each one, with every
/// choice of its select fields, must preview against the built-in plugin catalog without an issue.
/// </summary>
public sealed class SimplePresetTests
{
    private const string Source = "TV/Show.Name.S01E02.1080p.WEB-DL.x264-GROUP.mkv";
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public SimplePresetTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(call => (string)call[0] == Source);
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => (string)call[0] == (string)call[1]);
        fileSystem.Facts(Arg.Any<string>())
            .Returns(new FileFacts(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
    }

    public static TheoryData<string> PresetIds()
    {
        var data = new TheoryData<string>();
        foreach (var preset in Presets()) data.Add(preset["id"]!.GetValue<string>());
        return data;
    }

    [Fact]
    public void ThePresetFileIsFound() => Assert.NotEmpty(Presets());

    [Theory]
    [MemberData(nameof(PresetIds))]
    public async Task PresetPreviewsWithEveryChoice(string id)
    {
        var preset = Presets().Single(entry => entry["id"]!.GetValue<string>() == id);
        foreach (var steps in Variants(preset))
        {
            var preview = await Preview(steps, preset["tmdbMediaType"]?.GetValue<string>());

            var file = Assert.Single(preview.Files);
            Assert.Empty(file.Issues);
            Assert.All(file.Steps, step => Assert.Null(step.Error));
            Assert.False(file.Unchanged);
            Assert.True(preview.CanPrepare);
        }
    }

    [Fact]
    public async Task CleanUpProducesATidyName()
    {
        var preset = Presets().Single(entry => entry["id"]!.GetValue<string>() == "cleanUp");
        var preview = await Preview(Steps(preset), null);

        Assert.Equal("TV/Show Name S01e02.mkv", Assert.Single(preview.Files).TargetPath);
    }

    /// <summary>The preset as defined, then once per other choice of each select field.</summary>
    private static IEnumerable<JsonArray> Variants(JsonNode preset)
    {
        yield return Steps(preset);
        foreach (var field in preset["fields"]!.AsArray())
        {
            if (field!["kind"]!.GetValue<string>() != "select") continue;
            foreach (var choice in field["choices"]!.AsArray())
            {
                var steps = Steps(preset);
                steps[field["step"]!.GetValue<int>()]!["options"]![field["option"]!.GetValue<string>()] =
                    choice!["value"]!.DeepClone();
                yield return steps;
            }
        }
    }

    private static JsonArray Steps(JsonNode preset) => preset["steps"]!.DeepClone().AsArray();

    private Task<Namerre.Application.Renaming.Previews.PreviewResult> Preview(JsonArray steps, string? mediaType)
    {
        var plugins = steps.Select((step, index) => new PluginInstance($"step-{index}", step!["typeId"]!.GetValue<string>(),
            step["version"]!.GetValue<int>(), true, JsonSerializer.SerializeToElement(step["options"]))).ToList();
        var selections = plugins.Where(plugin => plugin.TypeId == "tmdb")
            .Select(plugin => new MetadataSelection("file", plugin.InstanceId, Source, TmdbPlugin.OptionsHash(plugin.Options),
                "token"))
            .ToList();
        metadata.Resolve(Arg.Any<MetadataSelection>()).Returns(mediaType == "tv"
            ? new TmdbMatch(1, "tv", "Show Name", "2008", 1, 2, "The Episode", ShowYear: "2008")
            : new TmdbMatch(2, "movie", "Movie Name", "2001", null, null, null));
        return TestPipeline.Create(metadata, fileSystem).Preview(
            new PipelineRequest([new("file", Source)], plugins, selections, IncludeCompanions: true), default);
    }

    private static JsonArray Presets()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "simple-presets.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsArray();
    }
}
