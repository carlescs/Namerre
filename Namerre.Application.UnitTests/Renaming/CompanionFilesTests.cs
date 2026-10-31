using Namerre.Application.Renaming;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class CompanionFilesTests
{
    [Theory]
    [InlineData("Movie.nfo", ".nfo")]
    [InlineData("Movie.NFO", ".NFO")]
    [InlineData("Movie.srt", ".srt")]
    [InlineData("Movie.en.srt", ".en.srt")]
    [InlineData("Movie.en.hi.srt", ".en.hi.srt")]
    [InlineData("Movie.pt-BR.forced.vtt", ".pt-BR.forced.vtt")]
    [InlineData("Movie.eng.ass", ".eng.ass")]
    [InlineData("Movie-poster.jpg", "-poster.jpg")]
    [InlineData("Movie-fanart.PNG", "-fanart.PNG")]
    [InlineData("Movie-thumb.webp", "-thumb.webp")]
    [InlineData("Movie 2.srt", null)]
    [InlineData("Movie 2.nfo", null)]
    [InlineData("Movie2-poster.jpg", null)]
    [InlineData("Movie.Part.Two.srt", null)]
    [InlineData("Movie.en.txt", null)]
    [InlineData("Movie-banner.jpg", null)]
    [InlineData("Movie-poster.txt", null)]
    [InlineData("Movie.jpg", null)]
    [InlineData("movie.srt", null)]
    [InlineData("Movie", null)]
    [InlineData("Other.srt", null)]
    public void RecognisesOnlyTheVideoStemFollowedByAKnownSuffix(string name, string? expected)
        => Assert.Equal(expected, CompanionFiles.Suffix("Movie", name));

    [Fact]
    public void AmbiguousStemsPairEachCompanionWithItsOwnVideo()
    {
        var companions = CompanionFiles.Detect([
            new("movie", "Films/Movie.mkv"),
            new("movie-2", "Films/Movie 2.mkv"),
            new("sub", "Films/Movie.en.srt"),
            new("sub-2", "Films/Movie 2.en.srt"),
            new("nfo-2", "Films/Movie 2.nfo"),
            new("poster", "Films/Movie-poster.jpg")
        ]);

        Assert.Equal(new Companion("movie", ".en.srt"), companions["sub"]);
        Assert.Equal(new Companion("movie-2", ".en.srt"), companions["sub-2"]);
        Assert.Equal(new Companion("movie-2", ".nfo"), companions["nfo-2"]);
        Assert.Equal(new Companion("movie", "-poster.jpg"), companions["poster"]);
        Assert.Equal(4, companions.Count);
    }

    [Fact]
    public void PrefersTheLongestVideoStemWhenSeveralMatch()
    {
        var companions = CompanionFiles.Detect([
            new("short", "Show.mkv"),
            new("long", "Show.en.mkv"),
            new("sub", "Show.en.srt")
        ]);

        Assert.Equal(new Companion("long", ".srt"), Assert.Single(companions).Value);
    }

    [Fact]
    public void CompanionsMustShareTheVideoFolderAndVideosNeverFollowOthers()
    {
        var companions = CompanionFiles.Detect([
            new("video", "A/Movie.mkv"),
            new("elsewhere", "B/Movie.srt"),
            new("root", "Movie.nfo"),
            new("copy", "A/Movie.mp4"),
            new("here", "A/Movie.nfo")
        ]);

        Assert.Equal("here", Assert.Single(companions).Key);
    }

    [Fact]
    public void FilesWithoutAVideoInTheRequestStayIndependent()
        => Assert.Empty(CompanionFiles.Detect([new("sub", "Movie.en.srt"), new("nfo", "Movie.nfo")]));
}
