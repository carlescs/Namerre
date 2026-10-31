using Namerre.Application.Renaming.Metadata;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class TmdbFileNameTests
{
    [Theory]
    [InlineData("Show.Name.S02E05.1080p.WEB-DL.mkv", "Show Name", 2, 5, null)]
    [InlineData("Show Name - s02e05 - Title.mkv", "Show Name", 2, 5, null)]
    [InlineData("Show_Name_2x05.avi", "Show Name", 2, 5, null)]
    [InlineData("Show.Name.S00E02.mkv", "Show Name", 0, 2, null)]
    [InlineData("Show.Name.S01E04-E05.mkv", "Show Name", 1, 4, 5)]
    [InlineData("Show.Name.S01E04E05.mkv", "Show Name", 1, 4, 5)]
    [InlineData("Show Name S01E04-05 720p.mkv", "Show Name", 1, 4, 5)]
    [InlineData("Show.Name.1x04-1x05.mkv", "Show Name", 1, 4, 5)]
    [InlineData("Show.Name.S01E04-1080p.mkv", "Show Name", 1, 4, null)]
    [InlineData("Show.Name.S01E04-2019.mkv", "Show Name", 1, 4, null)]
    [InlineData("Show.Name.S01E05-E04.mkv", "Show Name", 1, 5, null)]
    [InlineData("Show.Name.S01E01-E20.mkv", "Show Name", 1, 1, null)]
    public void ReadsEpisodes(string name, string query, int season, int episode, int? end)
        => Assert.Equal(new ParsedFileName(query, null, season, episode, end), TmdbFileName.Parse("TV/" + name, "tv"));

    [Theory]
    [InlineData("Movies/Movie.Name.2019.1080p.BluRay.x264-GRP.mkv", "Movie Name", "2019")]
    [InlineData("Movies/Movie Name (2019).mkv", "Movie Name", "2019")]
    [InlineData("Movies/Movie Name [2019] 2160p.mkv", "Movie Name", "2019")]
    [InlineData("Movies/2001.A.Space.Odyssey.1968.mkv", "2001 A Space Odyssey", "1968")]
    [InlineData("Movies/Blade.Runner.2049.2017.mkv", "Blade Runner 2049", "2017")]
    [InlineData("Movies/Plain Title.mkv", "Plain Title", null)]
    [InlineData("Movies/Movie_Name_720p_HDTV.mkv", "Movie Name", null)]
    public void ReadsMovies(string path, string query, string? year)
        => Assert.Equal(new ParsedFileName(query, year), TmdbFileName.Parse(path, "movie"));

    [Fact]
    public void MoviesNeverReadEpisodeNumbers()
        => Assert.Equal(new ParsedFileName("Show S01E02"), TmdbFileName.Parse("Show.S01E02.mkv", "movie"));

    [Theory]
    [InlineData("TV/Show Name/Season 1/S01E05.mkv")]
    [InlineData("TV/Show Name/S01/s01e05.mkv")]
    [InlineData("TV/Show Name/1x05.mkv")]
    public void BareEpisodeNamesTakeTheNearestShowFolder(string path)
        => Assert.Equal("Show Name", TmdbFileName.Parse(path, "tv").Query);

    [Fact]
    public void UnreadableNamesGiveAnEmptyQuery()
    {
        Assert.Equal(new ParsedFileName("", null, 1, 1), TmdbFileName.Parse("S01E01.mkv", "tv"));
        Assert.Equal("", TmdbFileName.Parse("Movies/1080p.mkv", "movie").Query);
    }
}
