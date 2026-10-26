using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Previews;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Folders;
using Namerre.Infrastructure.Renaming;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class RenamingWorkflowTests : IDisposable
{
    private readonly string workspace = Path.Combine(Directory.GetCurrentDirectory(), ".rename-tests", Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(workspace, "media");
    private string Data => Path.Combine(workspace, "data");

    public RenamingWorkflowTests() => Directory.CreateDirectory(Root);

    [Fact]
    public void StartupAppliesMigrationsBeforeRequestsWithoutTheBackgroundWorker()
    {
        for (var startup = 0; startup < 2; startup++)
        {
            using var factory = FactoryWithoutWorker();
            _ = factory.Server;

            using var context = new RenameDbContext(new DbContextOptionsBuilder<RenameDbContext>()
                .UseSqlite($"Data Source={Path.Combine(Data, "renaming.sqlite3")};Pooling=False").Options);
            Assert.NotEmpty(context.Database.GetAppliedMigrations());
            Assert.Empty(context.Database.GetPendingMigrations());
        }
    }

    [Fact]
    public void MigrationFailurePreventsStartup()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "renaming.sqlite3"), "Not a SQLite database.");
        using var factory = FactoryWithoutWorker();

        var exception = Assert.Throws<RenameException>(() => { _ = factory.Server; });

        Assert.Equal("StorageUnavailable", exception.Code);
        Assert.Contains("opening and migrating the SQLite database", exception.Message);
        Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageLockFailurePreservesCauseAndAllowsRetry(bool mediaRoot)
    {
        Directory.CreateDirectory(Data);
        var lockPath = mediaRoot ? Path.Combine(Root, ".namerre-owner") : Path.Combine(Data, "owner.lock");
        using var harness = Harness();
        using (var competingLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var exception = Assert.Throws<RenameException>(() => harness.Store.Initialize());

            Assert.Equal("StorageUnavailable", exception.Code);
            Assert.Contains(mediaRoot ? "media root lock" : "data directory lock", exception.Message);
            Assert.IsAssignableFrom<IOException>(exception.InnerException);
            if (mediaRoot)
            {
                using var releasedDataLock = new FileStream(Path.Combine(Data, "owner.lock"),
                    FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }

        harness.Store.Initialize();
    }

    [Fact]
    public async Task HttpWorkflowUsesMediatorImmutablePlanDurableHistoryAndIdempotentConfirmation()
    {
        Write("alpha.txt", "original");
        string runId;
        using (var factory = Factory())
        using (var client = await AuthWebApplicationFactory.AuthenticatedClient(factory))
        {
            var catalog = await client.GetFromJsonAsync<PluginDescriptor[]>("/api/rename-plugins");
            Assert.Equal(["literal", "regex", "case", "tmdb"], catalog!.Select(p => p.TypeId));
            var request = Request(["alpha.txt"], [Plugin("case", "case", new { mode = "upper" })]);
            using var response = await client.PostAsJsonAsync("/api/rename-previews", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var preview = await response.Content.ReadFromJsonAsync<PreviewResult>();
            Assert.Equal("ALPHA.txt", Assert.Single(preview!.Files).TargetPath);
            Assert.True(File.Exists(Path.Combine(Root, "alpha.txt")));
            using var preparing = await client.PostAsJsonAsync("/api/rename-plans", request);
            Assert.Equal(HttpStatusCode.Accepted, preparing.StatusCode);
            var accepted = await preparing.Content.ReadFromJsonAsync<JobAccepted>();
            Assert.Equal(accepted!.Location, preparing.Headers.Location!.OriginalString);
            var plan = await PollPlan(client, accepted.Id);
            Assert.Equal("Ready", plan.Status);
            Assert.Equal(64, Assert.Single(plan.Files).Hash.Length);
            using var stale = await client.PostAsJsonAsync(accepted.Location + "/execute", new ExecuteRequest(2, Guid.NewGuid().ToString()));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var execute = new ExecuteRequest(1, Guid.NewGuid().ToString());
            using var confirmation = await client.PostAsJsonAsync(accepted.Location + "/execute", execute);
            Assert.Equal(HttpStatusCode.Accepted, confirmation.StatusCode);
            var job = await confirmation.Content.ReadFromJsonAsync<JobAccepted>();
            runId = job!.Id;
            using var repeated = await client.PostAsJsonAsync(accepted.Location + "/execute", execute);
            Assert.Equal(runId, (await repeated.Content.ReadFromJsonAsync<JobAccepted>())!.Id);
            var run = await PollRun(client, runId);
            Assert.Equal("Completed", run.Status);
            Assert.Equal(2, run.Journal.Count);
            Assert.All(run.Journal, entry => Assert.Equal("Completed", entry.Status));
            Assert.Equal("original", Read("ALPHA.txt"));
            Assert.False(File.Exists(Path.Combine(Root, "alpha.txt")) && !OperatingSystem.IsWindows());
            var listing = await client.GetFromJsonAsync<Namerre.Application.Folders.FolderListing>("/api/folders");
            Assert.DoesNotContain(listing!.Files, f => f.Name.StartsWith(".namerre-", StringComparison.Ordinal));
        }
        using (var factory = Factory())
        using (var client = await AuthWebApplicationFactory.AuthenticatedClient(factory))
        {
            var run = await client.GetFromJsonAsync<RunView>("/api/rename-runs/" + runId);
            Assert.Equal("Completed", run!.Status);
            var page = await client.GetFromJsonAsync<RunPage>("/api/rename-runs?page=1&pageSize=1");
            Assert.Equal(runId, Assert.Single(page!.Items).Id);
            var selection = new SelectionRequest(["0"], Guid.NewGuid().ToString());
            using var check = await client.PostAsJsonAsync($"/api/rename-runs/{runId}/rollback-preview", selection);
            Assert.True(Assert.Single((await check.Content.ReadFromJsonAsync<SelectionPreview>())!.Files).Safe);
            var finished = await client.GetFromJsonAsync<RollbackCheckProgress>(
                $"/api/rename-runs/{runId}/rollback-preview/{selection.OperationId}");
            Assert.Equal(new RollbackCheckProgress(0, 0), finished);
            using var invalidProgress = await client.GetAsync($"/api/rename-runs/{runId}/rollback-preview/not-a-uuid");
            Assert.Equal(HttpStatusCode.BadRequest, invalidProgress.StatusCode);
            using var rollback = await client.PostAsJsonAsync($"/api/rename-runs/{runId}/rollback", selection);
            Assert.Equal(HttpStatusCode.Accepted, rollback.StatusCode);
            var job = await rollback.Content.ReadFromJsonAsync<JobAccepted>();
            Assert.Equal("Completed", (await PollRun(client, job!.Id)).Status);
            Assert.Equal("original", Read("alpha.txt"));
            var parent = await client.GetFromJsonAsync<RunView>("/api/rename-runs/" + runId);
            Assert.Equal("RolledBack", Assert.Single(parent!.Files).Status);
            using var deleted = await client.DeleteAsync("/api/rename-runs/" + runId);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            using var missing = await client.GetAsync("/api/rename-runs/" + job.Id);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<RunPage>("/api/rename-runs"))!.Items);
            Assert.Equal("original", Read("alpha.txt"));
        }
    }

    [Fact]
    public async Task DeletingSettledRunRemovesRecordsRollbacksAndPlanButNeverFiles()
    {
        Write("alpha.txt", "A");
        Write("bravo.txt", "B");
        using var harness = Harness();
        var plan = await Prepare(harness, UpperRequest(["alpha.txt", "bravo.txt"]));
        var rename = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var rollback = await harness.Service.Rollback(rename.Id, new(["0"], Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var other = await Prepare(harness, Request(["alpha.txt"], [Plugin("r", "literal", new { search = "alpha", replacement = "zulu" })]));

        Assert.Equal([rename.Id, rollback.Id], await harness.Service.DeleteRun(rename.Id, default));

        foreach (var id in new[] { rename.Id, rollback.Id })
            Assert.Equal("NotFound", (await Assert.ThrowsAsync<RenameException>(() => harness.Service.GetRun(id, default))).Code);
        Assert.Equal("NotFound", (await Assert.ThrowsAsync<RenameException>(() => harness.Service.GetPlan(plan.Id, default))).Code);
        Assert.Equal("Ready", (await harness.Service.GetPlan(other.Id, default)).Status);
        Assert.Empty((await harness.Service.GetRuns(1, 20, default)).Items);
        Assert.Equal("A", Read("alpha.txt"));
        Assert.Equal("B", Read("BRAVO.txt"));
        Assert.Equal("NotFound", (await Assert.ThrowsAsync<RenameException>(() => harness.Service.DeleteRun(rename.Id, default))).Code);
    }

    [Fact]
    public async Task DurableChangesNotifyClientsIncludingRollbackParentsAndDeletions()
    {
        Write("alpha.txt", "A");
        var notifier = new RecordingNotifier();
        using var harness = Harness(notifier: notifier);
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        Assert.Equal(new RenameChange("Plan", plan.Id, "Preparing"), notifier.Changes[0]);
        Assert.Equal(new RenameChange("Plan", plan.Id, "Ready"), notifier.Changes[^1]);

        notifier.Changes.Clear();
        var rename = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        Assert.Equal(new RenameChange("Run", rename.Id, "Queued"), notifier.Changes[0]);
        Assert.Contains(new RenameChange("Run", rename.Id, "Running"), notifier.Changes);
        Assert.Equal(new RenameChange("Run", rename.Id, "Completed"), notifier.Changes[^1]);

        notifier.Changes.Clear();
        var rollback = await harness.Service.Rollback(rename.Id, new(["0"], Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        // The parent's file statuses changed too, so its watchers must refresh.
        Assert.Equal([new RenameChange("Run", rollback.Id, "Completed"), new RenameChange("Run", rename.Id, "Completed")],
            notifier.Changes.TakeLast(2));

        notifier.Changes.Clear();
        await harness.Service.DeleteRun(rename.Id, default);
        Assert.Equal([new RenameChange("Run", rename.Id, "Deleted"), new RenameChange("Run", rollback.Id, "Deleted")],
            notifier.Changes);
    }

    [Fact]
    public async Task NotificationFailureNeverFailsTheJob()
    {
        Write("alpha.txt", "A");
        using var harness = Harness(notifier: new ThrowingNotifier());
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        var rename = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);

        Assert.Equal("Completed", (await harness.Service.GetRun(rename.Id, default)).Status);
        Assert.Equal("A", Read("ALPHA.txt"));
    }

    [Fact]
    public async Task RunDetailsLinkOriginalAndRollbackRuns()
    {
        Write("alpha.txt", "A");
        Write("bravo.txt", "B");
        using var harness = Harness();
        var plan = await Prepare(harness, UpperRequest(["alpha.txt", "bravo.txt"]));
        var rename = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        Assert.Empty((await harness.Service.GetRun(rename.Id, default)).RollbackRuns!);
        var first = await harness.Service.Rollback(rename.Id, new(["0"], Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var second = await harness.Service.Rollback(rename.Id, new(["1"], Guid.NewGuid().ToString()), default);

        var original = await harness.Service.GetRun(rename.Id, default);
        Assert.Equal([(first.Id, "Rollback", "Completed"), (second.Id, "Rollback", "Queued")],
            original.RollbackRuns!.Select(r => (r.Id, r.Kind, r.Status)));
        var rollback = await harness.Service.GetRun(first.Id, default);
        Assert.Equal(rename.Id, rollback.ParentRunId);
        Assert.Empty(rollback.RollbackRuns!);
        Assert.All((await harness.Service.GetRuns(1, 10, default)).Items, r => Assert.Null(r.RollbackRuns));
    }

    [Fact]
    public async Task DeletingRollbackKeepsOriginalRunAndSharedPlan()
    {
        Write("alpha.txt", "A");
        using var harness = Harness();
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        var rename = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var rollback = await harness.Service.Rollback(rename.Id, new(["0"], Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);

        Assert.Equal([rollback.Id], await harness.Service.DeleteRun(rollback.Id, default));

        Assert.Equal("RolledBack", Assert.Single((await harness.Service.GetRun(rename.Id, default)).Files).Status);
        Assert.Equal("Ready", (await harness.Service.GetPlan(plan.Id, default)).Status);
    }

    [Fact]
    public async Task RunsAwaitingRecoveryOrWithPendingRollbacksCannotBeDeleted()
    {
        Write("alpha.txt", "before");
        using var harness = Harness();
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        Write("alpha.txt", "after!");
        var failed = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(failed.Id, default)).Status);
        var blocked = await Assert.ThrowsAsync<RenameException>(() => harness.Service.DeleteRun(failed.Id, default));
        Assert.Equal(("RunNotSettled", 409), (blocked.Code, blocked.Status));
        Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(failed.Id, default)).Status);
    }

    [Fact]
    public async Task RunWithQueuedRollbackCannotBeDeleted()
    {
        Write("alpha.txt", "A");
        using var harness = Harness();
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        var rename = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var rollback = await harness.Service.Rollback(rename.Id, new(["0"], Guid.NewGuid().ToString()), default);
        foreach (var id in new[] { rename.Id, rollback.Id })
            Assert.Equal("RunNotSettled", (await Assert.ThrowsAsync<RenameException>(() => harness.Service.DeleteRun(id, default))).Code);
        await harness.Service.ProcessNext(default);
        Assert.Equal("Completed", (await harness.Service.GetRun(rollback.Id, default)).Status);
        Assert.Equal("A", Read("alpha.txt"));
    }

    [Fact]
    public async Task BulkDeletionRemovesEverySelectedRunAndItsRollbacksAndSkipsMissingOnes()
    {
        Write("alpha.txt", "A");
        Write("bravo.txt", "B");
        var notifier = new RecordingNotifier();
        using var harness = Harness(notifier: notifier);
        var first = await harness.Service.Execute((await Prepare(harness, UpperRequest(["alpha.txt"]))).Id,
            new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var rollback = await harness.Service.Rollback(first.Id, new(["0"], Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var second = await harness.Service.Execute((await Prepare(harness, UpperRequest(["bravo.txt"]))).Id,
            new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        notifier.Changes.Clear();

        // The rollback is selected as well as its parent; it must only be deleted once.
        var deleted = await harness.Service.DeleteRuns(new([first.Id, rollback.Id, second.Id, "gone"]), default);

        Assert.Equal([first.Id, rollback.Id, second.Id], deleted);
        Assert.Equal(deleted.Select(id => new RenameChange("Run", id, "Deleted")), notifier.Changes);
        Assert.Empty((await harness.Service.GetRuns(1, 20, default)).Items);
        Assert.Equal("A", Read("alpha.txt"));
        Assert.Equal("B", Read("BRAVO.txt"));
    }

    [Fact]
    public async Task BulkDeletionIsRefusedAsAWholeWhenAnySelectedRunIsUnsettled()
    {
        Write("alpha.txt", "A");
        Write("bravo.txt", "before");
        using var harness = Harness();
        var settled = await harness.Service.Execute((await Prepare(harness, UpperRequest(["alpha.txt"]))).Id,
            new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var plan = await Prepare(harness, UpperRequest(["bravo.txt"]));
        Write("bravo.txt", "after!");
        var failed = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);

        var blocked = await Assert.ThrowsAsync<RenameException>(
            () => harness.Service.DeleteRuns(new([settled.Id, failed.Id]), default));

        Assert.Equal(("RunNotSettled", 409), (blocked.Code, blocked.Status));
        Assert.Equal("Completed", (await harness.Service.GetRun(settled.Id, default)).Status);
        Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(failed.Id, default)).Status);
    }

    [Fact]
    public async Task DeletionCandidatesListOnlyDeletableRunsNewestFirst()
    {
        Write("alpha.txt", "A");
        Write("charlie.txt", "C");
        using var harness = Harness();
        var settled = await harness.Service.Execute((await Prepare(harness, UpperRequest(["alpha.txt"]))).Id,
            new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var rollback = await harness.Service.Rollback(settled.Id, new(["0"], Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        // A completed rename whose rollback is still queued cannot be deleted, so neither is a candidate.
        var pending = await harness.Service.Execute((await Prepare(harness, UpperRequest(["charlie.txt"]))).Id,
            new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        await harness.Service.Rollback(pending.Id, new(["0"], Guid.NewGuid().ToString()), default);

        var candidates = await harness.Service.GetDeletableRuns(default);
        Assert.Equal([rollback.Id, settled.Id], candidates.Ids);
        Assert.False(candidates.HasMore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RenameService.MaxRunsPerDeletion + 1)]
    public async Task BulkDeletionRequiresBetweenOneAndTheMaximumRuns(int count)
    {
        using var harness = Harness();
        var ids = Enumerable.Range(0, count).Select(i => "run-" + i).ToArray();
        Assert.Equal("InvalidSelection",
            (await Assert.ThrowsAsync<RenameException>(() => harness.Service.DeleteRuns(new(ids), default))).Code);
    }

    [Fact]
    public async Task BulkDeletionRejectsDuplicateRuns()
    {
        using var harness = Harness();
        Assert.Equal("InvalidSelection",
            (await Assert.ThrowsAsync<RenameException>(() => harness.Service.DeleteRuns(new(["a", "a"]), default))).Code);
    }

    [Fact]
    public async Task SwapsAndCyclesUseNoOverwriteStagingAndRequireCompleteRollbackDependencyGroup()
    {
        Write("a.txt", "content-a");
        Write("b.txt", "content-b");
        Write("c.txt", "content-c");
        using var harness = Harness();
        var pipeline = Request(["a.txt", "b.txt", "c.txt"],
        [
            Plugin("1", "literal", new { search = "a", replacement = "temporary" }),
            Plugin("2", "literal", new { search = "c", replacement = "a" }),
            Plugin("3", "literal", new { search = "b", replacement = "c" }),
            Plugin("4", "literal", new { search = "temporary", replacement = "b" })
        ]);
        var plan = await Prepare(harness, pipeline);
        var job = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var run = await harness.Service.GetRun(job.Id, default);
        Assert.Equal("Completed", run.Status);
        Assert.Equal("content-c", Read("a.txt"));
        Assert.Equal("content-a", Read("b.txt"));
        Assert.Equal("content-b", Read("c.txt"));
        var partial = await harness.Service.RollbackPreview(run.Id, new(["0"], ""), default);
        Assert.False(Assert.Single(partial.Files).Safe);
        var all = new SelectionRequest(["0", "1", "2"], Guid.NewGuid().ToString());
        Assert.All((await harness.Service.RollbackPreview(run.Id, all, default)).Files, f => Assert.True(f.Safe));
        var reverse = await harness.Service.Rollback(run.Id, all, default);
        await harness.Service.ProcessNext(default);
        Assert.Equal("Completed", (await harness.Service.GetRun(reverse.Id, default)).Status);
        Assert.Equal("content-a", Read("a.txt"));
        Assert.Equal("content-b", Read("b.txt"));
        Assert.Equal("content-c", Read("c.txt"));
        var duplicate = await harness.Service.Rollback(run.Id, all, default);
        Assert.Equal(reverse.Id, duplicate.Id);
    }

    [Fact]
    public async Task ChangedHashBlocksThatRollbackButSafeSelectedFileStillRuns()
    {
        Write("alpha.txt", "AAAA");
        Write("bravo.txt", "BBBB");
        using var harness = Harness();
        var plan = await Prepare(harness, Request(["alpha.txt", "bravo.txt"],
            [Plugin("r", "literal", new { search = "a", replacement = "z" })]));
        var execute = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var run = await harness.Service.GetRun(execute.Id, default);
        var timestamp = File.GetLastWriteTimeUtc(Path.Combine(Root, run.Files[0].TargetPath));
        Write(run.Files[0].TargetPath, "ZZZZ");
        File.SetLastWriteTimeUtc(Path.Combine(Root, run.Files[0].TargetPath), timestamp);
        var selection = new SelectionRequest(["0", "1"], Guid.NewGuid().ToString());
        var preview = await harness.Service.RollbackPreview(run.Id, selection, default);
        Assert.False(preview.Files[0].Safe);
        Assert.Equal("HashMismatch", Assert.Single(preview.Files[0].Issues).Code);
        Assert.True(preview.Files[1].Safe);
        var rollback = await harness.Service.Rollback(run.Id, selection, default);
        await harness.Service.ProcessNext(default);
        var reversed = await harness.Service.GetRun(rollback.Id, default);
        Assert.Single(reversed.Files);
        Assert.Equal("BBBB", Read("bravo.txt"));
        Assert.Equal("ZZZZ", Read(run.Files[0].TargetPath));
    }

    [Fact]
    public async Task SourceChangesAfterPreparationStopWithoutOverwriteAndRequireRecovery()
    {
        Write("alpha.txt", "before");
        using var harness = Harness();
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        Write("alpha.txt", "after!");
        var job = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var run = await harness.Service.GetRun(job.Id, default);
        Assert.Equal("RecoveryRequired", run.Status);
        Assert.Empty(run.Journal);
        var evidence = await harness.Service.PreviewRecovery(run.Id, default);
        Assert.False(evidence.CanRecover);
        Assert.Contains(Assert.Single(evidence.Files).Issues, i => i.Code == "ContentMissing");
        Assert.Equal("after!", Read("alpha.txt"));
    }

    [Fact]
    public async Task QuickCheckPlansSkipHashingButStillRenameAndRollBack()
    {
        Write("alpha.txt", "A");
        using var harness = Harness();
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]) with { VerifyContent = false });
        var file = Assert.Single(plan.Files);
        Assert.StartsWith(BoundedFileSystem.StampPrefix, file.Hash);
        Assert.Equal(1, file.Size);
        var rename = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        Assert.Equal("Completed", (await harness.Service.GetRun(rename.Id, default)).Status);
        Assert.Equal("A", Read("ALPHA.txt"));
        var rollback = await harness.Service.Rollback(rename.Id, new(["0"], Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        Assert.Equal("Completed", (await harness.Service.GetRun(rollback.Id, default)).Status);
        Assert.Equal("A", Read("alpha.txt"));
    }

    [Theory]
    [InlineData("after, and longer", false)]
    [InlineData("BEFORE", true)]
    public async Task QuickCheckPlansStopWhenEitherSizeOrTimeChanges(string replacement, bool timeChanges)
    {
        // Each case changes exactly one stamp component so both are proven to be checked.
        Write("alpha.txt", "before");
        var prepared = File.GetLastWriteTimeUtc(Full("alpha.txt"));
        using var harness = Harness();
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]) with { VerifyContent = false });
        Write("alpha.txt", replacement);
        Touch("alpha.txt", timeChanges ? prepared.AddMinutes(1) : prepared);
        var job = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var run = await harness.Service.GetRun(job.Id, default);
        Assert.Equal("RecoveryRequired", run.Status);
        Assert.Equal("HashMismatch", Assert.Single(run.Files).ErrorCode);
        Assert.Empty(run.Journal);
        Assert.Equal(replacement, Read("alpha.txt"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public Task EverySwapMoveFailureBoundarySurvivesRestartAndExplicitRecovery(int failedMove, bool after)
        => SwapFailureSurvivesRestartAndRecovers(failedMove, after, verifyContent: true);

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public Task QuickCheckSwapFailureRecoversFromPersistedStampsAfterRestart(int failedMove, bool after)
        => SwapFailureSurvivesRestartAndRecovers(failedMove, after, verifyContent: false);

    private async Task SwapFailureSurvivesRestartAndRecovers(int failedMove, bool after, bool verifyContent)
    {
        Write("a.txt", "A");
        Write("b.txt", "B");
        // Equal sizes; distinct times keep the quick-check stamps of the two files unique.
        Touch("a.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Touch("b.txt", new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc));
        string id;
        using (var harness = Harness(moveFailure: failedMove, failAfter: after))
        {
            var plan = await Prepare(harness, Request(["a.txt", "b.txt"],
            [
                Plugin("1", "literal", new { search = "a", replacement = "temp" }),
                Plugin("2", "literal", new { search = "b", replacement = "a" }),
                Plugin("3", "literal", new { search = "temp", replacement = "b" })
            ]) with { VerifyContent = verifyContent });
            Assert.All(plan.Files, f => Assert.Equal(!verifyContent, f.Hash.StartsWith(BoundedFileSystem.StampPrefix, StringComparison.Ordinal)));
            id = (await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default)).Id;
            await harness.Service.ProcessNext(default);
            Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(id, default)).Status);
        }
        using (var harness = Harness())
        {
            var evidence = await harness.Service.PreviewRecovery(id, default);
            Assert.True(evidence.CanRecover, JsonSerializer.Serialize(evidence));
            Assert.All(evidence.Files, f => Assert.NotNull(f.LocatedPath));
            var operation = new RecoveryRequest("RestoreOriginal", Guid.NewGuid().ToString());
            await harness.Service.Recover(id, operation, default);
            await harness.Service.ProcessNext(default);
            Assert.Equal("Recovered", (await harness.Service.GetRun(id, default)).Status);
            Assert.Equal("A", Read("a.txt"));
            Assert.Equal("B", Read("b.txt"));
            Assert.DoesNotContain(Directory.GetFiles(Root), p => Path.GetFileName(p).StartsWith(".namerre-stage-", StringComparison.Ordinal));
            Assert.Equal(id, (await harness.Service.Recover(id, operation, default)).Id);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalCommitFailureBeforeOrAfterPhysicalMoveLeavesRecoverableEvidence(bool after)
    {
        Write("alpha.txt", "A");
        var runSaves = 0;
        var fired = false;
        using var harness = Harness(boundary: name =>
        {
            if (name.StartsWith("Save:Run:Running:", StringComparison.Ordinal) && name.EndsWith(after ? ":After" : ":Before", StringComparison.Ordinal)
                && ++runSaves == 3 && !fired)
            {
                fired = true;
                throw new IOException("Injected durable write failure.");
            }
        });
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        var id = (await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default)).Id;
        await harness.Service.ProcessNext(default);
        Assert.True(fired);
        Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(id, default)).Status);
        Assert.True((await harness.Service.PreviewRecovery(id, default)).CanRecover);
        await harness.Service.Recover(id, new("RestoreOriginal", Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        Assert.Equal("Recovered", (await harness.Service.GetRun(id, default)).Status);
        Assert.Equal("A", Read("alpha.txt"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
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
    [InlineData(15)]
    [InlineData(16)]
    public async Task EveryRunJournalAndFinalCommitBoundaryCanBeExplicitlyRecovered(int boundaryNumber)
    {
        Write("alpha.txt", "content");
        var boundaries = 0;
        var fired = false;
        string id;
        using (var harness = Harness(boundary: name =>
        {
            if ((name.StartsWith("Save:Run:Running:", StringComparison.Ordinal) || name.StartsWith("Finalize:", StringComparison.Ordinal))
                && ++boundaries == boundaryNumber && !fired)
            {
                fired = true;
                throw new IOException("Injected write boundary.");
            }
        }))
        {
            var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
            id = (await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default)).Id;
            await harness.Service.ProcessNext(default);
            Assert.True(fired);
            Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(id, default)).Status);
        }
        using (var harness = Harness())
        {
            Assert.True((await harness.Service.PreviewRecovery(id, default)).CanRecover);
            await harness.Service.Recover(id, new("RestoreOriginal", Guid.NewGuid().ToString()), default);
            await harness.Service.ProcessNext(default);
            Assert.Equal("Recovered", (await harness.Service.GetRun(id, default)).Status);
            Assert.Equal("content", Read("alpha.txt"));
            Assert.Equal("alpha.txt", new BoundedFileSystem(Root, workspace).ActualPath("alpha.txt"));
        }
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public async Task RecoveryInterruptionRetainsEveryStageAndCanBeExplicitlyRecoveredAgain(int failAt, bool after)
    {
        Write("alpha.txt", "A");
        string id;
        using (var harness = Harness(moveFailure: 1, failAfter: true))
        {
            var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
            id = (await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default)).Id;
            await harness.Service.ProcessNext(default);
            await harness.Service.Recover(id, new("RestoreOriginal", Guid.NewGuid().ToString()), default);
            harness.Fault!.FailureAt = failAt;
            harness.Fault.FailAfter = after;
            await harness.Service.ProcessNext(default);
            Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(id, default)).Status);
        }
        using (var harness = Harness())
        {
            var evidence = await harness.Service.PreviewRecovery(id, default);
            Assert.True(evidence.CanRecover, JsonSerializer.Serialize(evidence));
            await harness.Service.Recover(id, new("RestoreOriginal", Guid.NewGuid().ToString()), default);
            await harness.Service.ProcessNext(default);
            Assert.Equal("Recovered", (await harness.Service.GetRun(id, default)).Status);
            Assert.Equal("A", Read("alpha.txt"));
        }
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public async Task InterruptedRollbackRecoversToItsOwnPreOperationLocation(int failAt, bool after)
    {
        Write("alpha.txt", "A");
        using var harness = Harness(moveFailure: failAt, failAfter: after);
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        var rename = await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        var rollback = await harness.Service.Rollback(rename.Id, new(["0"], Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(rollback.Id, default)).Status);
        Assert.True((await harness.Service.PreviewRecovery(rollback.Id, default)).CanRecover);
        await harness.Service.Recover(rollback.Id, new("RestoreOriginal", Guid.NewGuid().ToString()), default);
        await harness.Service.ProcessNext(default);
        Assert.Equal("Recovered", (await harness.Service.GetRun(rollback.Id, default)).Status);
        Assert.Equal("ALPHA.txt", new BoundedFileSystem(Root, workspace).ActualPath("ALPHA.txt"));
        Assert.Equal("Completed", Assert.Single((await harness.Service.GetRun(rename.Id, default)).Files).Status);
        var preview = await harness.Service.RollbackPreview(rename.Id, new(["0"], ""), default);
        Assert.True(Assert.Single(preview.Files).Safe);
    }

    [Fact]
    public async Task QuickCheckTreatsDifferentContentWithEqualStampsAsAmbiguous()
    {
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Write("a.txt", "aaaa");
        Write("b.txt", "bbbb");
        Touch("a.txt", stamp);
        Touch("b.txt", stamp);
        using var harness = Harness(moveFailure: 2, failAfter: true);
        var plan = await Prepare(harness, UpperRequest(["a.txt", "b.txt"]) with { VerifyContent = false });
        var id = (await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default)).Id;
        await harness.Service.ProcessNext(default);
        var run = await harness.Service.GetRun(id, default);
        Write(run.Files[0].SourcePath, "cccc");
        Touch(run.Files[0].SourcePath, stamp);
        var evidence = await harness.Service.PreviewRecovery(id, default);
        Assert.False(evidence.CanRecover);
        Assert.Contains(evidence.Files[0].Issues, issue => issue.Code == "AmbiguousContent");
        await Assert.ThrowsAsync<RenameException>(() => harness.Service.Recover(id,
            new("RestoreOriginal", Guid.NewGuid().ToString()), default));
        Assert.Equal("cccc", Read(run.Files[0].SourcePath));
        Assert.Single(Directory.GetFiles(Root), p => Path.GetFileName(p).StartsWith(".namerre-stage-", StringComparison.Ordinal)
            && File.ReadAllText(p) == "aaaa");
    }

    [Fact]
    public async Task IdenticalCopiesAreAmbiguousAndNeverAutomaticallyRecovered()
    {
        Write("a.txt", "same");
        Write("b.txt", "same");
        using var harness = Harness(moveFailure: 2, failAfter: true);
        var plan = await Prepare(harness, UpperRequest(["a.txt", "b.txt"]));
        var id = (await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default)).Id;
        await harness.Service.ProcessNext(default);
        var run = await harness.Service.GetRun(id, default);
        Write(run.Files[0].SourcePath, "same");
        var evidence = await harness.Service.PreviewRecovery(id, default);
        Assert.False(evidence.CanRecover);
        Assert.Contains(evidence.Files[0].Issues, issue => issue.Code == "AmbiguousContent");
        await Assert.ThrowsAsync<RenameException>(() => harness.Service.Recover(id,
            new("RestoreOriginal", Guid.NewGuid().ToString()), default));
    }

    [Fact]
    public async Task QueuedMutationOnRestartRequiresExplicitRecoveryInsteadOfAutomaticResume()
    {
        Write("alpha.txt", "original");
        string id;
        using (var harness = Harness())
        {
            var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
            id = (await harness.Service.Execute(plan.Id, new(1, Guid.NewGuid().ToString()), default)).Id;
        }
        using (var harness = Harness())
        {
            Assert.Equal("RecoveryRequired", (await harness.Service.GetRun(id, default)).Status);
            Assert.False(await harness.Service.ProcessNext(default));
            Assert.True((await harness.Service.PreviewRecovery(id, default)).CanRecover);
            Assert.Equal("original", Read("alpha.txt"));
        }
    }

    [Fact]
    public async Task RejectsExistingTargetsAndDuplicateTargetsAndLeavesPreviewPure()
    {
        Write("a.txt", "A");
        Write("b.txt", "B");
        using var harness = Harness();
        var preview = await harness.Service.Preview(Request(["a.txt"],
            [Plugin("r", "literal", new { search = "a", replacement = "b" })]), default);
        Assert.False(preview.CanPrepare);
        Assert.Contains(Assert.Single(preview.Files).Issues, i => i.Code == "TargetExists");
        var duplicate = await harness.Service.Preview(Request(["a.txt", "b.txt"],
            [Plugin("r", "regex", new { pattern = ".", replacement = "x" })]), default);
        Assert.All(duplicate.Files, f => Assert.Contains(f.Issues, i => i.Code == "DuplicateTarget"));
        Assert.Equal(["a.txt", "b.txt"], Directory.GetFiles(Root).Select(p => Path.GetFileName(p)!).Order().ToArray());
    }

    [Fact]
    public void ExclusiveOwnershipAndRootBindingAreEnforced()
    {
        using (var first = Harness())
        {
            first.Store.Initialize();
            using var second = Harness();
            Assert.Equal("StorageUnavailable", Assert.Throws<RenameException>(() => second.Store.Initialize()).Code);
        }
        var otherRoot = Path.Combine(workspace, "different-root");
        Directory.CreateDirectory(otherRoot);
        using var changed = new RenameStore(new BoundedFileSystem(otherRoot, workspace), Data, workspace);
        Assert.Equal("RootChanged", Assert.Throws<RenameException>(() => changed.Initialize()).Code);
    }

    [Fact]
    public void DatabaseUsesRealEfCoreMigrationAndConcurrencyTokens()
    {
        using var harness = Harness();
        harness.Store.Initialize();
        using var context = new RenameDbContext(new DbContextOptionsBuilder<RenameDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Data, "renaming.sqlite3")};Pooling=False").Options);
        var migrations = context.Database.GetMigrations().ToArray();
        Assert.NotEmpty(migrations);
        Assert.Equal(migrations, context.Database.GetAppliedMigrations().ToArray());
        Assert.True(context.Model.FindEntityType(typeof(RenameDocument))!.FindProperty("Revision")!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(DurableRenameJob))!.FindProperty("Revision")!.IsConcurrencyToken);
    }

    [Fact]
    public async Task DurableQueueEnforcesExactCapacityAndEfConcurrencyRejectsLostUpdates()
    {
        Write("alpha.txt", "A");
        using var harness = Harness();
        for (var i = 0; i < 32; i++) await harness.Service.Prepare(UpperRequest(["alpha.txt"]), default);
        Assert.Equal("QueueFull", (await Assert.ThrowsAsync<RenameException>(
            () => harness.Service.Prepare(UpperRequest(["alpha.txt"]), default))).Code);
        var options = new DbContextOptionsBuilder<RenameDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Data, "renaming.sqlite3")};Pooling=False").Options;
        using var first = new RenameDbContext(options);
        using var second = new RenameDbContext(options);
        var one = first.Jobs.First();
        var two = second.Jobs.Single(j => j.Id == one.Id);
        one.Status = "Running";
        one.Revision++;
        first.SaveChanges();
        two.Status = "Completed";
        two.Revision++;
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptanceCommitFailureRetriesDoNotDuplicateMutationJobs(bool after)
    {
        Write("alpha.txt", "A");
        var fired = false;
        using var harness = Harness(boundary: name =>
        {
            if (name == (after ? "Enqueue:Run:After" : "Enqueue:Run:Before") && !fired)
            {
                fired = true;
                throw new IOException("Injected acceptance failure.");
            }
        });
        var plan = await Prepare(harness, UpperRequest(["alpha.txt"]));
        var request = new ExecuteRequest(1, Guid.NewGuid().ToString());
        await Assert.ThrowsAsync<IOException>(() => harness.Service.Execute(plan.Id, request, default));
        var job = await harness.Service.Execute(plan.Id, request, default);
        Assert.Single((await harness.Service.GetRuns(1, 100, default)).Items);
        await harness.Service.ProcessNext(default);
        Assert.Equal("Completed", (await harness.Service.GetRun(job.Id, default)).Status);
        Assert.Equal("A", Read("ALPHA.txt"));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("file:stream")]
    [InlineData("a/CON.txt")]
    [InlineData("a/../file.txt")]
    [InlineData("a/file.")]
    [InlineData("a/file ")]
    public void BoundsPathNamesAndAncestors(string path)
    {
        var filesystem = new BoundedFileSystem(Root, workspace);
        Assert.Throws<RenameException>(() => filesystem.Validate(path));
    }

    [Fact]
    public void RejectsLinkedSourceDestinationAndConfiguredRootAncestor()
    {
        var outside = Path.Combine(workspace, "outside");
        Directory.CreateDirectory(outside);
        try { Directory.CreateSymbolicLink(Path.Combine(Root, "linked"), outside); }
        catch (Exception e) when (e is UnauthorizedAccessException or PlatformNotSupportedException or IOException) { return; }
        var filesystem = new BoundedFileSystem(Root, workspace);
        Assert.Equal("LinkForbidden", Assert.Throws<RenameException>(() => filesystem.Validate("linked/file.txt")).Code);
        var linkedRoot = new BoundedFileSystem(Path.Combine(Root, "linked"), workspace);
        Assert.Equal("LinkForbidden", Assert.Throws<RenameException>(() => _ = linkedRoot.Root).Code);
    }

    [Fact]
    public async Task ReadOnlyPreviewDoesNotRequireStorageOrTmdbCredentials()
    {
        Write("alpha.txt", "A");
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:RootPath"] = Root, ["Namerre:DataPath"] = ""
            })));
        using var client = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        using var preview = await client.PostAsJsonAsync("/api/rename-previews", UpperRequest(["alpha.txt"]));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var prepare = await client.PostAsJsonAsync("/api/rename-plans", UpperRequest(["alpha.txt"]));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, prepare.StatusCode);
        Assert.DoesNotContain(Root, await prepare.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(Data));
    }

    private WebApplicationFactory<Program> Factory() => new AuthWebApplicationFactory().WithWebHostBuilder(
        builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Namerre:RootPath"] = Root, ["Namerre:DataPath"] = Data })));
    private WebApplicationFactory<Program> FactoryWithoutWorker() => Factory().WithWebHostBuilder(
        builder => builder.ConfigureServices(services =>
        {
            var worker = services.Single(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType == typeof(RenameWorker));
            services.Remove(worker);
        }));
    private string Full(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
    private void Touch(string path, DateTime lastWriteUtc) => File.SetLastWriteTimeUtc(Full(path), lastWriteUtc);
    private void Write(string path, string content)
    {
        var full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
    private string Read(string path) => File.ReadAllText(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));
    private static PluginInstance Plugin(string id, string type, object options)
        => new(id, type, 1, true, JsonSerializer.SerializeToElement(options));
    private static PipelineRequest Request(string[] paths, IReadOnlyList<PluginInstance> plugins)
        => new(paths.Select((path, i) => new FileInput(i.ToString(), path)).ToArray(), plugins);
    private static PipelineRequest UpperRequest(string[] paths) => Request(paths, [Plugin("upper", "case", new { mode = "upper" })]);
    private TestHarness Harness(int moveFailure = 0, bool failAfter = false, Action<string>? boundary = null,
        IRenameNotifier? notifier = null)
        => new(Root, Data, workspace, moveFailure, failAfter, boundary, notifier);
    private static async Task<PlanView> Prepare(TestHarness harness, PipelineRequest request)
    {
        var accepted = await harness.Service.Prepare(request, default);
        Assert.True(await harness.Service.ProcessNext(default));
        var plan = await harness.Service.GetPlan(accepted.Id, default);
        Assert.Equal("Ready", plan.Status);
        return plan;
    }
    private static async Task<PlanView> PollPlan(HttpClient client, string id)
    {
        for (var i = 0; i < 100; i++)
        {
            var plan = (await client.GetFromJsonAsync<PlanView>("/api/rename-plans/" + id))!;
            if (plan.Status != "Preparing") return plan;
            await Task.Delay(50);
        }
        throw new TimeoutException("Preparation did not complete.");
    }
    private static async Task<RunView> PollRun(HttpClient client, string id)
    {
        for (var i = 0; i < 100; i++)
        {
            var run = (await client.GetFromJsonAsync<RunView>("/api/rename-runs/" + id))!;
            if (run.Status is not ("Queued" or "Running" or "RecoveryQueued" or "Recovering")) return run;
            await Task.Delay(50);
        }
        throw new TimeoutException("Run did not complete.");
    }

    private sealed class TestHarness : IDisposable
    {
        public RenameStore Store { get; }
        public RenameService Service { get; }
        public FaultFileSystem? Fault { get; }
        public TestHarness(string root, string data, string workspace, int moveFailure, bool failAfter, Action<string>? boundary,
            IRenameNotifier? notifier)
        {
            var filesystem = new BoundedFileSystem(root, workspace);
            Store = new RenameStore(filesystem, data, workspace, boundary);
            var metadata = new NoMetadata();
            Fault = moveFailure > 0 ? new FaultFileSystem(filesystem, moveFailure, failAfter) : null;
            IRenameFileSystem mutation = Fault is null ? filesystem : Fault;
            Service = new RenameService(Store, mutation, new PipelineEngine(metadata, mutation), NullLogger<RenameService>.Instance,
                notifier);
        }
        public void Dispose() => Store.Dispose();
    }
    private sealed class RecordingNotifier : IRenameNotifier
    {
        public List<RenameChange> Changes { get; } = [];
        public void Changed(RenameChange change) => Changes.Add(change);
    }
    private sealed class ThrowingNotifier : IRenameNotifier
    {
        public void Changed(RenameChange change) => throw new InvalidOperationException("Hub unavailable.");
    }
    private sealed class NoMetadata : ITmdbMetadata
    {
        public TmdbMatch Resolve(MetadataSelection selection) => throw new NotSupportedException();
        public Task<TmdbSearchResult> Search(TmdbSearchRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
    private sealed class FaultFileSystem(IRenameFileSystem inner, int failureAt, bool after) : IRenameFileSystem
    {
        private int moves;
        public int FailureAt { get; set; } = failureAt;
        public bool FailAfter { get; set; } = after;
        public string RootBinding => inner.RootBinding;
        public void Validate(string path) => inner.Validate(path);
        public bool Exists(string path) => inner.Exists(path);
        public string ActualPath(string path) => inner.ActualPath(path);
        public bool SamePath(string first, string second) => inner.SamePath(first, second);
        public Task<(string Hash, long Size)> Hash(string path, CancellationToken cancellationToken) => inner.Hash(path, cancellationToken);
        public (string Hash, long Size) Stamp(string path) => inner.Stamp(path);
        public void CheckSameDevice(string source, string target) => inner.CheckSameDevice(source, target);
        public IReadOnlyList<string> MissingDirectories(string path) => inner.MissingDirectories(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public bool IsEmptyDirectory(string path) => inner.IsEmptyDirectory(path);
        public bool DeleteEmptyDirectory(string path) => inner.DeleteEmptyDirectory(path);
        public void Move(string source, string destination)
        {
            var fail = ++moves == FailureAt;
            if (fail && !FailAfter) throw new IOException("Injected before move.");
            inner.Move(source, destination);
            if (fail && FailAfter) throw new IOException("Injected after move.");
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        var parent = Path.GetDirectoryName(workspace)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }
}
