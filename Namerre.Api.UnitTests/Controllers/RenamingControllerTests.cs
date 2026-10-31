using System.Text.Json;
using CMediator.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Previews;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;
using Namerre.Controllers;
using NSubstitute;
using Xunit;

namespace Namerre.Api.UnitTests.Controllers;

public sealed class RenamingControllerTests : IDisposable
{
    private readonly ISender sender = Substitute.For<ISender>();
    private readonly CancellationTokenSource cancellation = new();
    private readonly RenamingController controller;
    private readonly JobAccepted accepted = new("job", "Queued", "/api/rename-runs/job");
    private static readonly PipelineRequest Pipeline = new([new FileInput("f1", "a.txt")],
        [new PluginInstance("p", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }))]);

    public RenamingControllerTests() => controller = new RenamingController(sender);

    private CancellationToken Token => cancellation.Token;

    public void Dispose() => cancellation.Dispose();

    [Fact]
    public async Task PluginsReturnsCatalog()
    {
        IReadOnlyList<PluginDescriptor> catalog = [new("case", 1, "Case change", "Change case", new { }, [])];
        sender.Send(Arg.Any<GetRenamePluginsQuery>(), Token).Returns(catalog);

        var result = await controller.Plugins(Token);

        Assert.Same(catalog, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task PreviewSendsPipeline()
    {
        var preview = new PreviewResult([], true);
        sender.Send(Arg.Is<PreviewRenameQuery>(q => q.Pipeline == Pipeline), Token).Returns(preview);

        var result = await controller.Preview(Pipeline, Token);

        Assert.Same(preview, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task PrepareReturnsAcceptedWithJobLocation()
    {
        sender.Send(Arg.Is<PrepareRenameCommand>(c => c.Pipeline == Pipeline), Token).Returns(accepted);

        var result = Assert.IsType<AcceptedResult>(await controller.Prepare(Pipeline, Token));

        Assert.Equal(accepted.Location, result.Location);
        Assert.Same(accepted, result.Value);
    }

    [Fact]
    public async Task PlanSendsId()
    {
        var plan = new PlanView("plan", 1, "Ready", 0, Pipeline, [], [], DateTimeOffset.UnixEpoch);
        sender.Send(Arg.Is<GetRenamePlanQuery>(q => q.Id == "plan"), Token).Returns(plan);

        var result = await controller.Plan("plan", Token);

        Assert.Same(plan, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task ExecuteReturnsAcceptedWithJobLocation()
    {
        var request = new ExecuteRequest(2, "op");
        sender.Send(Arg.Is<ExecuteRenameCommand>(c => c.Id == "plan" && c.Request == request), Token)
            .Returns(accepted);

        var result = Assert.IsType<AcceptedResult>(await controller.Execute("plan", request, Token));

        Assert.Equal(accepted.Location, result.Location);
        Assert.Same(accepted, result.Value);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(3, 5)]
    public async Task RunsSendsPaging(int page, int pageSize)
    {
        var runs = new RunPage([], page, pageSize, false);
        sender.Send(Arg.Is<GetRenameRunsQuery>(q => q.Page == page && q.PageSize == pageSize), Token).Returns(runs);

        var result = await controller.Runs(Token, page, pageSize);

        Assert.Same(runs, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task RunSendsId()
    {
        var run = new RunView("run", "plan", null, "Rename", "Completed", [], [], DateTimeOffset.UnixEpoch);
        sender.Send(Arg.Is<GetRenameRunQuery>(q => q.Id == "run"), Token).Returns(run);

        var result = await controller.Run("run", Token);

        Assert.Same(run, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task DeleteRunReturnsNoContent()
    {
        sender.Send(Arg.Any<DeleteRenameRunCommand>(), Token).Returns(Task.FromResult<IReadOnlyList<string>>(["run"]));

        Assert.IsType<NoContentResult>(await controller.DeleteRun("run", Token));
        await sender.Received(1).Send(Arg.Is<DeleteRenameRunCommand>(c => c.Id == "run"), Token);
    }

    [Fact]
    public async Task DeletableRunsReturnsCandidates()
    {
        var candidates = new DeletableRuns(["run", "other"], false);
        sender.Send(Arg.Any<GetDeletableRenameRunsQuery>(), Token).Returns(candidates);

        Assert.Same(candidates, Assert.IsType<OkObjectResult>(await controller.DeletableRuns(Token)).Value);
    }

    [Fact]
    public async Task DeleteRunsReturnsDeletedIds()
    {
        var request = new DeleteRunsRequest(["run", "other"]);
        IReadOnlyList<string> deleted = ["run", "child", "other"];
        sender.Send(Arg.Is<DeleteRenameRunsCommand>(c => c.Request == request), Token).Returns(deleted);

        var result = await controller.DeleteRuns(request, Token);

        Assert.Same(deleted, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task RollbackPreviewSendsSelection()
    {
        var selection = new SelectionRequest(["f1"], "op");
        var preview = new SelectionPreview([]);
        sender.Send(Arg.Is<PreviewRollbackQuery>(q => q.Id == "run" && q.Selection == selection), Token)
            .Returns(preview);

        var result = await controller.RollbackPreview("run", selection, Token);

        Assert.Same(preview, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task RollbackCheckProgressSendsOperation()
    {
        var progress = new RollbackCheckProgress(1, 2);
        sender.Send(Arg.Is<GetRollbackCheckProgressQuery>(q => q.Id == "run" && q.OperationId == "op"), Token)
            .Returns(progress);

        var result = await controller.GetRollbackCheckProgress("run", "op", Token);

        Assert.Same(progress, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task RollbackReturnsAcceptedWithJobLocation()
    {
        var selection = new SelectionRequest(["f1"], "op");
        sender.Send(Arg.Is<RollbackRenameCommand>(c => c.Id == "run" && c.Selection == selection), Token)
            .Returns(accepted);

        var result = Assert.IsType<AcceptedResult>(await controller.Rollback("run", selection, Token));

        Assert.Equal(accepted.Location, result.Location);
    }

    [Fact]
    public async Task RecoveryPreviewSendsId()
    {
        var preview = new RecoveryPreview("run", true, "restore", []);
        sender.Send(Arg.Is<PreviewRecoveryQuery>(q => q.Id == "run"), Token).Returns(preview);

        var result = await controller.RecoveryPreview("run", Token);

        Assert.Same(preview, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task RecoverReturnsAcceptedWithJobLocation()
    {
        var request = new RecoveryRequest("restore", "op");
        sender.Send(Arg.Is<RecoverRenameCommand>(c => c.Id == "run" && c.Request == request), Token)
            .Returns(accepted);

        var result = Assert.IsType<AcceptedResult>(await controller.Recover("run", request, Token));

        Assert.Equal(accepted.Location, result.Location);
        Assert.Same(accepted, result.Value);
    }

    [Fact]
    public async Task SearchTmdbSendsRequest()
    {
        var request = new TmdbSearchRequest("movie", "Alien");
        var matches = new TmdbSearchResult([]);
        sender.Send(Arg.Is<SearchTmdbQuery>(q => q.Request == request), Token).Returns(matches);

        var result = await controller.SearchTmdb(request, Token);

        Assert.Same(matches, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task SuggestTmdbSendsRequest()
    {
        var request = new TmdbSuggestionRequest("movie", "file", "step", "Alien.1979.mkv", new string('a', 64));
        var suggestion = new TmdbSuggestion(new("Alien", "1979"), [], null, 0, "No TMDB matches.");
        sender.Send(Arg.Is<SuggestTmdbMatchQuery>(q => q.Request == request), Token).Returns(suggestion);

        var result = await controller.SuggestTmdb(request, Token);

        Assert.Same(suggestion, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task SenderFailuresPropagateToTheExceptionFilter()
    {
        sender.Send(Arg.Any<GetRenameRunQuery>(), Token)
            .Returns(Task.FromException<RunView>(new RenameException("NotFound", "Missing.", 404)));

        await Assert.ThrowsAsync<RenameException>(() => controller.Run("missing", Token));
    }
}
