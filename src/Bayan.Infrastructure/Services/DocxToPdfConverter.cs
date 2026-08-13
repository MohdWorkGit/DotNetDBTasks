using System.Diagnostics;
using Bayan.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bayan.Infrastructure.Services;

/// <summary>
/// Converts .docx to PDF with whichever engine the host machine offers, detected once:
/// LibreOffice (headless soffice process — preferred, works in Docker/services) or
/// Microsoft Word via COM automation (Windows machines with Office installed; no
/// interop package needed, so the air-gapped offline bundle stays dependency-free).
///
/// Config section "Export":
///   PdfEngine       — "auto" (default), "libreoffice", "word" or "builtin" (disable conversion)
///   LibreOfficePath — explicit path to soffice.exe when it is not installed in a standard location
///
/// Conversions are serialized: Word COM is single-instance by nature, and one
/// LibreOffice profile cannot run concurrent conversions either.
/// </summary>
public class DocxToPdfConverter : IDocxToPdfConverter
{
    private enum Engine { None, LibreOffice, Word }

    private const int LibreOfficeTimeoutMs = 120_000;

    private readonly ILogger<DocxToPdfConverter> _logger;
    private readonly Lazy<(Engine Engine, string? SofficePath)> _detected;
    private readonly object _conversionLock = new();

    public DocxToPdfConverter(IConfiguration configuration, ILogger<DocxToPdfConverter> logger)
    {
        _logger = logger;
        _detected = new Lazy<(Engine, string?)>(() => Detect(configuration));
    }

    public bool IsAvailable => _detected.Value.Engine != Engine.None;

    public byte[]? TryConvert(byte[] docx)
    {
        var (engine, sofficePath) = _detected.Value;
        if (engine == Engine.None)
            return null;

        var workDir = Path.Combine(Path.GetTempPath(), "bayan-docx2pdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var docxPath = Path.Combine(workDir, "export.docx");
        var pdfPath = Path.Combine(workDir, "export.pdf");
        try
        {
            File.WriteAllBytes(docxPath, docx);
            lock (_conversionLock)
            {
                switch (engine)
                {
                    case Engine.LibreOffice:
                        ConvertWithLibreOffice(sofficePath!, docxPath, workDir);
                        break;
                    case Engine.Word:
                        ConvertWithWord(docxPath, pdfPath);
                        break;
                }
            }
            return File.Exists(pdfPath) ? File.ReadAllBytes(pdfPath) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DOCX->PDF conversion with {Engine} failed; falling back to the built-in PDF layout", engine);
            return null;
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* temp cleanup only */ }
        }
    }

    // ---------- engines ----------

    private void ConvertWithLibreOffice(string sofficePath, string docxPath, string outDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = sofficePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add("--norestore");
        psi.ArgumentList.Add("--convert-to");
        psi.ArgumentList.Add("pdf");
        psi.ArgumentList.Add("--outdir");
        psi.ArgumentList.Add(outDir);
        psi.ArgumentList.Add(docxPath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start soffice.");
        if (!process.WaitForExit(LibreOfficeTimeoutMs))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"LibreOffice conversion exceeded {LibreOfficeTimeoutMs / 1000}s.");
        }
    }

    /// <summary>
    /// Late-bound Word COM automation (wdExportFormatPDF = 17). One fresh Word instance
    /// per conversion keeps state simple; DisplayAlerts off and ReadOnly open prevent
    /// the hidden instance from ever prompting.
    /// </summary>
    private static void ConvertWithWord(string docxPath, string pdfPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Word automation requires Windows.");

        var wordType = Type.GetTypeFromProgID("Word.Application")
            ?? throw new InvalidOperationException("Microsoft Word is not installed.");

        dynamic word = Activator.CreateInstance(wordType)!;
        try
        {
            word.Visible = false;
            word.DisplayAlerts = 0;
            dynamic doc = word.Documents.Open(docxPath, false, true); // ConfirmConversions:=false, ReadOnly:=true
            try
            {
                doc.ExportAsFixedFormat(pdfPath, 17);
            }
            finally
            {
                doc.Close(false);
            }
        }
        finally
        {
            word.Quit(false);
        }
    }

    // ---------- detection ----------

    private (Engine, string?) Detect(IConfiguration configuration)
    {
        var requested = (configuration["Export:PdfEngine"] ?? "auto").Trim().ToLowerInvariant();
        if (requested == "builtin")
        {
            _logger.LogInformation("PDF export uses the built-in layout (Export:PdfEngine = builtin).");
            return (Engine.None, null);
        }

        string? soffice = null;
        if (requested is "auto" or "libreoffice")
            soffice = FindLibreOffice(configuration["Export:LibreOfficePath"]);
        if (soffice is not null)
        {
            _logger.LogInformation("PDF export converts Word-template output via LibreOffice at {Path}", soffice);
            return (Engine.LibreOffice, soffice);
        }
        if (requested == "libreoffice")
        {
            _logger.LogWarning("Export:PdfEngine is 'libreoffice' but soffice.exe was not found; using the built-in PDF layout.");
            return (Engine.None, null);
        }

        if (requested is "auto" or "word")
        {
            if (OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Word.Application") is not null)
            {
                _logger.LogInformation("PDF export converts Word-template output via Microsoft Word automation.");
                return (Engine.Word, null);
            }
            if (requested == "word")
                _logger.LogWarning("Export:PdfEngine is 'word' but Microsoft Word is not installed; using the built-in PDF layout.");
        }

        if (requested == "auto")
            _logger.LogInformation("No DOCX->PDF engine found (LibreOffice/Word); PDF export uses the built-in layout.");
        return (Engine.None, null);
    }

    private static string? FindLibreOffice(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return File.Exists(configuredPath) ? configuredPath : null;

        var exe = OperatingSystem.IsWindows() ? "soffice.exe" : "soffice";
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            })
            {
                if (!string.IsNullOrEmpty(root))
                    candidates.Add(Path.Combine(root, "LibreOffice", "program", exe));
            }
        }
        else
        {
            candidates.Add("/usr/bin/soffice");
            candidates.Add("/usr/local/bin/soffice");
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // Last resort: whatever "soffice" resolves to on PATH.
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), exe);
            try
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }
}
