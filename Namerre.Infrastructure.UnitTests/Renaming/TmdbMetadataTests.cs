using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Renaming;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Renaming;

public sealed class TmdbMetadataTests : IDisposable
{
    private static readonly string Hash = new('a', 64);
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "namerre-infra-tests", Guid.NewGuid().ToString("N"));
    private readonly List<HttpRequestMessage> requests = [];
    private readonly RenameStore store;
    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = _ => Task.FromResult(Json("""{"results":[]}"""));

    public TmdbMetadataTests()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "root"));
        store = new RenameStore(new BoundedFileSystem("root", workspace), "data", workspace);
    }

    public void Dispose()
    {
        store.Dispose();
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }

    public static TheoryData<TmdbSearchRequest> InvalidQueries => new()
    {
        new("person", "x"),
        new("Movie", "x"),
        new("movie", new string('x', 201)),
        new("movie", "", Id: 0),
        new("tv", "", ShowId: -1),
        new("tv", "", Season: -1),
        new("tv", "", Season: 1000),
        new("tv", "", Episode: 0),
        new("tv", "", Episode: 10000)
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task InvalidQueriesAreRejectedBeforeCallingTmdb(TmdbSearchRequest request)
    {
        var exception = await Assert.ThrowsAsync<RenameException>(() => Metadata().Search(request, default));

        Assert.Equal("InvalidMetadataQuery", exception.Code);
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData("en_US")]
    [InlineData("en US")]
    [InlineData("es&x=1")]
    [InlineData("abcdefghijklmnopqrstu")]
    public async Task InvalidLanguagesAreRejected(string language)
    {
        var exception = await Assert.ThrowsAsync<RenameException>(() =>
            Metadata().Search(new("movie", "query", language), default));

        Assert.Equal("InvalidLanguage", exception.Code);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task NullConfiguredLanguageFallsBackToEnglish()
    {
        await Metadata(language: null).Search(new("movie", "query", ""), default);

        Assert.Contains("language=en", Assert.Single(requests).RequestUri!.Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FreeTextSearchRequiresAQuery(string query)
    {
        var exception = await Assert.ThrowsAsync<RenameException>(() => Metadata().Search(new("movie", query), default));

        Assert.Equal("QueryRequired", exception.Code);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task SearchSendsBearerTokenAndEscapesTheQuery()
    {
        await Metadata().Search(new("tv", "Law & Order", "es-ES"), default);

        var request = Assert.Single(requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("server-secret", request.Headers.Authorization.Parameter);
        Assert.Equal("/3/search/tv", request.RequestUri!.AbsolutePath);
        Assert.Contains("query=Law%20%26%20Order", request.RequestUri.Query);
        Assert.Contains("language=es-ES", request.RequestUri.Query);
    }

    [Fact]
    public async Task TvResultsUseNameAndFirstAirDateAndAreCappedAtTwenty()
    {
        var items = string.Join(',', Enumerable.Range(1, 25).Select(i =>
            $$"""{"id":{{i}},"name":"Show {{i}}","first_air_date":"19{{i + 70}}-01-01"}"""));
        respond = _ => Task.FromResult(Json($$"""{"results":[{{items}}]}"""));

        var result = await Metadata().Search(new("tv", "show"), default);

        Assert.Equal(20, result.Matches.Count);
        Assert.Equal(new TmdbMatch(1, "tv", "Show 1", "1971", null, null, null), result.Matches[0]);
    }

    [Fact]
    public async Task MissingOrMalformedFieldsBecomeEmptyValues()
    {
        respond = _ => Task.FromResult(Json("""{"results":[{"id":7,"title":null,"release_date":"99"},{"id":8}]}"""));

        var result = await Metadata().Search(new("movie", "x"), default);

        Assert.Equal([new TmdbMatch(7, "movie", "", null, null, null, null),
            new TmdbMatch(8, "movie", "", null, null, null, null)], result.Matches);
    }

    [Fact]
    public async Task ResponsesWithoutResultsYieldNoMatches()
    {
        respond = _ => Task.FromResult(Json("""{"page":1}"""));

        Assert.Empty((await Metadata().Search(new("movie", "x"), default)).Matches);
    }

    [Fact]
    public async Task RateLimitWithoutRetryAfterUsesAGenericMessage()
    {
        respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var exception = await Assert.ThrowsAsync<RenameException>(() => Metadata().Search(new("movie", "x"), default));

        Assert.Equal("TmdbRateLimited", exception.Code);
        Assert.Equal(429, exception.Status);
        Assert.Equal("TMDB rate limit reached. Try later.", exception.Message);
    }

    [Fact]
    public async Task NetworkFailuresAreReportedAsUnavailable()
    {
        respond = _ => throw new HttpRequestException("connection refused to api.themoviedb.org");

        var exception = await Assert.ThrowsAsync<RenameException>(() => Metadata().Search(new("movie", "x"), default));

        Assert.Equal("TmdbUnavailable", exception.Code);
        Assert.Equal(503, exception.Status);
    }

    [Fact]
    public async Task InvalidJsonIsReportedAsABadGateway()
    {
        respond = _ => Task.FromResult(Json("<html>"));

        var exception = await Assert.ThrowsAsync<RenameException>(() => Metadata().Search(new("movie", "x"), default));

        Assert.Equal("TmdbInvalidResponse", exception.Code);
        Assert.Equal(502, exception.Status);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsATimeout()
    {
        using var cancellation = new CancellationTokenSource();
        respond = _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Metadata().Search(new("movie", "x"), cancellation.Token));
    }

    [Fact]
    public async Task FailedResponsesAreNotCached()
    {
        var calls = 0;
        respond = _ => Task.FromResult(++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json("""{"results":[{"id":1,"title":"Alien","release_date":"1979-05-25"}]}"""));
        var metadata = Metadata();

        await Assert.ThrowsAsync<RenameException>(() => metadata.Search(new("movie", "alien"), default));
        var result = await metadata.Search(new("movie", "alien"), default);

        Assert.Equal("1979", Assert.Single(result.Matches).Year);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CacheIsKeyedByQueryAndLanguageAndSurvivesEviction()
    {
        var metadata = Metadata();

        await metadata.Search(new("movie", "a", "en"), default);
        await metadata.Search(new("movie", "a", "fr"), default);
        await metadata.Search(new("movie", "a", "en"), default);
        Assert.Equal(2, requests.Count);

        // Filling the cache beyond its 256-entry bound clears it rather than growing without limit.
        for (var i = 0; i < 260; i++) await metadata.Search(new("movie", "q" + i), default);
        await metadata.Search(new("movie", "a", "en"), default);
        Assert.Equal(2 + 260 + 1, requests.Count);
    }

    [Fact]
    public async Task ConcurrentLookupsBeyondFourAreRejected()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        respond = async _ =>
        {
            if (Interlocked.Increment(ref started) == 4) allStarted.TrySetResult();
            await release.Task;
            return Json("""{"results":[]}""");
        };
        var metadata = Metadata();
        var running = Enumerable.Range(0, 4).Select(i => metadata.Search(new("movie", "q" + i), default)).ToArray();
        try
        {
            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var busy = await Assert.ThrowsAsync<RenameException>(() => metadata.Search(new("movie", "fifth"), default))
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("TmdbBusy", busy.Code);
            Assert.Equal(429, busy.Status);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(5));
        }
        await metadata.Search(new("movie", "after"), default);
    }

    public static TheoryData<TmdbSearchRequest> UnboundConfirmations => new()
    {
        Confirm() with { FileId = null },
        Confirm() with { FileId = " " },
        Confirm() with { FileId = new string('f', 101) },
        Confirm() with { InstanceId = null },
        Confirm() with { InstanceId = new string('i', 101) },
        Confirm() with { InputPath = null },
        Confirm() with { OptionsHash = null },
        Confirm() with { OptionsHash = "abc" },
        Confirm() with { OptionsHash = new string('z', 64) }
    };

    [Theory]
    [MemberData(nameof(UnboundConfirmations))]
    public async Task ConfirmationRequiresACompleteBinding(TmdbSearchRequest request)
    {
        var exception = await Assert.ThrowsAsync<RenameException>(() => Metadata().Search(request, default));

        Assert.Equal("MissingBinding", exception.Code);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task ConfirmationRejectsUnsafeInputPaths()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Metadata().Search(Confirm() with { InputPath = "../outside.mkv" }, default));
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData("""{"id":5,"title":""}""")]
    [InlineData("""{"id":5}""")]
    public async Task ConfirmationRequiresATitle(string details)
    {
        respond = _ => Task.FromResult(Json(details));

        var exception = await Assert.ThrowsAsync<RenameException>(() => Metadata().Search(Confirm(), default));

        Assert.Equal("MetadataIncomplete", exception.Code);
        Assert.Equal(502, exception.Status);
    }

    [Fact]
    public async Task EpisodeConfirmationRequiresAnEpisodeTitle()
    {
        respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/episode/")
            ? Json("""{"name":" "}""")
            : Json("""{"name":"Show","first_air_date":"2010-01-01"}"""));

        var exception = await Assert.ThrowsAsync<RenameException>(() => Metadata().Search(
            Confirm() with { MediaType = "tv", Season = 1, Episode = 1 }, default));

        Assert.Equal("MetadataIncomplete", exception.Code);
    }

    [Fact]
    public async Task ConfirmedMatchesResolveOnlyForTheirExactBinding()
    {
        respond = _ => Task.FromResult(Json("""{"id":5,"title":"Alien","release_date":"1979-05-25"}"""));
        var metadata = Metadata();

        var match = Assert.Single((await metadata.Search(Confirm(), default)).Matches);

        Assert.Equal("/3/movie/5", Assert.Single(requests).RequestUri!.AbsolutePath);
        Assert.Matches("^[0-9a-f]{32}$", match.ConfirmationToken);
        var selection = new MetadataSelection("file", "step", "movie.mkv", Hash, match.ConfirmationToken!);
        Assert.Equal(match, metadata.Resolve(selection));
        foreach (var other in new[]
        {
            selection with { FileId = "other" },
            selection with { InstanceId = "other" },
            selection with { InputPath = "other.mkv" },
            selection with { OptionsHash = new string('b', 64) }
        })
            Assert.Equal("MetadataStale", Assert.Throws<RenameException>(() => metadata.Resolve(other)).Code);
    }

    [Fact]
    public void UnknownConfirmationTokensAreStale()
    {
        var exception = Assert.Throws<RenameException>(() => Metadata().Resolve(
            new MetadataSelection("file", "step", "movie.mkv", Hash, Guid.NewGuid().ToString("N"))));

        Assert.Equal("MetadataStale", exception.Code);
    }

    private static TmdbSearchRequest Confirm() => new("movie", "", Id: 5, FileId: "file", InstanceId: "step",
        InputPath: "movie.mkv", OptionsHash: Hash);

    private TmdbMetadata Metadata(string? language = "en") => new(
        new HttpClient(new StubHandler(request =>
        {
            lock (requests) requests.Add(request);
            return respond(request);
        })),
        Options.Create(new NamerreOptions
        {
            Tmdb = new TmdbOptions { ReadAccessToken = "server-secret", Language = language }
        }),
        store);

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => response(request);
    }
}
