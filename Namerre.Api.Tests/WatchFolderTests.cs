using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Presets;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Folders;
using Namerre.Infrastructure.Renaming;
using Namerre.Infrastructure.WatchFolders;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class WatchFolderTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private readonly string workspace = Path.Combine(Directory.GetCurrentDirectory(), ".watch-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeMetadata metadata = new();
    private readonly RenameStore store;
    private readonly RenameService renames;
    private readonly PresetService presets;
    private readonly BoundedFileSystem fileSystem;
    private readonly WatchFolderOptions folder = new()
    {
        Name = "Downloads", Path = "downloads", Preset = "Upper", IntervalSeconds = 10, StableSeconds = 30, MaxAttempts = 2
    };

    public WatchFolderTests()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "media", "downloads"));
        fileSystem = new BoundedFileSystem("media", workspace);
        store = new RenameStore(fileSystem, "data", workspace);
        var engine = TestPipeline.Create(metadata, fileSystem);
        renames = new RenameService(store, fileSystem, engine, NullLogger<RenameService>.Instance);
        presets = new PresetService(new PresetStore(store), engine);
        presets.Create(new("Upper", [Step("case", new { mode = "upper" })]));
    }

    [Fact]
    public async Task RenamesStableFilesThroughANormalPlanAndRunAndNeverTakesThemAgain()
    {
        Write("downloads/show.txt", "A");
        var service = Service();

        await Scan(service);
        await Scan(service, TimeSpan.FromSeconds(20));
        // Twenty seconds of an unchanged size and time is not yet stable.
        Assert.Empty(store.List<PlanView>("Plan"));

        await Scan(service, TimeSpan.FromSeconds(15));
        var plan = Assert.Single(store.List<PlanView>("Plan"));
        Assert.Equal("watch:Downloads", plan.Origin);
        Assert.Equal(1, Assert.Single(service.List()).InFlight);
        Assert.True(await renames.ProcessNext(default));

        await Scan(service);
        var run = Assert.Single(store.List<RunView>("Run"));
        Assert.Equal("watch:Downloads", run.Origin);
        Assert.True(await renames.ProcessNext(default));
        Assert.True(File.Exists(Full("downloads/SHOW.txt")));

        await Scan(service);
        var status = Assert.Single(service.List());
        Assert.Equal((0, 1, null), (status.InFlight, status.Completed, status.Error));
        // The renamed file sits in the watched folder too, but it is recorded as done under its new name.
        for (var i = 0; i < 5; i++) await Scan(service, TimeSpan.FromMinutes(1));
        Assert.Single(store.List<PlanView>("Plan"));
    }

    [Fact]
    public async Task AFileThatKeepsChangingIsNotTaken()
    {
        var service = Service();
        for (var i = 0; i < 6; i++)
        {
            Write("downloads/growing.txt", new string('x', i + 1));
            await Scan(service, TimeSpan.FromSeconds(20));
        }
        Assert.Empty(store.List<PlanView>("Plan"));
    }

    [Fact]
    public async Task FailingFilesBackOffAndAreIgnoredUntilTheyChangeOrAreRetried()
    {
        // Uppercasing b.txt needs B.txt, which is taken; case-insensitive filesystems cannot hold both.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return;
        Write("downloads/b.txt", "taken");
        Write("downloads/B.txt", "occupied");
        var service = Service();
        await Stabilize(service);

        var problem = Assert.Single(Assert.Single(service.List()).Problems, p => p.Path == "downloads/b.txt");
        Assert.Equal(("Failed", 1), (problem.Status, problem.Attempts));
        Assert.Equal(clock.GetUtcNow() + Interval, problem.NextAttemptAt);

        await Scan(service, TimeSpan.FromSeconds(5));
        Assert.Equal(1, Assert.Single(service.List()).Problems.Single(p => p.Path == "downloads/b.txt").Attempts);
        await Scan(service, TimeSpan.FromSeconds(10));
        problem = Assert.Single(service.List()).Problems.Single(p => p.Path == "downloads/b.txt");
        Assert.Equal(("Ignored", 2, (DateTimeOffset?)null), (problem.Status, problem.Attempts, problem.NextAttemptAt));
        for (var i = 0; i < 3; i++) await Scan(service, TimeSpan.FromHours(1));
        Assert.Equal(2, Assert.Single(service.List()).Problems.Single(p => p.Path == "downloads/b.txt").Attempts);

        service.Retry("downloads", "downloads/b.txt");
        Assert.DoesNotContain(Assert.Single(service.List()).Problems, p => p.Path == "downloads/b.txt");
        Assert.Throws<RenameException>(() => service.Retry("Downloads", "downloads/b.txt"));
    }

    [Fact]
    public async Task PausedFoldersAreNotScannedUntilResumed()
    {
        Write("downloads/a.txt", "A");
        var service = Service();
        Assert.True(service.SetPaused("downloads", true).Paused);
        await Stabilize(service);
        Assert.Empty(store.List<PlanView>("Plan"));

        Assert.False(service.SetPaused("Downloads", false).Paused);
        await Stabilize(service);
        Assert.Single(store.List<PlanView>("Plan"));
        Assert.Throws<RenameException>(() => service.SetPaused("unknown", true));
    }

    [Fact]
    public async Task MissingPresetsAreReported()
    {
        folder.Preset = "Nope";
        var service = Service();
        await Scan(service);
        Assert.Contains("Preset Nope does not exist", Assert.Single(service.List()).Error);
    }

    [Fact]
    public async Task TmdbPresetsNeedAnExplicitThreshold()
    {
        presets.Create(new("Movies", [Step("tmdb", new { nameTemplate = "{title} ({year})" })]));
        folder.Preset = "Movies";
        Write("downloads/Alien.1979.txt", "A");
        var service = Service();
        await Stabilize(service);
        Assert.Contains("needs explicit confirmation", Assert.Single(service.List()).Error);
        Assert.Empty(store.List<PlanView>("Plan"));
    }

    [Fact]
    public async Task TmdbSuggestionsAboveTheThresholdAreConfirmedAndOthersFail()
    {
        presets.Create(new("Movies", [Step("tmdb", new { nameTemplate = "{title} ({year})" })]));
        folder.Preset = "Movies";
        folder.TmdbMediaType = "movie";
        folder.TmdbAutoConfirm = 0.9;
        Write("downloads/Alien.1979.txt", "A");
        Write("downloads/Alienz.txt", "B");
        var service = Service();
        await Stabilize(service);

        var plan = Assert.Single(store.List<PlanView>("Plan"));
        Assert.Equal(["downloads/Alien.1979.txt"], plan.Pipeline.Files.Select(f => f.Path));
        Assert.Single(plan.Pipeline.Selections!);
        var problem = Assert.Single(Assert.Single(service.List()).Problems);
        Assert.Equal("downloads/Alienz.txt", problem.Path);
        Assert.Contains("below the 90% auto-confirm threshold", problem.Error);
        Assert.True(await renames.ProcessNext(default));
        Assert.Equal("downloads/Alien (1979).txt", store.List<PlanView>("Plan").Single().Files.Single().TargetPath);
    }

    [Fact]
    public async Task BusyTmdbPostponesTheBatchWithoutCountingFailures()
    {
        presets.Create(new("Movies", [Step("tmdb", new { })]));
        folder.Preset = "Movies";
        folder.TmdbMediaType = "movie";
        folder.TmdbAutoConfirm = 0.9;
        metadata.Busy = true;
        Write("downloads/Alien.1979.txt", "A");
        var service = Service();
        await Stabilize(service);
        var status = Assert.Single(service.List());
        Assert.Equal("TMDB is busy.", status.Error);
        Assert.Empty(status.Problems);
    }

    [Theory]
    [InlineData("", "downloads", "Upper", "names are 1–50")]
    [InlineData("a/b", "downloads", "Upper", "names are 1–50")]
    [InlineData("x", "../outside", "Upper", "root-relative Path")]
    [InlineData("x", "downloads", "", "needs the name of a Preset")]
    public void InvalidOptionsAreReported(string name, string path, string preset, string message)
        => Assert.Contains(message, WatchFolderOptions.Problem([new() { Name = name, Path = path, Preset = preset }]));

    [Fact]
    public void RangesThresholdsAndOverlapsAreChecked()
    {
        WatchFolderOptions Valid() => new() { Name = "a", Path = "in", Preset = "p" };
        Assert.Null(WatchFolderOptions.Problem([Valid()]));
        Assert.NotNull(WatchFolderOptions.Problem([Valid(), Valid()]));
        Assert.NotNull(WatchFolderOptions.Problem([Valid(), new() { Name = "b", Path = "IN", Preset = "p" }]));
        Assert.NotNull(WatchFolderOptions.Problem([new WatchFolderOptions { Name = "a", Path = "in", Preset = "p", Recursive = true },
            new() { Name = "b", Path = "in/sub", Preset = "p" }]));
        Assert.Null(WatchFolderOptions.Problem([Valid(), new() { Name = "b", Path = "in/sub", Preset = "p" }]));
        foreach (var invalid in new[]
        {
            new WatchFolderOptions { Name = "a", Path = "in", Preset = "p", IntervalSeconds = 5 },
            new WatchFolderOptions { Name = "a", Path = "in", Preset = "p", StableSeconds = -1 },
            new WatchFolderOptions { Name = "a", Path = "in", Preset = "p", MaxAttempts = 0 },
            new WatchFolderOptions { Name = "a", Path = "in", Preset = "p", TmdbMediaType = "show" },
            new WatchFolderOptions { Name = "a", Path = "in", Preset = "p", TmdbAutoConfirm = 0.9 },
            new WatchFolderOptions { Name = "a", Path = "in", Preset = "p", TmdbMediaType = "tv", TmdbAutoConfirm = 0.2 }
        })
            Assert.NotNull(WatchFolderOptions.Problem([invalid]));
    }

    [Fact]
    public void BackOffDoublesUpToADay()
    {
        Assert.Equal(Interval, WatchFolderService.BackOff(Interval, 1));
        Assert.Equal(Interval * 4, WatchFolderService.BackOff(Interval, 3));
        Assert.Equal(TimeSpan.FromHours(24), WatchFolderService.BackOff(TimeSpan.FromHours(1), 20));
    }

    private WatchFolderService Service()
    {
        var options = Options.Create(new NamerreOptions { WatchFolders = [folder] });
        var engine = TestPipeline.Create(metadata, fileSystem);
        return new WatchFolderService(options, store, renames, engine, presets, new PresetStore(store),
            new TmdbSuggester(metadata), new ServerFolderBrowser("media", workspace, store.IsOwnedEntry), fileSystem,
            NullLogger<WatchFolderService>.Instance, clock);
    }

    /// <summary>Observes the files, then scans again once they have been stable long enough.</summary>
    private async Task Stabilize(WatchFolderService service)
    {
        await Scan(service);
        await Scan(service, TimeSpan.FromSeconds(31));
    }

    private async Task Scan(WatchFolderService service, TimeSpan? advance = null)
    {
        clock.Advance(advance ?? Interval);
        await service.Scan(default);
    }

    private string Full(string path) => Path.Combine(workspace, "media", path.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string path, string content) => File.WriteAllText(Full(path), content);

    private static PluginInstance Step(string type, object options)
        => new(type, type, 1, true, JsonSerializer.SerializeToElement(options));

    public void Dispose()
    {
        store.Dispose();
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class FakeMetadata : ITmdbMetadata
    {
        private readonly Dictionary<string, TmdbMatch> confirmed = [];
        public bool Busy { get; set; }

        public Task<TmdbSearchResult> Search(TmdbSearchRequest request, CancellationToken cancellationToken)
        {
            if (Busy) throw new RenameException("TmdbBusy", "TMDB is busy.", 429);
            var alien = new TmdbMatch(348, "movie", "Alien", "1979", null, null, null);
            if (request.Id is null) return Task.FromResult(new TmdbSearchResult([alien]));
            var token = Guid.NewGuid().ToString("N");
            confirmed[token] = alien with { ConfirmationToken = token };
            return Task.FromResult(new TmdbSearchResult([confirmed[token]]));
        }

        public TmdbMatch Resolve(MetadataSelection selection) => confirmed[selection.ConfirmationToken];
    }
}
