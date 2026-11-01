using CMediator.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Previews;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;
using Namerre.Authentication;

namespace Namerre.Controllers;

[ApiController]
[TypeFilter(typeof(RenameExceptionFilter))]
public sealed class RenamingController(ISender sender) : ControllerBase
{
    [HttpGet("api/rename-plugins")]
    public async Task<IActionResult> Plugins(CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetRenamePluginsQuery(), cancellationToken));

    [HttpPost("api/rename-previews")]
    public async Task<IActionResult> Preview(PipelineRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new PreviewRenameQuery(request), cancellationToken));

    [HttpPost("api/rename-plans")]
    public async Task<IActionResult> Prepare(PipelineRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PrepareRenameCommand(request), cancellationToken);
        return Accepted(result.Location, result);
    }

    [HttpGet("api/rename-plans/{id}")]
    public async Task<IActionResult> Plan(string id, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetRenamePlanQuery(id), cancellationToken));

    [HttpPost("api/rename-plans/{id}/execute")]
    public async Task<IActionResult> Execute(string id, ExecuteRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ExecuteRenameCommand(id, request), cancellationToken);
        return Accepted(result.Location, result);
    }

    [HttpGet("api/rename-runs")]
    public async Task<IActionResult> Runs(CancellationToken cancellationToken, int page = 1, int pageSize = 20)
        => Ok(await sender.Send(new GetRenameRunsQuery(page, pageSize), cancellationToken));

    [HttpGet("api/rename-runs/{id}")]
    public async Task<IActionResult> Run(string id, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetRenameRunQuery(id), cancellationToken));

    [HttpGet("api/rename-runs/{id}/export")]
    public async Task<IActionResult> ExportRun(string id, CancellationToken cancellationToken, string format = "csv")
    {
        var export = await sender.Send(new ExportRenameRunQuery(id, format), cancellationToken);
        return File(export.Content, export.ContentType, export.FileName);
    }

    [Authorize(Policy = AuthenticationSetup.AdminPolicy)]
    [HttpDelete("api/rename-runs/{id}")]
    public async Task<IActionResult> DeleteRun(string id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteRenameRunCommand(id), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = AuthenticationSetup.AdminPolicy)]
    [HttpGet("api/rename-run-deletions/candidates")]
    public async Task<IActionResult> DeletableRuns(CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetDeletableRenameRunsQuery(), cancellationToken));

    [Authorize(Policy = AuthenticationSetup.AdminPolicy)]
    [HttpPost("api/rename-run-deletions")]
    public async Task<IActionResult> DeleteRuns(DeleteRunsRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new DeleteRenameRunsCommand(request), cancellationToken));

    [HttpPost("api/rename-runs/{id}/rollback-preview")]
    public async Task<IActionResult> RollbackPreview(string id, SelectionRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new PreviewRollbackQuery(id, request), cancellationToken));

    [HttpGet("api/rename-runs/{id}/rollback-preview/{operationId}")]
    public async Task<IActionResult> GetRollbackCheckProgress(string id, string operationId, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetRollbackCheckProgressQuery(id, operationId), cancellationToken));

    [HttpPost("api/rename-runs/{id}/rollback")]
    public async Task<IActionResult> Rollback(string id, SelectionRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RollbackRenameCommand(id, request), cancellationToken);
        return Accepted(result.Location, result);
    }

    [HttpPost("api/rename-runs/{id}/recovery-preview")]
    public async Task<IActionResult> RecoveryPreview(string id, CancellationToken cancellationToken)
        => Ok(await sender.Send(new PreviewRecoveryQuery(id), cancellationToken));

    [HttpPost("api/rename-runs/{id}/recover")]
    public async Task<IActionResult> Recover(string id, RecoveryRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RecoverRenameCommand(id, request), cancellationToken);
        return Accepted(result.Location, result);
    }

    [HttpPost("api/metadata/tmdb/search")]
    public async Task<IActionResult> SearchTmdb(TmdbSearchRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new SearchTmdbQuery(request), cancellationToken));

    [HttpPost("api/metadata/tmdb/suggestions")]
    public async Task<IActionResult> SuggestTmdb(TmdbSuggestionRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new SuggestTmdbMatchQuery(request), cancellationToken));
}

public sealed class RenameExceptionFilter(ILogger<RenameExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested)
            return;
        logger.LogWarning(context.Exception, "Renaming request failed.");
        var exception = context.Exception as RenameException;
        var problem = new ProblemDetails
        {
            Status = exception?.Status ?? 503,
            Title = exception?.Message ?? "Renaming is unavailable. No additional moves have been scheduled.",
            Extensions = { ["code"] = exception?.Code ?? "RenameUnavailable" }
        };
        context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        context.ExceptionHandled = true;
    }
}
