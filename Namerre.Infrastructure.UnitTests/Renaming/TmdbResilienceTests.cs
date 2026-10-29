using System.Net;
using System.Text;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Renaming;
using Polly;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Renaming;

public sealed class TmdbResilienceTests
{
    private int calls;
    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = _ => Task.FromResult(Json("""{"id":1}"""));

    [Fact]
    public async Task TransientFailuresAreRetried()
    {
        respond = _ => Task.FromResult(calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json("""{"id":7}"""));

        var result = await Client().Get("movie/7", default);

        Assert.Equal(7, result.GetProperty("id").GetInt32());
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "TmdbRateLimited")]
    [InlineData(HttpStatusCode.NotFound, "MetadataNotFound")]
    [InlineData(HttpStatusCode.Unauthorized, "TmdbUnavailable")]
    public async Task RateLimitsAndPermanentFailuresAreNotRetried(HttpStatusCode status, string code)
    {
        respond = _ => Task.FromResult(new HttpResponseMessage(status));

        var exception = await Assert.ThrowsAsync<RenameException>(() => Client().Get("movie/7", default));

        Assert.Equal(code, exception.Code);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RepeatedFailuresOpenTheCircuit()
    {
        respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = Client();

        // Three attempts per lookup; the fourth failure in the window opens the circuit.
        await Assert.ThrowsAsync<RenameException>(() => client.Get("movie/1", default));
        await Assert.ThrowsAsync<RenameException>(() => client.Get("movie/2", default));
        var open = await Assert.ThrowsAsync<RenameException>(() => client.Get("movie/3", default));

        Assert.Equal("TmdbUnavailable", open.Code);
        Assert.Equal(503, open.Status);
        Assert.Equal(4, calls);
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
        var client = Client();
        var running = Enumerable.Range(0, 4).Select(i => client.Get("search/movie?query=q" + i, default)).ToArray();
        try
        {
            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var busy = await Assert.ThrowsAsync<RenameException>(() => client.Get("search/movie?query=fifth", default))
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("TmdbBusy", busy.Code);
            Assert.Equal(429, busy.Status);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(5));
        }
        await client.Get("search/movie?query=after", default);
    }

    // The same pipeline AddInfrastructure attaches, in front of a stub transport.
    private TmdbClient Client()
    {
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>();
        TmdbResilience.Configure(pipeline);
        var handler = new ResilienceHandler(pipeline.Build())
        {
            InnerHandler = new StubHandler(request =>
            {
                Interlocked.Increment(ref calls);
                return respond(request);
            })
        };
        return new TmdbClient(new HttpClient(handler), Options.Create(new NamerreOptions
        {
            Tmdb = new TmdbOptions { ReadAccessToken = "server-secret" }
        }));
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => response(request);
    }
}
