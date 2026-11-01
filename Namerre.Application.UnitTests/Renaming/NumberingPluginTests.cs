using System.Text.Json;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

/// <summary>The pad numbers, counter and date steps.</summary>
public sealed class NumberingPluginTests
{
    private static readonly DateTimeOffset Modified = new(2024, 3, 9, 22, 30, 15, TimeSpan.Zero);
    private static readonly DateTimeOffset Created = new(2023, 12, 31, 23, 59, 0, TimeSpan.Zero);
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public NumberingPluginTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(false);
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => (string)call[0] == (string)call[1]);
        fileSystem.Facts(Arg.Any<string>()).Returns(new FileFacts(1, Modified, Created));
    }

    [Theory]
    [InlineData("Episode 3", 2, "all", false, "Episode 03")]
    [InlineData("Episode 12", 2, "all", false, "Episode 12")]
    [InlineData("Episode 123", 2, "all", false, "Episode 123")]
    [InlineData("Show 1x5 part 7", 3, "all", false, "Show 001x005 part 007")]
    [InlineData("Show 1x5 part 7", 2, "first", false, "Show 01x5 part 7")]
    [InlineData("Show 1x5 part 7", 2, "last", false, "Show 1x5 part 07")]
    [InlineData("Track 007", 2, "all", false, "Track 007")]
    [InlineData("Track 07", 3, "all", false, "Track 007")]
    [InlineData("No digits", 3, "all", false, "No digits")]
    [InlineData("Movie 1999 part 2", 3, "all", false, "Movie 1999 part 002")]
    [InlineData("Show 2024 5", 6, "all", false, "Show 002024 000005")]
    [InlineData("Show 2024 5", 6, "all", true, "Show 2024 000005")]
    [InlineData("2024 Show 5", 2, "first", true, "2024 Show 05")]
    [InlineData("Show 5 2024", 2, "last", true, "Show 05 2024")]
    [InlineData("Show 1899 2100 0999", 5, "all", true, "Show 01899 02100 00999")]
    [InlineData("Only 2024", 2, "first", true, "Only 2024")]
    public async Task PadNumbersPadsTheChosenNumbersAndNeverShortensThem(string stem, int width, string target,
        bool skipYears, string expected)
    {
        var file = await PreviewOne($"src/{stem}.mkv", Plugin("pad", new { width, target, skipYears }));

        Assert.Empty(file.Issues);
        Assert.Equal($"src/{expected}.mkv", file.TargetPath);
    }

    [Fact]
    public async Task PadNumbersRejectsWidthsOutsideItsRange()
    {
        var file = await PreviewOne("src/a 1.txt", Plugin("pad", new { width = 0 }));

        Assert.Equal("InvalidConfiguration", Assert.Single(file.Issues).Code);
        Assert.Contains("Width", file.Issues[0].Message);
    }

    [Fact]
    public async Task CounterFollowsRequestOrderAcrossFolders()
    {
        var preview = await Preview(["src/a/x.txt", "src/b/y.txt", "src/a/z.txt"],
            Plugin("counter", new { start = 5, step = 10, width = 3 }));

        Assert.Equal(["src/a/x 005.txt", "src/b/y 015.txt", "src/a/z 025.txt"], preview.Files.Select(f => f.TargetPath));
        Assert.True(preview.CanPrepare);
    }

    [Fact]
    public async Task CounterRestartsPerFolderAndFollowsReorderedRequests()
    {
        var plugin = Plugin("counter", new { perFolder = true, width = 1, position = "prefix", separator = "-" });

        var preview = await Preview(["src/a/x.txt", "src/b/y.txt", "src/a/z.txt"], plugin);
        var reordered = await Preview(["src/a/z.txt", "src/b/y.txt", "src/a/x.txt"], plugin);

        Assert.Equal(["src/a/1-x.txt", "src/b/1-y.txt", "src/a/2-z.txt"], preview.Files.Select(f => f.TargetPath));
        Assert.Equal(["src/a/1-z.txt", "src/b/1-y.txt", "src/a/2-x.txt"], reordered.Files.Select(f => f.TargetPath));
    }

    [Theory]
    [InlineData("Ep {n} - {name}", "src/Ep 01 - clip.txt", null)]
    [InlineData("{n}", "src/01.txt", null)]
    [InlineData("{name}", null, "InvalidConfiguration")]
    public async Task CounterTemplatesNeedThePlaceholder(string template, string? expected, string? issue)
    {
        var file = await PreviewOne("src/clip.txt", Plugin("counter", new { position = "template", template }));

        Assert.Equal(expected ?? "src/clip.txt", file.TargetPath);
        Assert.Equal(issue, file.Issues.SingleOrDefault()?.Code);
    }

    [Theory]
    [InlineData("modified", "yyyy-MM-dd", "UTC", "prefix", "src/2024-03-09 clip.txt")]
    [InlineData("modified", "yyyyMMdd_HHmmss", "UTC", "suffix", "src/clip 20240309_223015.txt")]
    [InlineData("created", "yyyy-MM-dd HH.mm", "UTC", "prefix", "src/2023-12-31 23.59 clip.txt")]
    [InlineData("modified", "yyyy-MM-dd HH.mm", "Asia/Tokyo", "prefix", "src/2024-03-10 07.30 clip.txt")]
    [InlineData("created", "yyyy", "Europe/Madrid", "suffix", "src/clip 2024.txt")]
    public async Task DateFormatsTheChosenDateInTheChosenTimeZone(string source, string format, string timeZone,
        string position, string expected)
    {
        var file = await PreviewOne("src/clip.txt", Plugin("date", new { source, format, timeZone, position }));

        Assert.Empty(file.Issues);
        Assert.Equal(expected, file.TargetPath);
    }

    [Fact]
    public async Task DateFillsTemplatesAndDefaultsToServerLocalTime()
    {
        var file = await PreviewOne("src/clip.txt",
            Plugin("date", new { format = "yyyy-MM-dd", position = "template", template = "{name} ({date})" }));

        Assert.Equal($"src/clip ({TimeZoneInfo.ConvertTime(Modified, TimeZoneInfo.Local):yyyy-MM-dd}).txt",
            file.TargetPath);
    }

    [Theory]
    [InlineData("", "Enter a date format.")]
    [InlineData("d", "standard format")]
    [InlineData("yyyy%", "not a valid .NET date format")]
    [InlineData("yyyy\\", "not a valid .NET date format")]
    public async Task InvalidDateFormatsReportAClearError(string format, string message)
    {
        var file = await PreviewOne("src/clip.txt", Plugin("date", new { format, timeZone = "UTC" }));

        var issue = Assert.Single(file.Issues);
        Assert.Equal("InvalidConfiguration", issue.Code);
        Assert.Contains(message, issue.Message);
    }

    [Fact]
    public async Task DateReportsUnknownTimeZonesAndMissingCreationDates()
    {
        fileSystem.Facts("src/new.txt").Returns(new FileFacts(1, Modified, null));

        var zone = await PreviewOne("src/clip.txt", Plugin("date", new { timeZone = "Mars/Olympus" }));
        var created = await PreviewOne("src/new.txt", Plugin("date", new { source = "created", timeZone = "UTC" }));

        Assert.Equal("Unknown time zone \"Mars/Olympus\".", Assert.Single(zone.Issues).Message);
        Assert.Equal("DateUnavailable", Assert.Single(created.Issues).Code);
    }

    [Fact]
    public async Task PreparedPlansProduceTheNamesOfThePreview()
    {
        var engine = TestPipeline.Create(metadata, fileSystem);
        var request = Request(["src/a/x 1.txt", "src/b/y 2.txt", "src/a/z 3.txt"],
            Plugin("pad", new { width = 3 }), Plugin("counter", new { perFolder = true }, "c"),
            Plugin("date", new { format = "yyyyMMdd", timeZone = "UTC" }, "d"));

        var preview = await engine.Preview(request, default);
        var facts = engine.ReadFacts(request);
        fileSystem.Facts(Arg.Any<string>()).Returns(new FileFacts(1, Modified.AddYears(1), Created));
        var prepared = await engine.Preview(request, facts, default);

        Assert.Equal(["src/a/20240309 x 001 01.txt", "src/b/20240309 y 002 01.txt", "src/a/20240309 z 003 02.txt"],
            preview.Files.Select(f => f.TargetPath));
        Assert.Equal(preview.Files.Select(f => f.TargetPath), prepared.Files.Select(f => f.TargetPath));
    }

    private async Task<Namerre.Application.Renaming.Previews.PreviewFile> PreviewOne(string path, PluginInstance plugin)
        => Assert.Single((await Preview([path], plugin)).Files);

    private Task<Namerre.Application.Renaming.Previews.PreviewResult> Preview(string[] paths,
        params PluginInstance[] plugins)
        => TestPipeline.Create(metadata, fileSystem).Preview(Request(paths, plugins), default);

    private PipelineRequest Request(string[] paths, params PluginInstance[] plugins)
    {
        foreach (var path in paths) fileSystem.Exists(path).Returns(true);
        return new(paths.Select((path, i) => new FileInput(i.ToString(), path)).ToArray(), plugins);
    }

    private static PluginInstance Plugin(string type, object options, string? id = null)
        => new(id ?? type, type, 1, true, JsonSerializer.SerializeToElement(options));
}
