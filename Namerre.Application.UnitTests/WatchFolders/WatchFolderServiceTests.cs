using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Namerre.Application.Folders;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming.Presets;
using Namerre.Application.Renaming.Runs;
using Namerre.Application.WatchFolders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Namerre.Application.UnitTests.WatchFolders;

public sealed class WatchFolderServiceTests
{
    private const string Folder = "Downloads";
    private const string PresetId = "preset";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StableFor = TimeSpan.FromSeconds(30);
    private static readonly DateTime Modified = new(2026, 10, 31, 8, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryWatchedFileStore files = new();
    private readonly IRenameService renames = Substitute.For<IRenameService>();
    private readonly IPresetStore presetStore = Substitute.For<IPresetStore>();
    private readonly IFolderBrowser browser = Substitute.For<IFolderBrowser>();
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();
    private readonly ITmdbMetadata metadata = Substitute.For<ITmdbMetadata>();
    private readonly IWatchFolderDiagnostics diagnostics = Substitute.For<IWatchFolderDiagnostics>();
    private readonly List<FileEntry> listed = [new("show.txt", 10, Modified)];
    private WatchFolderSettings settings = new(Folder, "downloads", "Upper", Interval, StableFor, MaxAttempts: 2);

    public WatchFolderServiceTests()
    {
        fileSystem.Exists(Arg.Any<string>()).Returns(call => listed.Any(f => "downloads/" + f.Name == (string)call[0]));
        fileSystem.SamePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => (string)call[0] == (string)call[1]);
        fileSystem.Facts(Arg.Any<string>()).Returns(new FileFacts(10, Modified, Modified));
        browser.Browse("downloads", Arg.Any<CancellationToken>()).Returns(_ => new FolderListing("downloads", null, [], listed.ToArray()));
        renames.Prepare(Arg.Any<PipelineRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new JobAccepted(Guid.NewGuid().ToString("N"), "Queued", "/plan"));
        UsePreset(Step("case", new { mode = "upper" }));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(8, 1280)]
    public void BackOffDoublesWithEachFailure(int attempts, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), WatchFolderService.BackOff(Interval, attempts));

    [Fact]
    public void BackOffStopsAtADayAndNeverGoesBelowTheInterval()
    {
        Assert.Equal(WatchFolderService.MaxBackOff, WatchFolderService.BackOff(TimeSpan.FromHours(1), 20));
        Assert.Equal(Interval, WatchFolderService.BackOff(Interval, 0));
    }

    [Fact]
    public async Task ATouchedFileWaitsOutTheStabilityPeriodAgain()
    {
        var service = Service();
        await Scan(service, TimeSpan.Zero);
        await Scan(service, StableFor - Interval);
        // Written to again just before it would have been taken.
        listed[0] = new("show.txt", 11, Modified);
        await Scan(service, Interval);
        await Scan(service, StableFor - Interval);
        await renames.DidNotReceiveWithAnyArgs().Prepare(default!, default, default);

        await Scan(service, Interval);
        await renames.Received(1).Prepare(Arg.Any<PipelineRequest>(), "watch:Downloads", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileIsTakenOnceItsFingerprintHasBeenStableLongEnoughAndRecordedAsQueued()
    {
        var service = Service();
        await Scan(service, TimeSpan.Zero);
        await Scan(service, StableFor - Interval);
        await renames.DidNotReceiveWithAnyArgs().Prepare(default!, default, default);

        await Scan(service, Interval);

        await renames.Received(1).Prepare(
            Arg.Is<PipelineRequest>(r => r.Files.Count == 1 && r.Files[0].Id == "downloads/show.txt"), "watch:Downloads",
            Arg.Any<CancellationToken>());
        var row = Assert.Single(files.Rows);
        Assert.Equal((WatchFileState.Queued, 10L, 0), (row.State, row.Size, row.Attempts));
        Assert.NotNull(row.PlanId);
        Assert.Null(row.RunId);
        Assert.Equal(1, Assert.Single(service.List()).InFlight);
    }

    [Fact]
    public async Task FoldersWaitOutTheirIntervalAndPausedFoldersAreNotScanned()
    {
        var service = Service();
        await Scan(service, TimeSpan.Zero);
        await Scan(service, Interval / 2);
        await browser.Received(1).Browse("downloads", Arg.Any<CancellationToken>());

        files.SetPaused(Folder, true);
        await Scan(service, Interval);
        await browser.Received(1).Browse("downloads", Arg.Any<CancellationToken>());
        Assert.True(Assert.Single(service.List()).Paused);

        Assert.False(service.SetPaused(Folder, false).Paused);
        // Resuming scans on the next tick instead of waiting for the interval.
        await Scan(service, TimeSpan.FromSeconds(1));
        await browser.Received(2).Browse("downloads", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APlanThatFailsToPrepareFailsItsFilesAndWaitsOneInterval()
    {
        var service = Service();
        var plan = Guid.NewGuid().ToString("N");
        renames.Prepare(default!, default, default).ReturnsForAnyArgs(new JobAccepted(plan, "Queued", "/plan"));
        renames.GetPlan(plan, Arg.Any<CancellationToken>()).Returns(Plan(plan, "Failed", "Disk full"));
        await Stabilize(service);

        await Scan(service, Interval);

        var failed = Assert.Single(files.Rows);
        Assert.Equal((WatchFileState.Failed, 1, "Disk full"), (failed.State, failed.Attempts, failed.Error));
        Assert.Equal(time.GetUtcNow() + Interval, failed.NextAttemptAt);
        Assert.Equal(nameof(WatchFileState.Failed), Assert.Single(Assert.Single(service.List()).Problems).Status);
        diagnostics.Received().Files(Folder, WatchOutcome.Failed, 1);
    }

    [Fact]
    public async Task AFileThatCannotBePreviewedIsRetriedWithDoublingBackOffAndIgnoredAfterTheLastAttempt()
    {
        settings = settings with { MaxAttempts = 3 };
        var service = Service();
        fileSystem.Exists(Arg.Any<string>()).Returns(false);

        // First failure: retried one interval later.
        await Stabilize(service);
        var failed = Assert.Single(files.Rows);
        Assert.Equal((WatchFileState.Failed, 1), (failed.State, failed.Attempts));
        Assert.False(string.IsNullOrEmpty(failed.Error));
        Assert.Equal(time.GetUtcNow() + Interval, failed.NextAttemptAt);

        // Second failure: the wait doubles, and a scan before it is over leaves the file alone.
        await Scan(service, Interval);
        Assert.Equal((2, time.GetUtcNow() + Interval * 2), (files.Rows[0].Attempts, files.Rows[0].NextAttemptAt));
        await Scan(service, Interval);
        Assert.Equal(2, files.Rows[0].Attempts);

        // The third failure reaches MaxAttempts: ignored until the file changes.
        await Scan(service, Interval);
        var ignored = Assert.Single(files.Rows);
        Assert.Equal((WatchFileState.Ignored, 3, null), (ignored.State, ignored.Attempts, ignored.NextAttemptAt));
        diagnostics.Received().Files(Folder, WatchOutcome.Ignored, 1);
        Assert.Equal(nameof(WatchFileState.Ignored), Assert.Single(Assert.Single(service.List()).Problems).Status);

        var updatedAt = ignored.UpdatedAt;
        await Scan(service, TimeSpan.FromDays(2));
        await Scan(service, TimeSpan.FromDays(2));
        Assert.Equal(updatedAt, files.Rows[0].UpdatedAt);
        await renames.DidNotReceiveWithAnyArgs().Prepare(default!, default, default);
    }

    [Fact]
    public async Task AChangedFileStartsCountingAttemptsAgain()
    {
        var service = Service();
        files.Seed(new WatchedFile(Folder, "downloads/show.txt", 10, Modified.Ticks, WatchFileState.Ignored, 2, null, "old"));
        await Stabilize(service);
        await renames.DidNotReceiveWithAnyArgs().Prepare(default!, default, default);

        listed[0] = new("show.txt", 99, Modified);
        await Stabilize(service);

        await renames.Received(1).Prepare(Arg.Any<PipelineRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.Equal((WatchFileState.Queued, 99L, 0), (files.Rows[0].State, files.Rows[0].Size, files.Rows[0].Attempts));
    }

    [Fact]
    public void RetryForgetsOnlyFailedOrIgnoredFiles()
    {
        var service = Service();
        files.Seed(new WatchedFile(Folder, "downloads/a.txt", 1, 1, WatchFileState.Ignored, 2, null, "x"),
            new WatchedFile(Folder, "downloads/b.txt", 1, 1, WatchFileState.Done));

        service.Retry(Folder, "downloads/a.txt");

        Assert.Equal("downloads/b.txt", Assert.Single(files.Rows).Path);
        Assert.Equal(404, Assert.Throws<RenameException>(() => service.Retry(Folder, "downloads/b.txt")).Status);
        Assert.Equal(404, Assert.Throws<RenameException>(() => service.Retry("Other", "downloads/a.txt")).Status);
    }

    [Fact]
    public async Task AReadyPlanIsExecutedOnceAndACompletedRunRecordsTheNewName()
    {
        var service = Service();
        var plan = Guid.NewGuid().ToString("N");
        renames.Prepare(default!, default, default).ReturnsForAnyArgs(new JobAccepted(plan, "Queued", "/plan"));
        renames.GetPlan(plan, Arg.Any<CancellationToken>()).Returns(Plan(plan, "Ready"));
        renames.Execute(plan, Arg.Any<ExecuteRequest>(), Arg.Any<CancellationToken>()).Returns(new JobAccepted("run", "Queued", "/run"));
        await Stabilize(service);

        await Scan(service, Interval);

        await renames.Received(1).Execute(plan, Arg.Is<ExecuteRequest>(r => r.Version == 3 && r.OperationId == Guid.ParseExact(plan, "N").ToString()),
            Arg.Any<CancellationToken>());
        Assert.Equal("run", Assert.Single(files.Rows).RunId);

        renames.GetRun("run", Arg.Any<CancellationToken>()).Returns(Run("Running"));
        await Scan(service, Interval);
        await renames.Received(1).Execute(plan, Arg.Any<ExecuteRequest>(), Arg.Any<CancellationToken>());
        Assert.Equal(WatchFileState.Queued, Assert.Single(files.Rows).State);

        renames.GetRun("run", Arg.Any<CancellationToken>()).Returns(Run("Completed"));
        // The run renamed the file on disk.
        listed[0] = new("SHOW.txt", 10, Modified);
        await Scan(service, Interval);
        var done = Assert.Single(files.Rows);
        Assert.Equal(("downloads/SHOW.txt", WatchFileState.Done, "run"), (done.Path, done.State, done.RunId));
        Assert.Equal(1, Assert.Single(service.List()).Completed);
        diagnostics.Received().Files(Folder, WatchOutcome.Completed, 1);
    }

    [Theory]
    [InlineData("RecoveryRequired")]
    [InlineData("Recovered")]
    public async Task AnInterruptedRunFailsItsFiles(string status)
    {
        var service = Service();
        files.Seed(new WatchedFile(Folder, "downloads/show.txt", 10, Modified.Ticks, WatchFileState.Queued, PlanId: "plan", RunId: "run"));
        renames.GetRun("run", Arg.Any<CancellationToken>()).Returns(Run(status) with { Error = "Interrupted" });

        await Scan(service, TimeSpan.Zero);

        Assert.Equal((WatchFileState.Failed, "Interrupted"), (files.Rows[0].State, files.Rows[0].Error));
    }

    [Fact]
    public async Task ADeletedPlanFailsItsFiles()
    {
        var service = Service();
        files.Seed(new WatchedFile(Folder, "downloads/show.txt", 10, Modified.Ticks, WatchFileState.Queued, PlanId: "plan"));
        renames.GetPlan("plan", Arg.Any<CancellationToken>()).Throws(new RenameException("NotFound", "gone", 404));

        await Scan(service, TimeSpan.Zero);

        Assert.Equal("The prepared plan was deleted.", files.Rows[0].Error);
    }

    [Fact]
    public async Task NamesThePresetAlreadyProducesAreDoneWithoutARun()
    {
        var service = Service();
        listed[0] = new("SHOW.txt", 10, Modified);
        await Stabilize(service);

        await renames.DidNotReceiveWithAnyArgs().Prepare(default!, default, default);
        Assert.Equal(WatchFileState.Done, Assert.Single(files.Rows).State);
        diagnostics.Received().Files(Folder, WatchOutcome.Unchanged, 1);
    }

    [Fact]
    public async Task APresetThatDoesNotExistStopsTheFolderWithAnError()
    {
        var service = Service();
        presetStore.Find("Upper").Returns((PresetRecord?)null);

        await Stabilize(service);

        Assert.Contains("Preset Upper does not exist", Assert.Single(service.List()).Error);
        await renames.DidNotReceiveWithAnyArgs().Prepare(default!, default, default);
    }

    [Fact]
    public async Task ATmdbStepNeedsAnExplicitAutoConfirmThreshold()
    {
        var service = Service();
        UsePreset(TmdbStep());

        await Stabilize(service);

        Assert.Contains("TmdbAutoConfirm", Assert.Single(service.List()).Error);
        await renames.DidNotReceiveWithAnyArgs().Prepare(default!, default, default);
        await metadata.DidNotReceiveWithAnyArgs().Search(default!, default);
    }

    [Fact]
    public async Task ASuggestionBelowTheThresholdFailsTheFileWithoutPreparing()
    {
        settings = settings with { TmdbMediaType = "movie", TmdbAutoConfirm = 0.9 };
        var service = Service();
        UsePreset(TmdbStep());
        listed[0] = new("The.Movie.txt", 10, Modified);
        Suggests(new TmdbMatch(42, "movie", "The Movie", "2024", null, null, null));

        await Stabilize(service);

        await renames.DidNotReceiveWithAnyArgs().Prepare(default!, default, default);
        var row = Assert.Single(files.Rows);
        Assert.Equal(WatchFileState.Failed, row.State);
        Assert.Equal("The TMDB suggestion (The Movie, 88%) is below the 90% auto-confirm threshold. Rename it by hand.", row.Error);
    }

    [Fact]
    public async Task ASuggestionAtOrAboveTheThresholdIsConfirmedAndPrepared()
    {
        settings = settings with { TmdbMediaType = "movie", TmdbAutoConfirm = 0.85 };
        var service = Service();
        UsePreset(TmdbStep());
        listed[0] = new("The.Movie.txt", 10, Modified);
        var match = new TmdbMatch(42, "movie", "The Movie", "2024", null, null, null);
        Suggests(match);
        metadata.Resolve(Arg.Any<MetadataSelection>()).Returns(match with { ConfirmationToken = "token" });

        await Stabilize(service);

        Assert.Equal(WatchFileState.Queued, Assert.Single(files.Rows).State);
        await renames.Received(1).Prepare(
            Arg.Is<PipelineRequest>(r => r.Selections!.Count == 1 && r.Selections[0].ConfirmationToken == "token"
                && r.Selections[0].FileId == "downloads/The.Movie.txt"), "watch:Downloads", Arg.Any<CancellationToken>());
        Assert.Equal(WatchFileState.Queued, Assert.Single(files.Rows).State);
    }

    [Fact]
    public async Task ABusyServiceHoldsNothingAgainstTheFiles()
    {
        var service = Service();
        renames.Prepare(default!, default, default).ThrowsAsyncForAnyArgs(new RenameException("QueueFull", "The queue is full.", 429));

        await Stabilize(service);

        Assert.Empty(files.Rows);
        Assert.Equal("The queue is full.", Assert.Single(service.List()).Error);
    }

    [Fact]
    public async Task AFailingStoreOrFolderIsReportedAndRetriedOnTheNextInterval()
    {
        var service = Service();
        browser.Browse("downloads", Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("gone"));
        await Scan(service, TimeSpan.Zero);
        Assert.Contains("could not be read", Assert.Single(service.List()).Error);

        browser.Browse("downloads", Arg.Any<CancellationToken>()).Returns(new FolderListing("downloads", null, [], []));
        await Scan(service, Interval);
        Assert.Null(Assert.Single(service.List()).Error);
    }

    private WatchFolderService Service()
        => new(new Configuration(() => settings), files, renames, Pipeline(), new PresetService(presetStore, Pipeline()),
            presetStore, new TmdbSuggester(metadata), browser, fileSystem, diagnostics,
            NullLogger<WatchFolderService>.Instance, time);

    private PipelineEngine Pipeline() => TestPipeline.Create(metadata, fileSystem);

    /// <summary>Observes the files, then scans again once they have been stable long enough.</summary>
    private async Task Stabilize(WatchFolderService service)
    {
        await Scan(service, Interval);
        await Scan(service, StableFor);
    }

    private async Task Scan(WatchFolderService service, TimeSpan advance)
    {
        time.Advance(advance);
        await service.Scan(default);
    }

    private void UsePreset(params PluginInstance[] steps)
    {
        var record = new PresetRecord(PresetId, "Upper", steps, false, false, null, default, default);
        presetStore.Find("Upper").Returns(record);
        presetStore.Get(PresetId).Returns(record);
    }

    private void Suggests(TmdbMatch candidate)
        => metadata.Search(Arg.Any<TmdbSearchRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            ((TmdbSearchRequest)call[0]).Id is null
                ? new TmdbSearchResult([candidate])
                : new TmdbSearchResult([candidate with { ConfirmationToken = "token" }]));

    private static PluginInstance TmdbStep() => Step("tmdb", new { language = "en-US" });

    private static PluginInstance Step(string type, object options)
        => new(type, type, 1, true, JsonSerializer.SerializeToElement(options));

    private static PlanView Plan(string id, string status, string? issue = null)
        => new(id, 3, status, 0, new PipelineRequest([], []), [],
            issue is null ? [] : [new RenameIssue("Failed", issue)], default);

    private static RunView Run(string status)
        => new("run", "plan", null, "Rename", status,
            [new RunFile("downloads/show.txt", "downloads/show.txt", "downloads/SHOW.txt", "", "", "Done")], [], default);

    private sealed class Configuration(Func<WatchFolderSettings> settings) : IWatchFolderConfiguration
    {
        public IReadOnlyList<WatchFolderSettings> Folders => [settings()];
    }
}
