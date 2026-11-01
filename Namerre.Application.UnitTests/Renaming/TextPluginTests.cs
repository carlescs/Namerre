using System.Text.Json;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Previews;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

/// <summary>The separator, release tag, character, insert, remove, truncate and replacement table steps.</summary>
public sealed class TextPluginTests
{
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    public TextPluginTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(false);
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => (string)call[0] == (string)call[1]);
        fileSystem.Facts(Arg.Any<string>()).Returns(new FileFacts(1, DateTimeOffset.UnixEpoch, null));
    }

    [Theory]
    [InlineData("Show.Name.S01E02.1080p", "Show Name S01E02 1080p")]
    [InlineData("my_file__name", "my file name")]
    [InlineData("Movie.DDP5.1.v1.2", "Movie DDP5.1 v1.2")]
    [InlineData("_.leading and trailing._", "leading and trailing")]
    [InlineData("Spider-Man_Far.From.Home", "Spider-Man Far From Home")]
    [InlineData("1.2.3", "1.2.3")]
    public async Task SeparatorsUseTheDefaults(string stem, string expected)
    {
        var file = await PreviewOne($"src/{stem}.mkv", Plugin("separators", new { }));

        Assert.Empty(file.Issues);
        Assert.Equal($"src/{expected}.mkv", file.TargetPath);
    }

    [Theory]
    [InlineData("Spider-Man_v1.2", true, false, false, true, "Spider Man_v1 2")]
    [InlineData("a..b__c", false, false, false, true, "a  b__c")]
    [InlineData("a..b__c", false, false, true, false, "a..b  c")]
    [InlineData(" a  b ", false, true, false, false, " a b ")]
    public async Task SeparatorsFollowTheirOptions(string stem, bool dashes, bool collapseSpaces, bool underscores,
        bool dots, string expected)
    {
        string[] characters = [.. new[] { (dots, "dots"), (underscores, "underscores"), (dashes, "dashes") }
            .Where(c => c.Item1).Select(c => c.Item2)];
        var file = await PreviewOne($"src/{stem}.txt", Plugin("separators",
            new { characters, splitNumbers = true, keepSpaces = !collapseSpaces, keepEnds = true }));

        Assert.Equal($"src/{expected}.txt", file.TargetPath);
    }

    [Fact]
    public async Task SeparatorsRejectNamesThatBecomeEmpty()
    {
        var file = await PreviewOne("src/._-_..txt", Plugin("separators",
            new { characters = new[] { "dots", "underscores", "dashes" } }));

        Assert.Equal("InvalidName", Assert.Single(file.Issues).Code);
    }

    [Theory]
    [InlineData("Show.Name.S01E02.1080p.WEB-DL.DDP5.1.H.264-NTb", "Show.Name.S01E02")]
    [InlineData("Movie.2010.1080p.BluRay.x264-SPARKS", "Movie.2010")]
    [InlineData("Movie (2019) 2160p UHD BluRay REMUX HDR10+ HEVC TrueHD 7.1 Atmos-FGT", "Movie (2019)")]
    [InlineData("[SubGroup] Show - 01 [1080p][HEVC]", "Show - 01")]
    [InlineData("Movie.2010.1080p.BluRay.x264.Extended.Cut", "Movie.2010.Extended.Cut")]
    [InlineData("Show_S02E05_720p_WEBRip_AAC2.0_x265", "Show_S02E05")]
    [InlineData("Movie.DTS-HD.MA.5.1.Dolby.Vision", "Movie")]
    [InlineData("Show[1080p]Name", "Show Name")]
    public async Task ReleaseTagsStripTypicalSceneNames(string stem, string expected)
    {
        var file = await PreviewOne($"src/{stem}.mkv", Plugin("releaseTags", new { }));

        Assert.Empty(file.Issues);
        Assert.Equal($"src/{expected}.mkv", file.TargetPath);
    }

    [Theory]
    [InlineData("Webdlx Hdrive Navcom Remuxer 1080pixels")]
    [InlineData("Spider-Man")]
    [InlineData("The DVD Collection")]
    [InlineData("Ex-Wives")]
    public async Task ReleaseTagsNeverRemovePartsOfWords(string stem)
    {
        var file = await PreviewOne($"src/{stem}.mkv", Plugin("releaseTags", new { }));

        Assert.True(file.Unchanged);
    }

    [Fact]
    public async Task ReleaseTagsFollowTheirCategoriesAndExtraTokens()
    {
        var file = await PreviewOne("src/Movie (2010) [Group] 1080p x264 PROPER REPACK-GRP.mkv", Plugin("releaseTags",
            new
            {
                categories = new[] { "resolution", "source", "audio", "hdr" },
                groups = new[] { "parentheses", "releaseGroup" },
                extra = "proper,\nrepack"
            }));

        Assert.Equal("src/Movie [Group] x264.mkv", file.TargetPath);
    }

    [Fact]
    public async Task ReleaseTagsRejectNamesThatBecomeEmpty()
    {
        var file = await PreviewOne("src/1080p.x265.mkv", Plugin("releaseTags", new { }));

        Assert.Equal("InvalidName", Assert.Single(file.Issues).Code);
    }

    [Theory]
    [InlineData("Crème Brûl\u00e9e à la carta, niño", "Creme Brulee a la carta, nino")]
    [InlineData("Æsir og Straße – l’Œuvre…", "AEsir og Strasse - l'OEuvre...")]
    [InlineData("Łódź Ørsted", "Lodz Orsted")]
    public async Task CharactersRemoveAccentsAndTransliterateByDefault(string stem, string expected)
    {
        var file = await PreviewOne($"src/{stem}.txt", Plugin("characters", new { }));

        Assert.Empty(file.Issues);
        Assert.Equal($"src/{expected}.txt", file.TargetPath);
    }

    [Fact]
    public async Task CharactersCanKeepDiacriticsAndOnlyNormaliseTheForm()
    {
        var decomposed = "Cafe\u0301";
        var nfc = await PreviewOne($"src/{decomposed}.txt",
            Plugin("characters", new { changes = Array.Empty<string>() }));
        var nfd = await PreviewOne("src/Caf\u00e9.txt",
            Plugin("characters", new { changes = Array.Empty<string>(), form = "nfd" }));

        Assert.Equal("src/Caf\u00e9.txt", nfc.TargetPath);
        Assert.Equal($"src/{decomposed}.txt", nfd.TargetPath);
    }

    [Theory]
    [InlineData("replace", "_", "東京 Cafe\u0301 \U0001F600", "__ Caf_ _")]
    [InlineData("remove", "_", "東京Caf\u00e9", "Caf")]
    [InlineData("keep", "_", "東京 æ", "東京 æ")]
    public async Task CharactersReplaceOrRemoveRemainingNonAscii(string nonAscii, string replacement, string stem,
        string expected)
    {
        var file = await PreviewOne($"src/{stem}.txt", Plugin("characters",
            new { changes = Array.Empty<string>(), nonAscii, replacement }));

        Assert.Equal($"src/{expected}.txt", file.TargetPath);
    }

    [Fact]
    public async Task CharactersRejectNamesThatBecomeEmptyOrContainSeparators()
    {
        var empty = await PreviewOne("src/東京.txt", Plugin("characters", new { nonAscii = "remove" }));
        var slash = await PreviewOne("src/東.txt", Plugin("characters", new { nonAscii = "replace", replacement = "/" }));

        Assert.Equal("InvalidName", Assert.Single(empty.Issues).Code);
        Assert.Equal("InvalidName", Assert.Single(slash.Issues).Code);
    }

    [Fact]
    public async Task CharactersReportNamesThatNormaliseToTheSameTarget()
    {
        var preview = await Preview(["src/Cafe.txt", "src/Caf\u00e9.txt"], Plugin("characters", new { }));

        Assert.False(preview.CanPrepare);
        Assert.All(preview.Files, f => Assert.Contains(f.Issues, i => i.Code == "DuplicateTarget"));
    }

    [Theory]
    [InlineData(0, "start", "-Name")]
    [InlineData(2, "start", "Na-me")]
    [InlineData(1, "end", "Nam-e")]
    [InlineData(99, "start", "Name-")]
    [InlineData(99, "end", "-Name")]
    public async Task InsertCountsFromEitherEndAndClamps(int position, string from, string expected)
    {
        var file = await PreviewOne("src/Name.txt", Plugin("insert", new { text = "-", position, from }));

        Assert.Equal($"src/{expected}.txt", file.TargetPath);
    }

    [Fact]
    public async Task InsertNeverSplitsSurrogatePairsOrCombiningCharacters()
    {
        var file = await PreviewOne("src/\U0001F600e\u0301x.txt", Plugin("insert", new { text = "|", position = 2 }));

        Assert.Equal("src/\U0001F600e\u0301|x.txt", file.TargetPath);
    }

    [Theory]
    [InlineData(2, 0, "start", "me")]
    [InlineData(2, 0, "end", "Na")]
    [InlineData(2, 1, "start", "Ne")]
    [InlineData(2, 1, "end", "Ne")]
    [InlineData(10, 3, "start", "Nam")]
    [InlineData(10, 9, "start", "Name")]
    [InlineData(3, 2, "end", "me")]
    public async Task RemoveTakesCharactersFromEitherEndOrARange(int count, int position, string from, string expected)
    {
        var file = await PreviewOne("src/Name.txt", Plugin("remove", new { count, position, from }));

        Assert.Equal($"src/{expected}.txt", file.TargetPath);
    }

    [Fact]
    public async Task RemoveCountsTextElementsAndRejectsEmptyNames()
    {
        var emoji = await PreviewOne("src/\U0001F600e\u0301x.txt", Plugin("remove", new { count = 2 }));
        var empty = await PreviewOne("src/Name.txt", Plugin("remove", new { count = 4 }));

        Assert.Equal("src/x.txt", emoji.TargetPath);
        Assert.Equal("InvalidName", Assert.Single(empty.Issues).Code);
    }

    [Fact]
    public async Task RemoveRejectsCountsOutsideItsRange()
    {
        var file = await PreviewOne("src/Name.txt", Plugin("remove", new { count = 0 }));

        Assert.Equal("InvalidConfiguration", Assert.Single(file.Issues).Code);
    }

    [Theory]
    [InlineData("A long file name", 10, "", false, "A long fil")]
    [InlineData("A long file name", 10, "…", false, "A long fi…")]
    [InlineData("A long file name", 10, "…", true, "A long…")]
    [InlineData("A long file name", 11, "", true, "A long file")]
    [InlineData("Unbreakable", 5, "", true, "Unbre")]
    [InlineData("Short", 5, "…", true, "Short")]
    public async Task TruncateShortensLongNames(string stem, int maxLength, string marker, bool wordBoundary,
        string expected)
    {
        var file = await PreviewOne($"src/{stem}.txt", Plugin("truncate", new { maxLength, marker, wordBoundary }));

        Assert.Empty(file.Issues);
        Assert.Equal($"src/{expected}.txt", file.TargetPath);
    }

    [Fact]
    public async Task TruncateKeepsTextElementsWholeAndNeedsRoomBesideTheMarker()
    {
        var emoji = await PreviewOne("src/\U0001F600\U0001F600e\u0301e\u0301.txt", Plugin("truncate", new { maxLength = 3 }));
        var marker = await PreviewOne("src/Name.txt", Plugin("truncate", new { maxLength = 2, marker = "..." }));

        Assert.Equal("src/\U0001F600\U0001F600e\u0301.txt", emoji.TargetPath);
        Assert.Equal("InvalidConfiguration", Assert.Single(marker.Issues).Code);
    }

    [Fact]
    public async Task ReplacementTableAppliesRowsInOrder()
    {
        var file = await PreviewOne("src/a b c.txt", Plugin("replacements", new
        {
            replacements = new[] { new { search = "a", replacement = "b" }, new { search = "b", replacement = "c" } }
        }));

        Assert.Equal("src/c c c.txt", file.TargetPath);
    }

    [Theory]
    [InlineData(false, "all", "y-X-y")]
    [InlineData(true, "all", "y-y-y")]
    [InlineData(true, "first", "y-X-x")]
    [InlineData(false, "first", "y-X-x")]
    public async Task ReplacementTableHonoursCaseAndScope(bool ignoreCase, string scope, string expected)
    {
        var file = await PreviewOne("src/x-X-x.txt", Plugin("replacements", new
        {
            replacements = new[] { new { search = "x", replacement = "y" } },
            ignoreCase,
            scope
        }));

        Assert.Equal($"src/{expected}.txt", file.TargetPath);
    }

    [Fact]
    public async Task ReplacementTableRejectsEmptySearchRows()
    {
        var file = await PreviewOne("src/name.txt", Plugin("replacements", new
        {
            replacements = new[] { new { search = "n", replacement = "N" }, new { search = "", replacement = "x" } }
        }));

        Assert.Equal("InvalidConfiguration", Assert.Single(file.Issues).Code);
        Assert.Contains("row 2", file.Issues[0].Message);
    }

    private async Task<PreviewFile> PreviewOne(string path, PluginInstance plugin)
        => Assert.Single((await Preview([path], plugin)).Files);

    private Task<PreviewResult> Preview(string[] paths, params PluginInstance[] plugins)
        => TestPipeline.Create(metadata, fileSystem).Preview(Request(paths, plugins), default);

    private PipelineRequest Request(string[] paths, params PluginInstance[] plugins)
    {
        foreach (var path in paths) fileSystem.Exists(path).Returns(true);
        return new(paths.Select((path, i) => new FileInput(i.ToString(), path)).ToArray(), plugins);
    }

    private static PluginInstance Plugin(string type, object options)
        => new(type, type, 1, true, JsonSerializer.SerializeToElement(options));
}
