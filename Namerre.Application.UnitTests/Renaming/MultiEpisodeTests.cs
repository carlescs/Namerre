using System.Text.Json;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plugins;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class MultiEpisodeTests
{
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public MultiEpisodeTests()
    {
        fileSystem.Exists("show.mkv").Returns(true);
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => (string)call[0] == (string)call[1]);
    }

    [Theory]
    [InlineData(new[] { "Pilot" }, "Pilot")]
    [InlineData(new[] { "Pilot", "Pilot" }, "Pilot")]
    [InlineData(new[] { "The End (1)", "The End (2)" }, "The End")]
    [InlineData(new[] { "Exodus, Part 1", "Exodus, Part 2", "Exodus, Part 3" }, "Exodus")]
    [InlineData(new[] { "Genesis - Part One", "Genesis - Part Two" }, "Genesis")]
    [InlineData(new[] { "Storm: Pt. I", "Storm: Pt. II" }, "Storm")]
    [InlineData(new[] { "Title A", "Title B" }, "Title A + Title B")]
    [InlineData(new[] { "The End (1)", "Another (2)" }, "The End (1) + Another (2)")]
    [InlineData(new[] { "The End (1)", "The End" }, "The End (1) + The End")]
    public void JoinsEpisodeTitles(string[] titles, string expected)
        => Assert.Equal(expected, EpisodeTitles.Join(titles));

    [Theory]
    [InlineData(null, "Show - S01E01-E02 - A + B")]
    [InlineData(TmdbPlugin.RangeRepeat, "Show - S01E01-E02 - A + B")]
    [InlineData(TmdbPlugin.RangeConcatenate, "Show - S01E01E02 - A + B")]
    [InlineData(TmdbPlugin.RangeHyphen, "Show - S01E01-02 - A + B")]
    public async Task RendersEpisodeRangesInTheChosenStyle(string? style, string expected)
    {
        var preview = await Preview(new TmdbMatch(7, "tv", "Show", "2001", 1, 1, "A + B", EpisodeEnd: 2),
            style is null ? new { } : new { episodeRange = style });
        Assert.Equal(expected + ".mkv", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task NumberFormatsApplyToBothEndsOfARange()
    {
        var preview = await Preview(new TmdbMatch(7, "tv", "Show", "2001", 1, 9, "A + B", EpisodeEnd: 10),
            new { nameTemplate = "{title} {season:0}x{episode:000}" });
        Assert.Equal("Show 1x009-E010.mkv", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task SingleEpisodesAreUnchanged()
    {
        var preview = await Preview(new TmdbMatch(7, "tv", "Show", "2001", 1, 1, "Pilot"), new { episodeRange = TmdbPlugin.RangeHyphen });
        Assert.Equal("Show - S01E01 - Pilot.mkv", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public void TheRangeStyleDoesNotChangeTheOptionsHash()
        => Assert.Equal(TmdbPlugin.OptionsHash(JsonSerializer.SerializeToElement(new { language = "en-US" })),
            TmdbPlugin.OptionsHash(JsonSerializer.SerializeToElement(new { language = "en-US", episodeRange = "hyphen" })));

    private Task<Namerre.Application.Renaming.Previews.PreviewResult> Preview(TmdbMatch match, object options)
    {
        var plugin = new PluginInstance("tmdb", "tmdb", 1, true, JsonSerializer.SerializeToElement(options));
        var selection = new MetadataSelection("file", "tmdb", "show.mkv", TmdbPlugin.OptionsHash(plugin.Options), "token");
        metadata.Resolve(selection).Returns(match);
        return TestPipeline.Create(metadata, fileSystem)
            .Preview(new PipelineRequest([new("file", "show.mkv")], [plugin], [selection]), default);
    }
}
