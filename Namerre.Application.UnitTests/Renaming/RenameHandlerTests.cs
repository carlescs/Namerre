using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Previews;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class RenameHandlerTests : IDisposable
{
    private readonly IRenameService service = Substitute.For<IRenameService>();
    private readonly CancellationTokenSource cancellation = new();
    private readonly JobAccepted accepted = new("job", "Queued", "/api/rename-runs/job");
    private static readonly PipelineRequest Pipeline = new([new FileInput("f1", "a.txt")], []);

    private CancellationToken Token => cancellation.Token;

    public void Dispose() => cancellation.Dispose();

    [Fact]
    public async Task PreviewForwardsPipeline()
    {
        var expected = new PreviewResult([], true);
        service.Preview(Pipeline, Token).Returns(expected);

        var result = await new PreviewRenameQueryHandler(service).Handle(new PreviewRenameQuery(Pipeline), Token);

        Assert.Same(expected, result);
        await service.Received(1).Preview(Pipeline, Token);
    }

    [Fact]
    public async Task PrepareForwardsPipeline()
    {
        service.Prepare(Pipeline, Token).Returns(accepted);

        var result = await new PrepareRenameCommandHandler(service).Handle(new PrepareRenameCommand(Pipeline), Token);

        Assert.Same(accepted, result);
        await service.Received(1).Prepare(Pipeline, Token);
    }

    [Fact]
    public async Task GetPlanForwardsId()
    {
        var expected = new PlanView("plan", 1, "Ready", 0, Pipeline, [], [], DateTimeOffset.UnixEpoch);
        service.GetPlan("plan", Token).Returns(expected);

        var result = await new GetRenamePlanQueryHandler(service).Handle(new GetRenamePlanQuery("plan"), Token);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ExecuteForwardsIdAndRequest()
    {
        var request = new ExecuteRequest(3, "op");
        service.Execute("plan", request, Token).Returns(accepted);

        var result = await new ExecuteRenameCommandHandler(service)
            .Handle(new ExecuteRenameCommand("plan", request), Token);

        Assert.Same(accepted, result);
        await service.Received(1).Execute("plan", request, Token);
    }

    [Fact]
    public async Task GetRunsForwardsPaging()
    {
        var expected = new RunPage([], 2, 10, false);
        service.GetRuns(2, 10, Token).Returns(expected);

        var result = await new GetRenameRunsQueryHandler(service).Handle(new GetRenameRunsQuery(2, 10), Token);

        Assert.Same(expected, result);
        await service.Received(1).GetRuns(2, 10, Token);
    }

    [Fact]
    public async Task GetRunForwardsId()
    {
        var expected = new RunView("run", "plan", null, "Rename", "Completed", [], [], DateTimeOffset.UnixEpoch);
        service.GetRun("run", Token).Returns(expected);

        var result = await new GetRenameRunQueryHandler(service).Handle(new GetRenameRunQuery("run"), Token);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task DeleteRunForwardsIdAndReturnsDeletedIds()
    {
        IReadOnlyList<string> deleted = ["run", "child"];
        service.DeleteRun("run", Token).Returns(deleted);

        var result = await new DeleteRenameRunCommandHandler(service).Handle(new DeleteRenameRunCommand("run"), Token);

        Assert.Same(deleted, result);
        await service.Received(1).DeleteRun("run", Token);
    }

    [Fact]
    public async Task RollbackPreviewForwardsSelection()
    {
        var selection = new SelectionRequest(["f1"], "op");
        var expected = new SelectionPreview([]);
        service.RollbackPreview("run", selection, Token).Returns(expected);

        var result = await new PreviewRollbackQueryHandler(service)
            .Handle(new PreviewRollbackQuery("run", selection), Token);

        Assert.Same(expected, result);
        await service.Received(1).RollbackPreview("run", selection, Token);
    }

    [Fact]
    public async Task RollbackForwardsSelection()
    {
        var selection = new SelectionRequest(["f1"], "op");
        service.Rollback("run", selection, Token).Returns(accepted);

        var result = await new RollbackRenameCommandHandler(service)
            .Handle(new RollbackRenameCommand("run", selection), Token);

        Assert.Same(accepted, result);
        await service.Received(1).Rollback("run", selection, Token);
    }

    [Fact]
    public async Task RecoveryPreviewForwardsId()
    {
        var expected = new RecoveryPreview("run", true, "restore", []);
        service.PreviewRecovery("run", Token).Returns(expected);

        var result = await new PreviewRecoveryQueryHandler(service).Handle(new PreviewRecoveryQuery("run"), Token);

        Assert.Same(expected, result);
        await service.Received(1).PreviewRecovery("run", Token);
    }

    [Fact]
    public async Task RecoverForwardsRequest()
    {
        var request = new RecoveryRequest("restore", "op");
        service.Recover("run", request, Token).Returns(accepted);

        var result = await new RecoverRenameCommandHandler(service)
            .Handle(new RecoverRenameCommand("run", request), Token);

        Assert.Same(accepted, result);
        await service.Received(1).Recover("run", request, Token);
    }

    [Fact]
    public async Task ServiceExceptionsPropagateUnchanged()
    {
        var failure = new RenameException("NotFound", "Missing.", 404);
        service.GetRun("missing", Token).Returns(Task.FromException<RunView>(failure));

        var thrown = await Assert.ThrowsAsync<RenameException>(() =>
            new GetRenameRunQueryHandler(service).Handle(new GetRenameRunQuery("missing"), Token));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task SearchTmdbForwardsRequestToMetadata()
    {
        var metadata = Substitute.For<ITmdbMetadata>();
        var request = new TmdbSearchRequest("movie", "Alien");
        var expected = new TmdbSearchResult([new TmdbMatch(348, "movie", "Alien", "1979", null, null, null)]);
        metadata.Search(request, Token).Returns(expected);

        var result = await new SearchTmdbQueryHandler(metadata).Handle(new SearchTmdbQuery(request), Token);

        Assert.Same(expected, result);
        await metadata.Received(1).Search(request, Token);
    }

    [Fact]
    public async Task PluginsQueryReturnsEngineCatalog()
    {
        var result = await new GetRenamePluginsQueryHandler().Handle(new GetRenamePluginsQuery(), Token);

        Assert.Same(PipelineEngine.Catalog, result);
        Assert.Equal(["literal", "regex", "case", "tmdb"], result.Select(p => p.TypeId));
    }
}
