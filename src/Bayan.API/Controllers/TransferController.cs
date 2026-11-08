using System.Text.Json;
using Bayan.API.Extensions;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.Transfer;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

/// <summary>
/// Imports a file written by any export endpoint — queries, reports, dashboards, or a query
/// backup from before reports and dashboards could be exported. One endpoint, because one file
/// can hold all three kinds and they have to come in together, in order.
///
/// <para>Not gated by a single permission attribute: which transfer permissions are needed
/// depends on what the file holds, so <see cref="ImportTransferCommand"/> checks each kind it
/// finds before writing anything.</para>
/// </summary>
[ApiController]
[Route("api/admin/transfer")]
[Authorize]
public class TransferController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAppLocalizer _localizer;

    public TransferController(IMediator mediator, IAppLocalizer localizer)
    {
        _mediator = mediator;
        _localizer = localizer;
    }

    /// <summary>Never overwrites: a name clash comes in as a copy. Returns what came in and what did not.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(TransferFiles.MaxImportBytes + 1024)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = _localizer[MessageKeys.TransferNoFile] });
        if (file.Length > TransferFiles.MaxImportBytes)
            return BadRequest(new { message = _localizer[MessageKeys.TransferTooLarge] });

        TransferFile? parsed;
        try
        {
            await using var stream = file.OpenReadStream();
            parsed = await JsonSerializer.DeserializeAsync<TransferFile>(
                stream, TransferFiles.JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { message = _localizer[MessageKeys.TransferInvalidFile, ex.Message] });
        }

        if (parsed is null)
            return BadRequest(new { message = _localizer[MessageKeys.TransferNoContent] });

        return Ok(await _mediator.Send(new ImportTransferCommand(parsed), cancellationToken));
    }
}
