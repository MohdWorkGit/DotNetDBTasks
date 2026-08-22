using Bayan.API.Authorization;
using Bayan.Application.Features.Reports.Commands;
using Bayan.Application.Features.Reports.Dtos;
using Bayan.Application.Features.Reports.Queries;
using Bayan.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

/// <summary>
/// Authoring side of reports: the definition, the Word template that lays it out, and who may
/// run it. Running one lives on <see cref="UserReportsController"/>.
/// </summary>
[ApiController]
[Route("api/admin/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    /// <summary>
    /// Templates are Word documents, not archives. The same ceiling the per-query template
    /// upload uses, so neither becomes the easier way to push a large blob into the database.
    /// </summary>
    private const int MaxTemplateBytes = 5 * 1024 * 1024;

    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission(Permissions.ReportsView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetReportsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.ReportsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetReportByIdQuery { Id = id }, cancellationToken));

    [HttpPost]
    [RequirePermission(Permissions.ReportsManage)]
    public async Task<IActionResult> Create(
        [FromBody] ReportInput input,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SaveReportCommand { Input = input }, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.ReportsManage)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] ReportInput input,
        CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new SaveReportCommand { Id = id, Input = input }, cancellationToken));

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.ReportsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteReportCommand { Id = id }, cancellationToken);
        return NoContent();
    }

    // ---------------------------------------------------------------- template

    /// <summary>
    /// Uploads the report's Word template and returns what it references, so the author is told
    /// about a mistyped dataset key straight away rather than discovering an empty section in a
    /// document days later.
    /// </summary>
    [HttpPost("{id:guid}/template")]
    [RequirePermission(Permissions.ReportsManage)]
    [RequestSizeLimit(MaxTemplateBytes)]
    public async Task<IActionResult> UploadTemplate(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });

        if (file.Length > MaxTemplateBytes)
            return BadRequest(new { message = "The template must be 5 MB or smaller." });

        if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "The template must be a .docx file." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);

        var inspection = await _mediator.Send(new SetReportTemplateCommand
        {
            ReportId = id,
            Content = ms.ToArray(),
            FileName = file.FileName
        }, cancellationToken);

        return Ok(inspection);
    }

    /// <summary>
    /// Downloads the stored template, or a generated starter carrying this report's real dataset
    /// keys when none is stored — so the download / edit in Word / upload loop works from the
    /// first click.
    /// </summary>
    [HttpGet("{id:guid}/template")]
    [RequirePermission(Permissions.ReportsManage)]
    public async Task<IActionResult> DownloadTemplate(Guid id, CancellationToken cancellationToken)
    {
        var (content, fileName) = await _mediator.Send(
            new GetReportTemplateQuery { ReportId = id }, cancellationToken);
        return File(content, DocxContentType, fileName);
    }

    /// <summary>The generated starter, even when a template is already stored.</summary>
    [HttpGet("{id:guid}/starter-template")]
    [RequirePermission(Permissions.ReportsManage)]
    public async Task<IActionResult> DownloadStarterTemplate(Guid id, CancellationToken cancellationToken)
    {
        var (content, fileName) = await _mediator.Send(
            new GetReportTemplateQuery { ReportId = id, Starter = true }, cancellationToken);
        return File(content, DocxContentType, fileName);
    }

    [HttpDelete("{id:guid}/template")]
    [RequirePermission(Permissions.ReportsManage)]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteReportTemplateCommand { ReportId = id }, cancellationToken);
        return NoContent();
    }

    // ---------------------------------------------------------------- access

    [HttpGet("{id:guid}/access")]
    [RequirePermission(Permissions.AccessManageReport)]
    public async Task<IActionResult> GetAccess(Guid id, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetReportAccessQuery { Id = id }, cancellationToken));

    [HttpPut("{id:guid}/access")]
    [RequirePermission(Permissions.AccessManageReport)]
    public async Task<IActionResult> SetAccess(
        Guid id,
        [FromBody] ReportAccessDto access,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetReportAccessCommand
        {
            ReportId = id,
            RoleIds = access.RoleIds,
            UserGroupIds = access.UserGroupIds,
            UserIds = access.UserIds
        }, cancellationToken);

        return Ok(result);
    }
}
