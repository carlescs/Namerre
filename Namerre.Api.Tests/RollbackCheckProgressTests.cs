using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Rollback;
using Namerre.Infrastructure.Renaming;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class RollbackCheckProgressTests : IDisposable
{
    private readonly string workspace = Path.Combine(Directory.GetCurrentDirectory(), ".rollback-progress-tests", Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(workspace, "media");
    private string Data => Path.Combine(workspace, "data");
    public RollbackCheckProgressTests() => Directory.CreateDirectory(Root);

    [Fact]
    public async Task ReportsPerFileProgressWhileTheCheckHoldsTheGateAndForgetsItOnCancellation()
    {
        File.WriteAllText(Path.Combine(Root, "alpha.txt"), "alpha");
        File.WriteAllText(Path.Combine(Root, "bravo.txt"), "bravo");
        var bounded = new BoundedFileSystem(Root, workspace);
        var filesystem = new PausingFileSystem(bounded);
        using var store = new RenameStore(bounded, Data, workspace);
        var service = new RenameService(store, filesystem, new PipelineEngine(new NoMetadata(), filesystem),
            NullLogger<RenameService>.Instance);
        var pipeline = new PipelineRequest([new("a", "alpha.txt"), new("b", "bravo.txt")],
            [new("r", "literal", 1, true, JsonSerializer.SerializeToElement(new { search = "a", replacement = "z" }))]);
        var prepared = await service.Prepare(pipeline, default);
        await service.ProcessNext(default);
        var job = await service.Execute(prepared.Id, new(1, Guid.NewGuid().ToString()), default);
        await service.ProcessNext(default);
        Assert.Equal("Completed", (await service.GetRun(job.Id, default)).Status);

        var operation = Guid.NewGuid().ToString();
        filesystem.PausePrefix = "brzvo";
        using var cancellation = new CancellationTokenSource();
        var check = service.RollbackPreview(job.Id, new(["a", "b"], operation), cancellation.Token);
        await filesystem.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new RollbackCheckProgress(1, 2), await service.GetRollbackCheckProgress(job.Id, operation, default));
        using (var repeatCancellation = new CancellationTokenSource())
        {
            var repeat = service.RollbackPreview(job.Id, new(["a", "b"], operation), repeatCancellation.Token);
            await repeatCancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repeat);
        }
        Assert.Equal(new RollbackCheckProgress(1, 2), await service.GetRollbackCheckProgress(job.Id, operation, default));

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.Equal(new RollbackCheckProgress(0, 0), await service.GetRollbackCheckProgress(job.Id, operation, default));
    }

    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        var parent = Path.GetDirectoryName(workspace)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }

    // Holds content hashing of matching paths until the caller's token is cancelled.
    private sealed class PausingFileSystem(IRenameFileSystem inner) : IRenameFileSystem
    {
        public string? PausePrefix { get; set; }
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string RootBinding => inner.RootBinding;
        public void Validate(string path) => inner.Validate(path);
        public bool Exists(string path) => inner.Exists(path);
        public string ActualPath(string path) => inner.ActualPath(path);
        public bool SamePath(string first, string second) => inner.SamePath(first, second);
        public (string Hash, long Size) Stamp(string path) => inner.Stamp(path);
        public void CheckSameDevice(string source, string target) => inner.CheckSameDevice(source, target);
        public void Move(string source, string destination) => inner.Move(source, destination);
        public IReadOnlyList<string> MissingDirectories(string path) => inner.MissingDirectories(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void DeleteEmptyDirectory(string path) => inner.DeleteEmptyDirectory(path);

        public async Task<(string Hash, long Size)> Hash(string path, CancellationToken cancellationToken)
        {
            if (PausePrefix is not null && path.StartsWith(PausePrefix, StringComparison.OrdinalIgnoreCase))
            {
                Paused.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return await inner.Hash(path, cancellationToken);
        }
    }

    private sealed class NoMetadata : ITmdbMetadata
    {
        public Task<TmdbSearchResult> Search(TmdbSearchRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public TmdbMatch Resolve(MetadataSelection selection) => throw new NotSupportedException();
    }
}
