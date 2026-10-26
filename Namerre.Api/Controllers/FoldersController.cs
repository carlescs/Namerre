using CMediator.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Namerre.Application.Folders;

namespace Namerre.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class FoldersController(ISender sender, ILogger<FoldersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FolderListing>> Get(
        CancellationToken cancellationToken,
        [FromQuery] string path = "")
    {
        try
        {
            return Ok(await sender.Send(new GetFolderQuery(path), cancellationToken));
        }
        catch (FolderBrowserException exception)
        {
            return FolderProblem(exception);
        }
    }

    [HttpGet("selection")]
    public async Task<ActionResult<IReadOnlyList<FolderListing>>> GetSelection(
        CancellationToken cancellationToken,
        [FromQuery] string path = "",
        [FromQuery] bool includeSubfolders = false,
        [FromQuery] bool includeHidden = true)
    {
        try
        {
            return Ok(await sender.Send(
                new GetFolderSelectionQuery(path, includeSubfolders, includeHidden),
                cancellationToken));
        }
        catch (FolderBrowserException exception)
        {
            return FolderProblem(exception);
        }
    }

    private ObjectResult FolderProblem(FolderBrowserException exception)
    {
        logger.LogWarning(exception, "Server folder browsing failed: {Error}", exception.Error);
        var statusCode = exception.Error switch
        {
            FolderBrowserError.InvalidPath => StatusCodes.Status400BadRequest,
            FolderBrowserError.Forbidden => StatusCodes.Status403Forbidden,
            FolderBrowserError.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return Problem(statusCode: statusCode, title: exception.Message);
    }
}
