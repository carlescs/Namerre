using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Integrations;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Integrations;

public sealed class LibraryNotifierTests
{
    private const string Secret = "super-secret-value"; // betterleaks:allow
    private readonly List<(HttpRequestMessage Request, string? Body)> sent = [];
    private readonly RecordingLogger logger = new();
    private HttpStatusCode status = HttpStatusCode.OK;
    private bool throwOnSend;

    private static readonly RunView Run = new("run1", "plan1", null, "Rename", "Completed",
        [
            new("0", "a/one.txt", "a/ONE.txt", "", "h", "Completed"),
            new("1", "a/two.txt", "a/TWO.txt", "", "h", "Failed"),
            new("2", "a/three.txt", "a/THREE.txt", "", "h", "Completed")
        ], [], DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task NothingIsSentWhenUnconfigured()
    {
        var notifier = Create(new IntegrationsOptions());

        notifier.RunFinished(Run);
        await notifier.Deliver(Run, default);

        Assert.Empty(sent);
    }

    [Fact]
    public async Task JellyfinPostsALibraryRefreshWithTheTokenHeader()
    {
        await Create(new IntegrationsOptions { Jellyfin = new() { Url = " http://jelly:8096/ ", ApiKey = Secret } })
            .Deliver(Run, default);

        var (request, _) = Assert.Single(sent);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://jelly:8096/Library/Refresh", request.RequestUri!.ToString());
        Assert.Equal(Secret, Assert.Single(request.Headers.GetValues("X-Emby-Token")));
    }

    [Fact]
    public async Task EmbyPostsToTheEmbyPath()
    {
        await Create(new IntegrationsOptions { Emby = new() { Url = "https://emby.example/base", ApiKey = Secret } })
            .Deliver(Run, default);

        var (request, _) = Assert.Single(sent);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://emby.example/base/emby/Library/Refresh", request.RequestUri!.ToString());
        Assert.Equal(Secret, Assert.Single(request.Headers.GetValues("X-Emby-Token")));
    }

    [Fact]
    public async Task PlexRequestsAllSectionsWithTheTokenHeader()
    {
        await Create(new IntegrationsOptions { Plex = new() { Url = "http://plex:32400/", Token = Secret } })
            .Deliver(Run, default);

        var (request, _) = Assert.Single(sent);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://plex:32400/library/sections/all/refresh", request.RequestUri!.ToString());
        Assert.Equal(Secret, Assert.Single(request.Headers.GetValues("X-Plex-Token")));
    }

    [Fact]
    public async Task WebhookPostsCountsWithoutPathsByDefault()
    {
        await Create(new IntegrationsOptions { Webhook = new() { Url = "https://hooks.example/run" } })
            .Deliver(Run, default);

        var (request, body) = Assert.Single(sent);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://hooks.example/run", request.RequestUri!.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("run.finished", root.GetProperty("event").GetString());
        Assert.Equal("run1", root.GetProperty("runId").GetString());
        Assert.Equal("Rename", root.GetProperty("kind").GetString());
        Assert.Equal("Completed", root.GetProperty("status").GetString());
        var files = root.GetProperty("files");
        Assert.Equal(3, files.GetProperty("total").GetInt32());
        Assert.Equal(2, files.GetProperty("completed").GetInt32());
        Assert.Equal(1, files.GetProperty("failed").GetInt32());
        Assert.False(files.TryGetProperty("items", out _));
        Assert.DoesNotContain("one.txt", body);
    }

    [Fact]
    public async Task WebhookIncludesPathsOnlyWhenEnabled()
    {
        await Create(new IntegrationsOptions { Webhook = new() { Url = "https://hooks.example/run", IncludePaths = true } })
            .Deliver(Run, default);

        using var json = JsonDocument.Parse(Assert.Single(sent).Body!);
        var items = json.RootElement.GetProperty("files").GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("a/one.txt", items[0].GetProperty("sourcePath").GetString());
        Assert.Equal("a/ONE.txt", items[0].GetProperty("targetPath").GetString());
    }

    [Fact]
    public async Task EveryConfiguredTargetIsNotified()
    {
        await Create(new IntegrationsOptions
        {
            Jellyfin = new() { Url = "http://j", ApiKey = Secret },
            Emby = new() { Url = "http://e", ApiKey = Secret },
            Plex = new() { Url = "http://p", Token = Secret },
            Webhook = new() { Url = "http://w" }
        }).Deliver(Run, default);

        Assert.Equal(4, sent.Count);
    }

    [Fact]
    public async Task AFailingEndpointIsLoggedWithoutTheSecretAndOtherTargetsStillRun()
    {
        status = HttpStatusCode.InternalServerError;
        await Create(new IntegrationsOptions
        {
            Jellyfin = new() { Url = "http://j", ApiKey = Secret },
            Plex = new() { Url = "http://p", Token = Secret }
        }).Deliver(Run, default);

        Assert.Equal(2, sent.Count);
        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains("Jellyfin", logger.Warnings[0]);
        Assert.Contains("500", logger.Warnings[0]);
        Assert.All(logger.Warnings, warning => Assert.DoesNotContain(Secret, warning));
    }

    [Fact]
    public async Task ATransportFailureIsSwallowedAndLogged()
    {
        throwOnSend = true;
        var notifier = Create(new IntegrationsOptions { Webhook = new() { Url = "http://w/?key=" + Secret } });

        await notifier.Deliver(Run, default);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("Webhook", warning);
        Assert.DoesNotContain(Secret, warning);
    }

    [Fact]
    public async Task QueuedRunsAreDeliveredByTheBackgroundLoop()
    {
        var notifier = Create(new IntegrationsOptions { Webhook = new() { Url = "http://w" } });
        await notifier.StartAsync(default);

        notifier.RunFinished(Run);
        for (var i = 0; i < 100 && sent.Count == 0; i++) await Task.Delay(20);
        await notifier.StopAsync(default);

        Assert.Single(sent);
    }

    private LibraryNotifier Create(IntegrationsOptions integrations)
        => new(new StubFactory(this), Options.Create(new NamerreOptions { Integrations = integrations }), logger);

    private sealed class StubFactory(LibraryNotifierTests owner) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(owner));
    }

    private sealed class StubHandler(LibraryNotifierTests owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            owner.sent.Add((request, body));
            if (owner.throwOnSend) throw new HttpRequestException("connection refused for " + request.RequestUri);
            return new HttpResponseMessage(owner.status);
        }
    }

    private sealed class RecordingLogger : ILogger<LibraryNotifier>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;
            Warnings.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
        }
    }
}
