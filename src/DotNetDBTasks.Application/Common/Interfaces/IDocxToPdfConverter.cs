namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Converts a Word (.docx) document to PDF using a locally installed rendering engine
/// (LibreOffice or Microsoft Word). When an engine is available, PDF exports are
/// produced by converting the Word-template output — so PDFs get the same template
/// styling as Word exports; otherwise callers fall back to the built-in table PDF.
/// </summary>
public interface IDocxToPdfConverter
{
    /// <summary>Whether a conversion engine was found (config: Export:PdfEngine).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Converts .docx bytes to PDF bytes. Returns null when conversion fails — callers
    /// are expected to fall back to the built-in PDF layout rather than error out.
    /// </summary>
    byte[]? TryConvert(byte[] docx);
}
