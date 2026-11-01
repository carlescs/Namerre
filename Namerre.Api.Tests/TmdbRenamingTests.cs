using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plans;
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

    [Theory]
    [InlineData("")]
    [InlineData("Library/Movies")]
    public async Task ConfirmationValidatesRemoteIdsBindsExactStepAndPersistsSnapshotWithoutCredentials(string baseFolder)
    {
        var prefix = baseFolder.Length > 0 ? baseFolder + "/" : "";
        var destination = prefix + "A Movie (2001)/A Movie (2001).mkv";
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
            var metadata = Metadata(client, Config("server-only-secret"), store);
            var options = JsonSerializer.SerializeToElement(new { nameTemplate = "", changeFolder = true,
                baseFolder, folderTemplate = "{title} ({year})" });
            var result = await metadata.Search(new("movie", "", Id: 42, FileId: "file", InstanceId: "tmdb1",
                InputPath: "input.mkv", OptionsHash: TmdbPlugin.OptionsHash(options)), default);
            var match = Assert.Single(result.Matches);
            Assert.Equal("A Movie", match.Title);
            Assert.NotNull(match.ConfirmationToken);
            selection = new("file", "tmdb1", "input.mkv", TmdbPlugin.OptionsHash(options), match.ConfirmationToken);
            Assert.Equal("A Movie", metadata.Resolve(selection).Title);
            Assert.Throws<RenameException>(() => metadata.Resolve(selection with { InstanceId = "different" }));
            var pipeline = new PipelineRequest([new("file", "input.mkv")],
                [new("tmdb1", "tmdb", 1, true, options)], [selection]);
            var engine = TestPipeline.Create(metadata, filesystem);
            var preview = await engine.Preview(pipeline, default);
            Assert.Equal(destination, Assert.Single(preview.Files).TargetPath);
            Assert.False(Directory.Exists(Path.Combine(Root, "A Movie (2001)")));
            if (baseFolder.Length > 0)
                Assert.False(Directory.Exists(Path.Combine(Root, baseFolder.Replace('/', Path.DirectorySeparatorChar))));
            var edited = pipeline with
            {
                Plugins = [new("tmdb1", "tmdb", 1, true, JsonSerializer.SerializeToElement(new {
                    nameTemplate = "{title}", changeFolder = true, folderTemplate = "{year}"
                }))]
            };
            var editedPreview = await engine.Preview(edited, default);
            Assert.True(editedPreview.CanPrepare);
            Assert.Equal("2001/A Movie.mkv", Assert.Single(editedPreview.Files).TargetPath);
            var service = new RenameService(store, filesystem, engine, NullLogger<RenameService>.Instance);
            var prepared = await service.Prepare(pipeline, default);
            await service.ProcessNext(default);
            plan = await service.GetPlan(prepared.Id, default);
            Assert.Equal("Ready", plan.Status);
            Assert.Equal(destination, Assert.Single(plan.Files).TargetPath);
        }
        using (var store = new RenameStore(filesystem, Data, workspace))
        {
            var metadata = Metadata(client, Config(""), store);
            Assert.Equal("A Movie", metadata.Resolve(selection).Title);
            var service = new RenameService(store, filesystem, TestPipeline.Create(metadata, filesystem), NullLogger<RenameService>.Instance);
            var job = await service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
            await service.ProcessNext(default);
            var run = await service.GetRun(job.Id, default);
            Assert.Equal("Completed", run.Status);
            Assert.Contains(run.Journal, j => j.Kind == "CreateDirectory" && j.Status == "Completed");
            Assert.Equal("content", File.ReadAllText(Path.Combine(Root, destination.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(1, calls);
        }
        foreach (var file in Directory.GetFiles(Data, "renaming.sqlite3*"))
            Assert.DoesNotContain("server-only-secret", Encoding.UTF8.GetString(File.ReadAllBytes(file)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptiedFoldersAreDeletedOnlyWhenRequested(bool deleteEmptyFolders)
    {
        WriteRelease();
        Directory.CreateDirectory(Path.Combine(Root, "Downloads", "Release", "Extras"));
        File.WriteAllText(Path.Combine(Root, "Downloads", "keep.txt"), "kept");
        var filesystem = new BoundedFileSystem(Root, workspace);
        using var store = new RenameStore(filesystem, Data, workspace);
        using var client = MovieClient();
        var metadata = Metadata(client, Config("secret"), store);
        var service = new RenameService(store, filesystem, TestPipeline.Create(metadata, filesystem), NullLogger<RenameService>.Instance);
        var id = await MoveMovie(service, metadata, deleteEmptyFolders);

        var run = await service.GetRun(id, default);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(deleteEmptyFolders, run.DeleteEmptyFolders);
        Assert.Equal("content", File.ReadAllText(MovieTarget));
        // A nested folder that was already empty is not something the run emptied.
        Assert.True(Directory.Exists(Path.Combine(Root, "Downloads", "Release", "Extras")));
        Assert.True(File.Exists(Path.Combine(Root, "Downloads", "keep.txt")));
        Assert.DoesNotContain(run.Journal, j => j.Kind == "DeleteDirectory");

        var second = await service.Rollback(id, new(["file"], Guid.NewGuid().ToString()), default);
        await service.ProcessNext(default);
        var rollback = await service.GetRun(second.Id, default);
        Assert.Equal("Completed", rollback.Status);
        Assert.Equal("content", File.ReadAllText(Path.Combine(Root, "Downloads", "Release", "input.mkv")));
        Assert.Equal(!deleteEmptyFolders, Directory.Exists(Path.Combine(Root, "A Movie (2001)")));
        Assert.Equal(deleteEmptyFolders, rollback.Journal.Any(j => j.Kind == "DeleteDirectory"
            && j.FromPath == "A Movie (2001)" && j.Status == "Completed"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RollbackCanOverrideTheInheritedFolderCleanup(bool deleteEmptyFolders)
    {
        WriteRelease();
        var filesystem = new BoundedFileSystem(Root, workspace);
        using var store = new RenameStore(filesystem, Data, workspace);
        using var client = MovieClient();
        var metadata = Metadata(client, Config("secret"), store);
        var service = new RenameService(store, filesystem, TestPipeline.Create(metadata, filesystem), NullLogger<RenameService>.Instance);
        var id = await MoveMovie(service, metadata, !deleteEmptyFolders);

        var second = await service.Rollback(id, new(["file"], Guid.NewGuid().ToString(), deleteEmptyFolders), default);
        await service.ProcessNext(default);
        var rollback = await service.GetRun(second.Id, default);
        Assert.Equal("Completed", rollback.Status);
        Assert.Equal(deleteEmptyFolders, rollback.DeleteEmptyFolders);
        Assert.Equal("content", File.ReadAllText(Path.Combine(Root, "Downloads", "Release", "input.mkv")));
        Assert.Equal(!deleteEmptyFolders, Directory.Exists(Path.Combine(Root, "A Movie (2001)")));
    }

    [Fact]
    public async Task EmptiedSourceFoldersAreDeletedDeepestFirstButNeverTheRootOrOccupiedFolders()
    {
        File.WriteAllText(Path.Combine(Root, "input.mkv"), "root file");
        WriteRelease();
        var filesystem = new BoundedFileSystem(Root, workspace);
        using var store = new RenameStore(filesystem, Data, workspace);
        using var client = MovieClient();
        var metadata = Metadata(client, Config("secret"), store);
        var service = new RenameService(store, filesystem, TestPipeline.Create(metadata, filesystem), NullLogger<RenameService>.Instance);
        var id = await MoveMovie(service, metadata, deleteEmptyFolders: true);

        var run = await service.GetRun(id, default);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(["Downloads/Release", "Downloads"], run.Journal.Where(j => j.Kind == "DeleteDirectory")
            .Select(j => j.FromPath));
        Assert.All(run.Journal, j => Assert.Equal("Completed", j.Status));
        Assert.False(Directory.Exists(Path.Combine(Root, "Downloads")));
        Assert.True(Directory.Exists(Root));
        Assert.Equal("root file", File.ReadAllText(Path.Combine(Root, "input.mkv")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderThatCannotBeInspectedOrDeletedIsJournaledAsFailedAndTheRunCompletes(bool inspection)
    {
        WriteRelease();
        var bounded = new BoundedFileSystem(Root, workspace);
        var filesystem = new CleanupFaultFileSystem(bounded, "Downloads/Release", inspection);
        using var store = new RenameStore(bounded, Data, workspace);
        using var client = MovieClient();
        var metadata = Metadata(client, Config("secret"), store);
        var service = new RenameService(store, filesystem, TestPipeline.Create(metadata, filesystem), NullLogger<RenameService>.Instance);
        var id = await MoveMovie(service, metadata, deleteEmptyFolders: true);

        var run = await service.GetRun(id, default);
        Assert.Equal("Completed", run.Status);
        Assert.Equal("content", File.ReadAllText(MovieTarget));
        // The folder that failed keeps its parent occupied, so the parent is never a candidate.
        Assert.Equal([("Downloads/Release", "Failed")], run.Journal.Where(j => j.Kind == "DeleteDirectory")
            .Select(j => (j.FromPath, j.Status)));
        Assert.True(Directory.Exists(Path.Combine(Root, "Downloads", "Release")));
    }

    // Counts the run's write boundaries once the file is placed: the Place completion and file status (1-4), the
    // intent and outcome of deleting Downloads/Release (5-8) and then Downloads (9-12), and finalization (13-14).
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public async Task EveryFolderCleanupBoundaryCanBeExplicitlyRecovered(int boundaryNumber)
    {
        WriteRelease();
        var filesystem = new BoundedFileSystem(Root, workspace);
        var boundaries = 0;
        var fired = false;
        string id;
        using (var store = new RenameStore(filesystem, Data, workspace, name =>
        {
            if ((name.StartsWith("Save:Run:Running:", StringComparison.Ordinal) || name.StartsWith("Finalize:", StringComparison.Ordinal))
                && File.Exists(MovieTarget) && ++boundaries == boundaryNumber && !fired)
            {
                fired = true;
                throw new IOException("Injected write boundary.");
            }
        }))
        {
            using var client = MovieClient();
            var metadata = Metadata(client, Config("secret"), store);
            var service = new RenameService(store, filesystem, TestPipeline.Create(metadata, filesystem), NullLogger<RenameService>.Instance);
            id = await MoveMovie(service, metadata, deleteEmptyFolders: true);
            Assert.True(fired);
            Assert.Equal("RecoveryRequired", (await service.GetRun(id, default)).Status);
        }
        using (var store = new RenameStore(filesystem, Data, workspace))
        {
            using var client = new HttpClient();
            var service = new RenameService(store, filesystem,
                TestPipeline.Create(Metadata(client, Config(""), store), filesystem), NullLogger<RenameService>.Instance);
            var evidence = await service.PreviewRecovery(id, default);
            Assert.True(evidence.CanRecover, JsonSerializer.Serialize(evidence));
            await service.Recover(id, new("RestoreOriginal", Guid.NewGuid().ToString()), default);
            await service.ProcessNext(default);
            Assert.Equal("Recovered", (await service.GetRun(id, default)).Status);
            Assert.Equal("content", File.ReadAllText(Path.Combine(Root, "Downloads", "Release", "input.mkv")));
            Assert.False(File.Exists(MovieTarget));
        }
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
        var metadata = Metadata(client, Config("secret"), store);
        var options = JsonSerializer.SerializeToElement(new { nameTemplate = "" });
        var query = new TmdbSearchRequest("tv", "", Id: 42, FileId: "file", InstanceId: "step",
            InputPath: "input.mkv", OptionsHash: TmdbPlugin.OptionsHash(options));
        Assert.Equal("EpisodeRequired", (await Assert.ThrowsAsync<RenameException>(
            () => metadata.Search(query, default))).Code);
        var confirmed = await metadata.Search(query with { Season = 1, Episode = 2 }, default);
        var token = Assert.Single(confirmed.Matches).ConfirmationToken!;
        var preview = await TestPipeline.Create(metadata, filesystem).Preview(new([new("file", "input.mkv")],
            [new("step", "tmdb", 1, true, options)], [new("file", "step", "input.mkv",
                TmdbPlugin.OptionsHash(options), token)]), default);
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
        var metadata = Metadata(client, Config("secret"), store);
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
        var metadata = Metadata(client, Config("secret", configuredLanguage), store);

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
        var metadata = Metadata(client, Config("secret"), store);
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
        var missing = Metadata(client, Config(""), store);
        Assert.Equal("TmdbUnavailable", (await Assert.ThrowsAsync<RenameException>(
            () => missing.Search(new("movie", "query"), default))).Code);
        var timeout = Metadata(client, Config("secret"), store);
        Assert.Equal("TmdbTimeout", (await Assert.ThrowsAsync<RenameException>(
            () => timeout.Search(new("movie", "query"), default))).Code);
    }

    private string MovieTarget => Path.Combine(Root, "A Movie (2001)", "A Movie (2001).mkv");
    private void WriteRelease()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Downloads", "Release"));
        File.WriteAllText(Path.Combine(Root, "Downloads", "Release", "input.mkv"), "content");
    }
    private static HttpClient MovieClient()
        => new(new StubHandler(_ => Response("""{"id":42,"title":"A Movie","release_date":"2001-02-03"}""")));
    // Confirms the TMDB match, then prepares and executes the move of Downloads/Release/input.mkv into "A Movie (2001)".
    private static async Task<string> MoveMovie(RenameService service, TmdbMetadata metadata, bool deleteEmptyFolders)
    {
        var options = JsonSerializer.SerializeToElement(new { nameTemplate = "", changeFolder = true,
            folderTemplate = "{title} ({year})" });
        var hash = TmdbPlugin.OptionsHash(options);
        var token = Assert.Single((await metadata.Search(new("movie", "", Id: 42, FileId: "file", InstanceId: "tmdb1",
            InputPath: "Downloads/Release/input.mkv", OptionsHash: hash), default)).Matches).ConfirmationToken!;
        var prepared = await service.Prepare(new([new("file", "Downloads/Release/input.mkv")],
            [new("tmdb1", "tmdb", 1, true, options)], [new("file", "tmdb1", "Downloads/Release/input.mkv", hash, token)],
            DeleteEmptyFolders: deleteEmptyFolders), default);
        await service.ProcessNext(default);
        var job = await service.Execute(prepared.Id, new(1, Guid.NewGuid().ToString()), default);
        await service.ProcessNext(default);
        return job.Id;
    }
    private static TmdbMetadata Metadata(HttpClient client, IOptions<NamerreOptions> config, RenameStore store)
        => new(new CachedTmdbClient(() => new TmdbClient(client, config)), config, store);
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
    // Fails inspecting or deleting one folder during cleanup; everything else reaches the real filesystem.
    private sealed class CleanupFaultFileSystem(IRenameFileSystem inner, string folder, bool inspection) : IRenameFileSystem
    {
        public string RootBinding => inner.RootBinding;
        public void Validate(string path) => inner.Validate(path);
        public bool Exists(string path) => inner.Exists(path);
        public string ActualPath(string path) => inner.ActualPath(path);
        public bool SamePath(string first, string second) => inner.SamePath(first, second);
        public Task<(string Hash, long Size)> Hash(string path, CancellationToken cancellationToken) => inner.Hash(path, cancellationToken);
        public (string Hash, long Size) Stamp(string path) => inner.Stamp(path);
        public FileFacts Facts(string path) => inner.Facts(path);
        public void CheckSameDevice(string source, string target) => inner.CheckSameDevice(source, target);
        public void Move(string source, string destination) => inner.Move(source, destination);
        public IReadOnlyList<string> MissingDirectories(string path) => inner.MissingDirectories(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public bool IsEmptyDirectory(string path) => inspection && path == folder
            ? throw new UnauthorizedAccessException("Injected inspection failure.") : inner.IsEmptyDirectory(path);
        public bool DeleteEmptyDirectory(string path) => !inspection && path == folder
            ? throw new IOException("Injected deletion failure.") : inner.DeleteEmptyDirectory(path);
    }
    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        var parent = Path.GetDirectoryName(workspace)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }
}
