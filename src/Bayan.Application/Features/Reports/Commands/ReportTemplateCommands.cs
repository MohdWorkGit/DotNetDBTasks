using System.IO.Compression;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.Reports.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Reports.Commands;

/// <summary>
/// Stores the Word template that lays a report out, and reports back what it references.
///
/// <para>The inspection is the point of returning anything at all. The whole feature rests on
/// the author typing <c>{{RESULTS:key}}</c> correctly in Word, and a mistyped key is otherwise
/// invisible — the section just renders empty, later, in a document nobody re-reads. Checking
/// at upload turns that into an immediate, specific message while they still have the file
/// open.</para>
/// </summary>
public class SetReportTemplateCommand : IRequest<ReportTemplateInspectionDto>
{
    public Guid ReportId { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
}

public class SetReportTemplateCommandHandler
    : IRequestHandler<SetReportTemplateCommand, ReportTemplateInspectionDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResultFileExporter _exporter;

    public SetReportTemplateCommandHandler(IUnitOfWork unitOfWork, IResultFileExporter exporter)
    {
        _unitOfWork = unitOfWork;
        _exporter = exporter;
    }

    public async Task<ReportTemplateInspectionDto> Handle(
        SetReportTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var report = await _unitOfWork.Reports.GetByIdAsync(request.ReportId, cancellationToken, "Datasets")
            ?? throw new NotFoundException(nameof(Report), request.ReportId);

        if (request.Content.Length == 0)
            throw new DomainException("The template file is empty.");

        // Checked here rather than only at the controller: a .docx that is not really a .docx
        // would otherwise be discovered at render time, on a schedule, with nobody watching.
        if (!LooksLikeDocx(request.Content))
            throw new DomainException("That file is not a Word (.docx) document.");

        report.TemplateDocx = request.Content;
        report.TemplateFileName = request.FileName;
        report.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Reports.Update(report);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Describe(_exporter.InspectWordTemplate(request.Content), report);
    }

    /// <summary>
    /// Lines the template's markers up against the report's datasets, so the client can say
    /// which sections will render, which key matches nothing, and which dataset will run but
    /// appear nowhere.
    /// </summary>
    internal static ReportTemplateInspectionDto Describe(
        Common.Models.ReportTemplateInspection inspection,
        Report report)
    {
        var datasetKeys = report.Datasets.Select(d => d.DatasetKey).ToList();

        return new ReportTemplateInspectionDto
        {
            TemplateFileName = report.TemplateFileName,
            MatchedDatasetKeys = inspection.ReferencedDatasetKeys
                .Where(k => datasetKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
                .ToList(),
            UnknownDatasetKeys = inspection.ReferencedDatasetKeys
                .Where(k => !datasetKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
                .ToList(),
            DatasetsWithoutMarker = datasetKeys
                .Where(k => !inspection.ReferencedDatasetKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
                .ToList(),
            DuplicateKeysInOneTable = inspection.DuplicateKeysInOneTable.ToList(),
            HasUnkeyedResultsMarker = inspection.HasUnkeyedResultsMarker,
            HasPlaceholderInsideTextBox = inspection.HasPlaceholderInsideTextBox
        };
    }

    /// <summary>
    /// A real zip carrying a main document part — the extension alone says nothing, and the
    /// exporter would fail confusingly on anything else.
    /// </summary>
    private static bool LooksLikeDocx(byte[] content)
    {
        if (content.Length < 4 || content[0] != 0x50 || content[1] != 0x4B)
            return false;

        try
        {
            using var ms = new MemoryStream(content, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            return zip.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}

// ----------------------------------------------------------------------------

/// <summary>Removes a report's template, so it falls back to the generated starter layout.</summary>
public class DeleteReportTemplateCommand : IRequest<Unit>
{
    public Guid ReportId { get; set; }
}

public class DeleteReportTemplateCommandHandler : IRequestHandler<DeleteReportTemplateCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteReportTemplateCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Unit> Handle(DeleteReportTemplateCommand request, CancellationToken cancellationToken)
    {
        var report = await _unitOfWork.Reports.GetByIdAsync(request.ReportId, cancellationToken)
            ?? throw new NotFoundException(nameof(Report), request.ReportId);

        report.TemplateDocx = null;
        report.TemplateFileName = null;
        report.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Reports.Update(report);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ----------------------------------------------------------------------------

/// <summary>
/// The report's stored template, or — when it has none — a generated starter already carrying
/// this report's real dataset keys. Downloading, editing in Word and uploading back is the
/// intended authoring loop, and it works from the first click because of that fallback.
/// </summary>
public class GetReportTemplateQuery : IRequest<(byte[] Content, string FileName)>
{
    public Guid ReportId { get; set; }

    /// <summary>Forces the generated starter even when a template is stored.</summary>
    public bool Starter { get; set; }
}

public class GetReportTemplateQueryHandler
    : IRequestHandler<GetReportTemplateQuery, (byte[] Content, string FileName)>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResultFileExporter _exporter;

    public GetReportTemplateQueryHandler(IUnitOfWork unitOfWork, IResultFileExporter exporter)
    {
        _unitOfWork = unitOfWork;
        _exporter = exporter;
    }

    public async Task<(byte[] Content, string FileName)> Handle(
        GetReportTemplateQuery request,
        CancellationToken cancellationToken)
    {
        var report = await _unitOfWork.Reports.GetByIdAsync(request.ReportId, cancellationToken, "Datasets")
            ?? throw new NotFoundException(nameof(Report), request.ReportId);

        if (!request.Starter && report.TemplateDocx is { Length: > 0 })
            return (report.TemplateDocx, report.TemplateFileName ?? $"{report.Name}.docx");

        // Direction follows the language the caller is working in — the same Accept-Language
        // the rest of the API localises by — so an Arabic session downloads an Arabic template.
        var starter = _exporter.GetReportStarterTemplate(
            report.Name,
            ReportMapper.ToTemplateSections(report),
            System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft,
            ReportMapper.ToTemplateCharts(report));
        return (starter, $"{SafeFileName(report.Name)}-template.docx");
    }

    private static string SafeFileName(string name)
    {
        var cleaned = new string(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned;
    }
}
