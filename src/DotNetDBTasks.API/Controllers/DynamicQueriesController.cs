using System.Text.Json;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.DynamicQueries.Commands;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Application.Features.DynamicQueries.Transfer;
using DotNetDBTasks.Application.Features.QueryExecution.Queries;
using DotNetDBTasks.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Admin endpoints for managing dynamic queries.
/// Auditors have read access and can manage query accessibility (roles/departments/users assignments and logs).
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin,Auditor")]
public class DynamicQueriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IResultFileExporter _resultFileExporter;

    public DynamicQueriesController(IMediator mediator, IResultFileExporter resultFileExporter)
    {
        _mediator = mediator;
        _resultFileExporter = resultFileExporter;
    }

    /// <summary>
    /// Retrieves all dynamic queries.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllDynamicQueriesQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a specific dynamic query by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDynamicQueryByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new dynamic query with parameters. Requires Admin role.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(
        [FromBody] CreateDynamicQueryCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Updates an existing dynamic query. Requires Admin role.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateDynamicQueryCommand command,
        CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Deletes a dynamic query. Requires Admin role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteDynamicQueryCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Uploads (or replaces) the Word (.docx) template used when this query's results are
    /// exported as Word. The template may contain {{RESULTS}} (replaced by the result table)
    /// and {{QUERY_NAME}}/{{GENERATED_AT}}/{{ROW_COUNT}} text placeholders. Requires Admin role.
    /// </summary>
    [HttpPost("{id:guid}/word-template")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(MaxTemplateBytes + 1024)]
    public async Task<IActionResult> UploadWordTemplate(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });
        if (file.Length > MaxTemplateBytes)
            return BadRequest(new { message = "The template must be 5 MB or smaller." });
        if (!Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "The template must be a Word .docx file." });

        byte[] content;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, cancellationToken);
            content = ms.ToArray();
        }

        if (!LooksLikeDocx(content))
            return BadRequest(new { message = "The file is not a valid .docx document." });

        await _mediator.Send(
            new SetQueryWordTemplateCommand(id, Path.GetFileName(file.FileName), content),
            cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Downloads the query's current Word export template. 404 when none is uploaded.
    /// </summary>
    [HttpGet("{id:guid}/word-template")]
    public async Task<IActionResult> DownloadWordTemplate(Guid id, CancellationToken cancellationToken)
    {
        var template = await _mediator.Send(new GetQueryWordTemplateQuery(id), cancellationToken);
        if (template is null)
            return NotFound();
        return File(
            template.Content,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            template.FileName);
    }

    /// <summary>
    /// Removes the query's Word export template; Word exports fall back to the built-in
    /// default layout. Requires Admin role.
    /// </summary>
    [HttpDelete("{id:guid}/word-template")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteWordTemplate(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteQueryWordTemplateCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Uploads (or replaces) the system-wide default Word export template, used whenever a
    /// query has no template of its own. Requires Admin role.
    /// </summary>
    [HttpPost("default-word-template")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(MaxTemplateBytes + 1024)]
    public async Task<IActionResult> UploadDefaultWordTemplate(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });
        if (file.Length > MaxTemplateBytes)
            return BadRequest(new { message = "The template must be 5 MB or smaller." });
        if (!Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "The template must be a Word .docx file." });

        byte[] content;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, cancellationToken);
            content = ms.ToArray();
        }

        if (!LooksLikeDocx(content))
            return BadRequest(new { message = "The file is not a valid .docx document." });

        await _mediator.Send(
            new SetDefaultWordTemplateCommand(Path.GetFileName(file.FileName), content),
            cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Downloads the current default Word export template. When none has been uploaded,
    /// returns the built-in starter template — edit it in Word and upload it back to
    /// customize the default look of Word exports.
    /// </summary>
    [HttpGet("default-word-template")]
    public async Task<IActionResult> DownloadDefaultWordTemplate(CancellationToken cancellationToken)
    {
        var stored = await _mediator.Send(new GetDefaultWordTemplateQuery(), cancellationToken);
        var (bytes, name) = stored is null
            ? (_resultFileExporter.GetStarterWordTemplate(), "default-word-template.docx")
            : (stored.Content, stored.FileName);
        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", name);
    }

    /// <summary>
    /// Reports whether a custom default Word template is stored, and its file name.
    /// </summary>
    [HttpGet("default-word-template/info")]
    public async Task<IActionResult> GetDefaultWordTemplateInfo(CancellationToken cancellationToken)
    {
        var stored = await _mediator.Send(new GetDefaultWordTemplateQuery(), cancellationToken);
        return Ok(new { fileName = stored?.FileName, isBuiltIn = stored is null });
    }

    /// <summary>
    /// Removes the custom default template; Word exports without a per-query template fall
    /// back to the built-in starter layout. Requires Admin role.
    /// </summary>
    [HttpDelete("default-word-template")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteDefaultWordTemplate(CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteDefaultWordTemplateCommand(), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Exports one query as a portable JSON file for backup or transfer to another system.
    /// </summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> ExportQuery(Guid id, CancellationToken cancellationToken)
    {
        var file = await _mediator.Send(new ExportQueriesQuery(id), cancellationToken);
        var name = file.Queries.FirstOrDefault()?.Name ?? "query";
        return ExportFileResult(file, $"{Slug(name)}-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    /// <summary>
    /// Exports every query as a single JSON backup. Contains no database credentials — see
    /// <see cref="ExportQueriesQuery"/>.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportAllQueries(CancellationToken cancellationToken)
    {
        var file = await _mediator.Send(new ExportQueriesQuery(null), cancellationToken);
        return ExportFileResult(file, $"queries-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    /// <summary>
    /// Imports queries from a file produced by the export endpoints. Never overwrites: a name
    /// clash is imported as a copy. Requires Admin role.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(MaxImportBytes + 1024)]
    public async Task<IActionResult> ImportQueries(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });
        if (file.Length > MaxImportBytes)
            return BadRequest(new { message = "The backup file must be 50 MB or smaller." });

        QueryExportFile? parsed;
        try
        {
            await using var stream = file.OpenReadStream();
            parsed = await JsonSerializer.DeserializeAsync<QueryExportFile>(
                stream, ExportJsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { message = $"The file is not a valid query export: {ex.Message}" });
        }

        if (parsed is null)
            return BadRequest(new { message = "The file is empty or not a query export." });

        var result = await _mediator.Send(new ImportQueriesCommand(parsed), cancellationToken);
        return Ok(result);
    }

    private IActionResult ExportFileResult(QueryExportFile file, string fileName)
    {
        // Indented on purpose: an export is something an admin may read, diff or keep in
        // source control, not just feed back into the import endpoint.
        var json = JsonSerializer.Serialize(file, ExportJsonOptions);
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Makes a query name safe to use as a download file name.</summary>
    private static string Slug(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        while (cleaned.Contains("--"))
            cleaned = cleaned.Replace("--", "-");
        cleaned = cleaned.Trim('-');
        return cleaned.Length == 0 ? "query" : cleaned;
    }

    private const int MaxImportBytes = 50 * 1024 * 1024;

    private const int MaxTemplateBytes = 5 * 1024 * 1024;

    /// <summary>A .docx is a zip (PK signature) containing word/document.xml.</summary>
    private static bool LooksLikeDocx(byte[] content)
    {
        if (content.Length < 4 || content[0] != 0x50 || content[1] != 0x4B)
            return false;
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(
                new MemoryStream(content), System.IO.Compression.ZipArchiveMode.Read);
            return zip.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// Assigns a query to one or more roles.
    /// </summary>
    [HttpPost("{id:guid}/roles")]
    public async Task<IActionResult> AssignToRoles(
        Guid id,
        [FromBody] AssignQueryToRolesCommand command,
        CancellationToken cancellationToken)
    {
        command.QueryId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Assigns a query to one or more departments.
    /// All users in those departments will gain access.
    /// </summary>
    [HttpPost("{id:guid}/departments")]
    public async Task<IActionResult> AssignToDepartments(
        Guid id,
        [FromBody] AssignQueryToDepartmentsCommand command,
        CancellationToken cancellationToken)
    {
        command.QueryId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Assigns a query to specific individual users.
    /// </summary>
    [HttpPost("{id:guid}/users")]
    public async Task<IActionResult> AssignToUsers(
        Guid id,
        [FromBody] AssignQueryToUsersCommand command,
        CancellationToken cancellationToken)
    {
        command.QueryId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Retrieves one page of execution logs with optional filters.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? queryId,
        [FromQuery] Guid? userId,
        [FromQuery] bool? isSuccess,
        [FromQuery] QueryType? queryType,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool sortDescending = true,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetExecutionLogsQuery
            {
                QueryId = queryId,
                UserId = userId,
                IsSuccess = isSuccess,
                QueryType = queryType,
                Search = search,
                SortBy = sortBy,
                SortDescending = sortDescending,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves one page of the pre-change row snapshots recorded for an execution log.
    /// </summary>
    [HttpGet("logs/{id:guid}/old-values")]
    public async Task<IActionResult> GetLogOldValues(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetExecutionLogOldValuesQuery
            {
                LogId = id,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            cancellationToken);
        return Ok(result);
    }
}
