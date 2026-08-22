using Bayan.API.Authorization;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Application.Features.QueryExecution.Commands;
using Bayan.Application.Features.Reports.Commands;
using Bayan.Application.Features.Reports.Queries;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Interfaces;
using Bayan.Domain.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

/// <summary>
/// Running side of reports: list what you may run, run one, read its sections, download it.
///
/// <para>A run produces one cached result per section, filed into the same job store the query
/// grid uses. That is why there is no paging endpoint here — the viewer pages each section
/// through <c>GET /api/user/queries/jobs/{jobId}/rows</c>, inheriting its server-side filter and
/// sort behaviour rather than growing a second, subtly different implementation.</para>
/// </summary>
[ApiController]
[Route("api/user/reports")]
[RequirePermission(Permissions.ReportsRun)]
public class UserReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IQueryJobStore _jobStore;
    private readonly IReportRunStore _runStore;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionService _permissions;
    private readonly IResultFileExporter _exporter;

    public UserReportsController(
        IMediator mediator,
        IQueryJobStore jobStore,
        IReportRunStore runStore,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork,
        IPermissionService permissions,
        IResultFileExporter exporter)
    {
        _mediator = mediator;
        _jobStore = jobStore;
        _runStore = runStore;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _permissions = permissions;
        _exporter = exporter;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetMyReportsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetMyReportByIdQuery { Id = id }, cancellationToken));

    /// <summary>
    /// Runs the report and files each section's rows into the result cache, returning the
    /// section metadata plus the job id the grid pages against. The rows themselves do not
    /// cross the wire here.
    /// </summary>
    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> Run(
        Guid id,
        [FromBody] RunReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RunReportCommand
        {
            ReportId = id,
            Parameters = request.Parameters ?? new()
        }, cancellationToken);

        var snapshot = new UserContextSnapshot(
            _currentUser.UserId, _currentUser.Username, _currentUser.Roles);

        var envelope = new ReportRunEnvelope
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            ReportId = result.ReportId,
            ReportName = result.ReportName,
            Parameters = result.Parameters,
            Charts = result.Charts,
            Warnings = result.Warnings,
            ExecutionDurationMs = result.ExecutionDurationMs,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var section in result.Sections)
        {
            Guid? jobId = null;

            if (section.IsSuccess && section.Columns.Count > 0)
            {
                // Reuse the query job store so this section's rows get the same heap/disk spill,
                // retention and eviction handling as any other cached result.
                var job = _jobStore.Create(_currentUser.UserId, snapshot, new ExecuteQueryCommand
                {
                    QueryId = section.DynamicQueryId ?? Guid.Empty,
                    CacheFullResult = true
                });

                _jobStore.SetResult(job.Id, new QueryExecutionResult
                {
                    Columns = section.Columns,
                    Rows = section.Rows,
                    TotalRows = section.TotalRows,
                    ExecutionDurationMs = section.ExecutionDurationMs
                });

                jobId = job.Id;
            }

            envelope.Sections.Add(new ReportRunSection
            {
                Key = section.Key,
                Title = section.Title,
                JobId = jobId,
                Columns = section.Columns,
                TotalRows = section.TotalRows,
                Error = section.Error,
                IsVisibleInViewer = section.IsVisibleInViewer
            });
        }

        _runStore.Add(envelope);
        return Ok(ToRunDto(envelope));
    }

    /// <summary>The section metadata for a run that is still cached.</summary>
    [HttpGet("runs/{runId:guid}")]
    public IActionResult GetRun(Guid runId)
    {
        var envelope = _runStore.Get(runId);
        if (envelope is null)
            return RunGone();

        if (!MayRead(envelope))
            return Forbid();

        return Ok(ToRunDto(envelope));
    }

    /// <summary>
    /// Downloads the whole run as one document. Every section becomes a keyed result set, so the
    /// Word template's {{RESULTS:key}} markers resolve to the right section.
    /// </summary>
    [HttpGet("runs/{runId:guid}/export-file")]
    public async Task<IActionResult> ExportFile(
        Guid runId,
        [FromQuery] string format,
        CancellationToken cancellationToken)
    {
        var envelope = _runStore.Get(runId);
        if (envelope is null)
            return RunGone();

        if (!MayRead(envelope))
            return Forbid();

        // Accepts the file extension the client sends as well as the enum name, matching the
        // query export endpoint's contract — the two must agree or the same menu would work on
        // one page and 400 on the other.
        var parsed = (format ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "xlsx" or "excel" => ExportFileFormat.Excel,
            "csv" => ExportFileFormat.Csv,
            "json" => ExportFileFormat.Json,
            "pdf" => ExportFileFormat.Pdf,
            "docx" or "word" => ExportFileFormat.Word,
            _ => (ExportFileFormat?)null
        };
        if (parsed is null)
            return BadRequest(new { message = $"Unsupported export format '{format}'. Use xlsx, csv, json, pdf or docx." });
        var exportFormat = parsed.Value;

        var report = await _unitOfWork.Reports.GetByIdAsync(envelope.ReportId, cancellationToken);
        if (report is null)
            return NotFound();

        // Both halves of the gate, re-checked here. The client only offers formats it was told
        // about, but hiding a menu item is not a control.
        var permitted = ExportPermissions.Parse(report.AllowedExportFormats);
        if (!permitted.Contains(exportFormat))
            return Forbid();

        var held = await _permissions.HasAsync(
            _currentUser.Roles, ExportPermissions.ReportPermissionFor(exportFormat), cancellationToken);
        if (!held)
            return Forbid();

        var sets = new List<ExportResultSet>();
        foreach (var section in envelope.Sections)
        {
            if (section.JobId is null)
                continue;

            var job = _jobStore.Get(section.JobId.Value);
            if (job?.CachedRows is null)
                return RunGone();

            sets.Add(new ExportResultSet(
                section.Columns,
                job.CachedRows.AsRowList(),
                section.Key,
                section.Title));
        }

        if (sets.Count == 0)
            return BadRequest(new { message = "This run produced no data to export." });

        // With no stored template the exporter would fall back to the single-query starter,
        // whose bare {{RESULTS}} appends every section into ONE table under the first
        // section's header — which is wrong the moment two sections have different columns.
        // Generating this report's own starter gives each section its own {{RESULTS:key}}
        // table, so a report renders correctly before anyone has designed a template for it.
        var template = report.TemplateDocx is { Length: > 0 }
            ? report.TemplateDocx
            : _exporter.GetReportStarterTemplate(
                envelope.ReportName,
                sets.Select(set => new ReportTemplateSection(set.Key!, set.Title ?? set.Key!)).ToList(),
                System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft,
                // The run's charts already carry the dataset each was built from, so their
                // markers land under the right section rather than at the end.
                envelope.Charts
                    .Select(c => new ReportTemplateChart(c.Key, c.Title, c.DatasetKey))
                    .ToList());
        // Note: the generated fallback lays sections out flat. A report with master/detail
        // datasets wants the {{#EACH}} layout, which is what the downloadable starter produces —
        // this path only ever runs before an author has uploaded anything.

        var bytes = _exporter.Export(
            exportFormat,
            sets,
            envelope.ReportName,
            includeHeaders: true,
            wordTemplate: template,
            parameters: envelope.Parameters,
            charts: envelope.Charts);

        var extension = _exporter.GetExtension(exportFormat);
        var fileName = $"{SafeFileName(envelope.ReportName)}_{DateTime.Now:yyyyMMdd-HHmmss}.{extension}";
        return File(bytes, ContentTypeFor(exportFormat), fileName);
    }

    /// <summary>
    /// Releases a run and every section's cached rows. Called when the viewer is left, so a
    /// report's results do not sit in the cache until retention expires them one section at a time.
    /// </summary>
    [HttpDelete("runs/{runId:guid}")]
    public IActionResult ReleaseRun(Guid runId)
    {
        var envelope = _runStore.Get(runId);
        if (envelope is not null && !MayRead(envelope))
            return Forbid();

        _runStore.Remove(runId);
        return NoContent();
    }

    // ---------------------------------------------------------------- helpers

    private bool MayRead(ReportRunEnvelope envelope) =>
        envelope.UserId == _currentUser.UserId || _currentUser.Roles.Contains(RoleNames.Admin);

    /// <summary>
    /// 410 rather than 404: the run existed and has been evicted or released, which the client
    /// handles by offering to run it again. Matches how an expired query job is reported.
    /// </summary>
    private IActionResult RunGone() =>
        StatusCode(StatusCodes.Status410Gone,
            new { message = "This report result is no longer available. Run the report again." });

    private static object ToRunDto(ReportRunEnvelope envelope) => new
    {
        runId = envelope.Id,
        reportId = envelope.ReportId,
        reportName = envelope.ReportName,
        executionDurationMs = envelope.ExecutionDurationMs,
        warnings = envelope.Warnings,
        charts = envelope.Charts.Select(c => new
        {
            key = c.Key,
            title = c.Title,
            type = (int)c.Type,
            categories = c.Categories,
            series = c.Series.Select(x => new { name = x.Name, values = x.Values })
        }),
        sections = envelope.Sections.Select(s => new
        {
            key = s.Key,
            title = s.Title,
            jobId = s.JobId,
            columns = s.Columns,
            totalRows = s.TotalRows,
            error = s.Error,
            isVisibleInViewer = s.IsVisibleInViewer
        })
    };

    private static string ContentTypeFor(ExportFileFormat format) => format switch
    {
        ExportFileFormat.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ExportFileFormat.Csv => "text/csv",
        ExportFileFormat.Json => "application/json",
        ExportFileFormat.Pdf => "application/pdf",
        ExportFileFormat.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };

    private static string SafeFileName(string name)
    {
        var cleaned = new string(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned;
    }
}

/// <summary>The report-level parameter values the run form submitted.</summary>
public class RunReportRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
}
