using CMediator.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Namerre.Application.Renaming.Presets;

namespace Namerre.Controllers;

/// <summary>Named pipeline presets kept on the server.</summary>
[ApiController]
[Route("api/rename-presets")]
[TypeFilter(typeof(RenameExceptionFilter))]
public sealed class RenamePresetsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await sender.Send(new ListRenamePresetsQuery(), cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetRenamePresetQuery(id), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(SavePresetRequest request, CancellationToken cancellationToken)
    {
        var preset = await sender.Send(new CreateRenamePresetCommand(request), cancellationToken);
        return Created($"/api/rename-presets/{preset.Id}", preset);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SavePresetRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new UpdateRenamePresetCommand(id, request), cancellationToken));

    [HttpPatch("{id}")]
    public async Task<IActionResult> Rename(string id, RenamePresetRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new RenameRenamePresetCommand(id, request), cancellationToken));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteRenamePresetCommand(id), cancellationToken);
        return NoContent();
    }
}
