using CMediator.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Namerre.Application.WatchFolders;

namespace Namerre.Controllers;

/// <summary>Status of the configured watch folders, with pause, resume and retry.</summary>
[ApiController]
[Route("api/watch-folders")]
[TypeFilter(typeof(RenameExceptionFilter))]
public sealed class WatchFoldersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetWatchFoldersQuery(), cancellationToken));

    [HttpPost("{name}/pause")]
    public async Task<IActionResult> Pause(string name, CancellationToken cancellationToken)
        => Ok(await sender.Send(new SetWatchFolderPausedCommand(name, true), cancellationToken));

    [HttpPost("{name}/resume")]
    public async Task<IActionResult> Resume(string name, CancellationToken cancellationToken)
        => Ok(await sender.Send(new SetWatchFolderPausedCommand(name, false), cancellationToken));

    [HttpPost("{name}/retry")]
    public async Task<IActionResult> Retry(string name, RetryWatchedFileRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new RetryWatchedFileCommand(name, request), cancellationToken));
}
