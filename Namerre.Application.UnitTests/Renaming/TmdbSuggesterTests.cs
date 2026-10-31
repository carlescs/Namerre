using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class TmdbSuggesterTests
{
    private static readonly string Hash = new('a', 64);
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();

    [Fact]
    public async Task ProposesAndVerifiesTheBestScoringMovie()
    {
        Results("movie", "The Movie", Movie(7, "The Movie", "1990"), Movie(42, "The Movie", "2024"), Movie(9, "Another", "2024"));
        metadata.Search(Arg.Is<TmdbSearchRequest>(r => r.Id == 42), Arg.Any<CancellationToken>())
            .Returns(new TmdbSearchResult([Movie(42, "The Movie", "2024") with { ConfirmationToken = "token" }]));

        var suggestion = await new TmdbSuggester(metadata).Suggest(Request("movie", "Movies/The.Movie.2024.1080p.mkv"), default);

        Assert.Equal("token", suggestion.Match!.ConfirmationToken);
        Assert.Equal(1, suggestion.Confidence);
        Assert.Equal(3, suggestion.Candidates.Count);
        await metadata.Received().Search(Arg.Is<TmdbSearchRequest>(r => r.Id == 42 && r.FileId == "file"
            && r.InstanceId == "step" && r.InputPath == "Movies/The.Movie.2024.1080p.mkv" && r.OptionsHash == Hash
            && r.Language == "fr-FR"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifiesEpisodesAndRangesFromTheName()
    {
        var show = new TmdbMatch(1, "tv", "Show", "2020", null, null, null);
        Results("tv", "Show", show);
        metadata.Search(Arg.Is<TmdbSearchRequest>(r => r.Id == 1), Arg.Any<CancellationToken>())
            .Returns(new TmdbSearchResult([show with { Season = 2, Episode = 4, EpisodeEnd = 5, ConfirmationToken = "t" }]));

        var suggestion = await new TmdbSuggester(metadata).Suggest(Request("tv", "Show.S02E04-E05.mkv"), default);

        Assert.Equal(5, suggestion.Match!.EpisodeEnd);
        // An exact title without a year to compare is likely but not certain.
        Assert.Equal(0.88, suggestion.Confidence);
        await metadata.Received().Search(Arg.Is<TmdbSearchRequest>(r => r.Season == 2 && r.Episode == 4 && r.EpisodeEnd == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsNamesWithoutTitleOrEpisodeWithoutSearching()
    {
        var suggestion = await new TmdbSuggester(metadata).Suggest(Request("tv", "Pilot.mkv"), default);

        Assert.Null(suggestion.Match);
        Assert.Contains("Could not infer", suggestion.Problem);
        await metadata.DidNotReceiveWithAnyArgs().Search(default!, default);
    }

    [Fact]
    public async Task ReportsEmptyResults()
    {
        Results("movie", "Unknown");
        var suggestion = await new TmdbSuggester(metadata).Suggest(Request("movie", "Unknown.mkv"), default);
        Assert.Contains("No TMDB matches", suggestion.Problem);
        Assert.Equal(0, suggestion.Confidence);
    }

    [Fact]
    public async Task AMissingEpisodeIsAProblemNotAnError()
    {
        Results("tv", "Show", new TmdbMatch(1, "tv", "Show", "2020", null, null, null));
        metadata.Search(Arg.Is<TmdbSearchRequest>(r => r.Id == 1), Arg.Any<CancellationToken>())
            .Throws(new RenameException("MetadataNotFound", "Missing", 404));

        var suggestion = await new TmdbSuggester(metadata).Suggest(Request("tv", "Show.S09E99.mkv"), default);

        Assert.Equal("Show has no episode S09E99 on TMDB. Correct the episode numbers.", suggestion.Problem);
        Assert.Single(suggestion.Candidates);
    }

    [Fact]
    public async Task RateLimitsPropagateSoTheCallerCanRetry()
    {
        metadata.Search(Arg.Any<TmdbSearchRequest>(), Arg.Any<CancellationToken>())
            .Throws(new RenameException("TmdbBusy", "Busy", 429));
        var exception = await Assert.ThrowsAsync<RenameException>(
            () => new TmdbSuggester(metadata).Suggest(Request("movie", "Movie.2024.mkv"), default));
        Assert.Equal(429, exception.Status);
    }

    [Theory]
    [InlineData("person", "a.mkv")]
    [InlineData("movie", "")]
    public async Task InvalidRequestsAreRejected(string mediaType, string path)
        => Assert.Equal("InvalidMetadataQuery", (await Assert.ThrowsAsync<RenameException>(
            () => new TmdbSuggester(metadata).Suggest(Request(mediaType, path), default))).Code);

    [Theory]
    [InlineData("The Movie", "2024", "The Movie", "2024", 1.0)]
    [InlineData("The Movie", "2024", "the movie!", "2023", 0.88)]
    [InlineData("The Movie", "2024", "The Movie", "2010", 0.75)]
    [InlineData("Amelie", "2001", "Amélie", "2001", 1.0)]
    [InlineData("Law and Order", null, "Law & Order", null, 0.88)]
    public void ScoresTitleAndYear(string query, string? year, string title, string? candidateYear, double expected)
        => Assert.Equal(expected, MatchConfidence.Best([Movie(1, title, candidateYear)], new(query, year))!.Value.Confidence);

    [Fact]
    public void OriginalTitlesCountAndRivalsLowerTheScoreWithoutAYear()
    {
        var original = Movie(1, "The Intouchables", "2011") with { OriginalTitle = "Intouchables" };
        Assert.Equal(1.0, MatchConfidence.Best([original], new("Intouchables", "2011"))!.Value.Confidence);
        var remakes = MatchConfidence.Best([Movie(1, "The Office", "2005"), Movie(2, "The Office", "2001")], new("The Office"))!.Value;
        Assert.Equal(1, remakes.Match.Id);
        Assert.True(remakes.Confidence < MatchConfidence.High);
        var dated = MatchConfidence.Best([Movie(1, "The Office", "2005"), Movie(2, "The Office", "2001")], new("The Office", "2001"))!.Value;
        Assert.Equal((2, 1.0), (dated.Match.Id, dated.Confidence));
        Assert.True(MatchConfidence.Best([Movie(1, "Completely different", "2001")], new("The Office", "2001"))!.Value.Confidence < 0.5);
    }

    private void Results(string mediaType, string query, params TmdbMatch[] matches)
        => metadata.Search(Arg.Is<TmdbSearchRequest>(r => r.Id == null && r.MediaType == mediaType && r.Query == query),
            Arg.Any<CancellationToken>()).Returns(new TmdbSearchResult(matches));

    private static TmdbSuggestionRequest Request(string mediaType, string path)
        => new(mediaType, "file", "step", path, Hash, "fr-FR");

    private static TmdbMatch Movie(int id, string title, string? year) => new(id, "movie", title, year, null, null, null);
}
