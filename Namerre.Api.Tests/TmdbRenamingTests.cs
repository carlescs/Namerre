using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Renaming;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Renaming;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class TmdbRenamingTests : IDisposable
{
    private readonly string workspace = Path.Combine(Directory.GetCurrentDirectory(), ".metadata-tests", Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(workspace, "media");
    private string Data => Path.Combine(workspace, "data");
    public TmdbRenamingTests() => Directory.CreateDirectory(Root);

    [Fact]
    public async Task ConfirmationValidatesRemoteIdsBindsExactStepAndPersistsSnapshotWithoutCredentials()
    {
        File.WriteAllText(Path.Combine(Root, "input.mkv"), "content");
        var filesystem = new BoundedFileSystem(Root, workspace);
        var calls = 0;
        using var handler = new StubHandler(request =>
        {
            calls++;
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("server-only-secret", request.Headers.Authorization.Parameter);
            Assert.StartsWith("https://api.themoviedb.org/3/movie/42", request.RequestUri!.AbsoluteUri);
            return Response("""{"id":42,"title":"A Movie","release_date":"2001-02-03"}""");
        });
        using var client = new HttpClient(handler);
        MetadataSelection selection;
        PlanView plan;
        using (var store = new RenameStore(filesystem, Data, workspace))
        {
            var metadata = new TmdbMetadata(client, Config("server-only-secret"), store);
            var options = JsonSerializer.SerializeToElement(new { nameTemplate = "", changeFolder = true,
                folderTemplate = "{title} ({year})" });
            var result = await metadata.Search(new("movie", "", Id: 42, FileId: "file", InstanceId: "tmdb1",
                InputPath: "input.mkv", OptionsHash: PipelineEngine.OptionsHash(options)), default);
            var match = Assert.Single(result.Matches);
            Assert.Equal("A Movie", match.Title);
            Assert.NotNull(match.ConfirmationToken);
            selection = new("file", "tmdb1", "input.mkv", PipelineEngine.OptionsHash(options), match.ConfirmationToken);
            Assert.Equal("A Movie", metadata.Resolve(selection).Title);
            Assert.Throws<RenameException>(() => metadata.Resolve(selection with { InstanceId = "different" }));
            var pipeline = new PipelineRequest([new("file", "input.mkv")],
                [new("tmdb1", "tmdb", 1, true, options)], [selection]);
            var engine = new PipelineEngine(metadata, filesystem);
            var preview = await engine.Preview(pipeline, default);
            Assert.Equal("A Movie (2001)/A Movie (2001).mkv", Assert.Single(preview.Files).TargetPath);
            Assert.False(Directory.Exists(Path.Combine(Root, "A Movie (2001)")));
            var service = new RenameService(store, filesystem, engine, NullLogger<RenameService>.Instance);
            var prepared = await service.Prepare(pipeline, default);
            await service.ProcessNext(default);
            plan = await service.GetPlan(prepared.Id, default);
            Assert.Equal("Ready", plan.Status);
        }
        using (var store = new RenameStore(filesystem, Data, workspace))
        {
            var metadata = new TmdbMetadata(client, Config(""), store);
            Assert.Equal("A Movie", metadata.Resolve(selection).Title);
            var service = new RenameService(store, filesystem, new PipelineEngine(metadata, filesystem), NullLogger<RenameService>.Instance);
            var job = await service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
            await service.ProcessNext(default);
            var run = await service.GetRun(job.Id, default);
            Assert.Equal("Completed", run.Status);
            Assert.Contains(run.Journal, j => j.Kind == "CreateDirectory" && j.Status == "Completed");
            Assert.Equal("content", File.ReadAllText(Path.Combine(Root, "A Movie (2001)", "A Movie (2001).mkv")));
            Assert.Equal(1, calls);
        }
        foreach (var file in Directory.GetFiles(Data, "renaming.sqlite3*"))
            Assert.DoesNotContain("server-only-secret", Encoding.UTF8.GetString(File.ReadAllBytes(file)));
    }

    [Fact]
    public async Task EpisodeConfirmationUsesExplicitShowSeasonEpisodeAndAppropriateDefaultName()
    {
        File.WriteAllText(Path.Combine(Root, "input.mkv"), "content");
        var filesystem = new BoundedFileSystem(Root, workspace);
        using var store = new RenameStore(filesystem, Data, workspace);
        using var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.Contains("/episode/", StringComparison.Ordinal)
            ? Response("""{"id":123,"name":"The Episode","season_number":1,"episode_number":2}""")
            : Response("""{"id":42,"name":"The Show","first_air_date":"2010-01-01"}"""));
        using var client = new HttpClient(handler);
        var metadata = new TmdbMetadata(client, Config("secret"), store);
        var options = JsonSerializer.SerializeToElement(new { nameTemplate = "" });
        var query = new TmdbSearchRequest("tv", "", Id: 42, FileId: "file", InstanceId: "step",
            InputPath: "input.mkv", OptionsHash: PipelineEngine.OptionsHash(options));
        Assert.Equal("EpisodeRequired", (await Assert.ThrowsAsync<RenameException>(
            () => metadata.Search(query, default))).Code);
        var confirmed = await metadata.Search(query with { Season = 1, Episode = 2 }, default);
        var token = Assert.Single(confirmed.Matches).ConfirmationToken!;
        var preview = await new PipelineEngine(metadata, filesystem).Preview(new([new("file", "input.mkv")],
            [new("step", "tmdb", 1, true, options)], [new("file", "step", "input.mkv",
                PipelineEngine.OptionsHash(options), token)]), default);
        Assert.Equal("The Show - S01E02 - The Episode.mkv", Assert.Single(preview.Files).TargetPath);
    }

    [Fact]
    public async Task SearchProposesCandidatesWithoutImplicitlyConfirmingAndCachesResults()
    {
        var filesystem = new BoundedFileSystem(Root, workspace);
        using var store = new RenameStore(filesystem, Data, workspace);
        var calls = 0;
        using var handler = new StubHandler(_ =>
        {
            calls++;
            return Response("""{"results":[{"id":1,"title":"First","release_date":"2000-01-01"},{"id":2,"title":"Second","release_date":""}]}""");
        });
        using var client = new HttpClient(handler);
        var metadata = new TmdbMetadata(client, Config("secret"), store);
        for (var i = 0; i < 2; i++)
        {
            var result = await metadata.Search(new("movie", "candidate"), default);
            Assert.Equal(2, result.Matches.Count);
            Assert.All(result.Matches, m => Assert.Null(m.ConfirmationToken));
        }
        Assert.Equal(1, calls);
        Assert.False(Directory.Exists(Data));
    }

    [Theory]
    [InlineData(null, "", "en")]
    [InlineData("fr", "", "fr")]
    [InlineData("fr", "de", "de")]
    public async Task SearchUsesConfiguredLanguageUnlessTheRequestOverridesIt(
        string? configuredLanguage, string requestLanguage, string expectedLanguage)
    {
        var filesystem = new BoundedFileSystem(Root, workspace);
        using var store = new RenameStore(filesystem, Data, workspace);
        using var handler = new StubHandler(request =>
        {
            Assert.Contains($"language={expectedLanguage}", request.RequestUri!.Query);
            return Response("""{"results":[]}""");
        });
        using var client = new HttpClient(handler);
        var metadata = new TmdbMetadata(client, Config("secret", configuredLanguage), store);

        await metadata.Search(new("movie", "query", Language: requestLanguage), default);
    }

    [Theory]
    [InlineData(429, "TmdbRateLimited")]
    [InlineData(404, "MetadataNotFound")]
    [InlineData(500, "TmdbUnavailable")]
    [InlineData(401, "TmdbUnavailable")]
    public async Task HandlesRemoteFailureWithoutLeakingTokensOrRawErrors(int status, string code)
    {
        var filesystem = new BoundedFileSystem(Root, workspace);
        using var store = new RenameStore(filesystem, Data, workspace);
        using var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });
        using var client = new HttpClient(handler);
        var metadata = new TmdbMetadata(client, Config("secret"), store);
        var exception = await Assert.ThrowsAsync<RenameException>(() => metadata.Search(new("movie", "query"), default));
        Assert.Equal(code, exception.Code);
        Assert.DoesNotContain("secret", exception.Message);
        if (status == 429) Assert.Contains("7", exception.Message);
    }

    [Fact]
    public async Task MissingCredentialsAndTimeoutsHaveExplicitErrors()
    {
        var filesystem = new BoundedFileSystem(Root, workspace);
        using var store = new RenameStore(filesystem, Data, workspace);
        using var handler = new StubHandler(_ => throw new OperationCanceledException());
        using var client = new HttpClient(handler);
        var missing = new TmdbMetadata(client, Config(""), store);
        Assert.Equal("TmdbUnavailable", (await Assert.ThrowsAsync<RenameException>(
            () => missing.Search(new("movie", "query"), default))).Code);
        var timeout = new TmdbMetadata(client, Config("secret"), store);
        Assert.Equal("TmdbTimeout", (await Assert.ThrowsAsync<RenameException>(
            () => timeout.Search(new("movie", "query"), default))).Code);
    }

    private static IOptions<NamerreOptions> Config(string token, string? language = "en") => Options.Create(new NamerreOptions
    {
        Tmdb = new TmdbOptions { ReadAccessToken = token, Language = language }
    });
    private static HttpResponseMessage Response(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        var parent = Path.GetDirectoryName(workspace)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }
}
