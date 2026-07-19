using System.Globalization;
using System.IO.Compression;
using System.Text;
using DotNetDBTasks.Application.Common.Models;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// Generates a minimal but valid PDF (landscape table) using only the base class
/// library — no third-party package, so it works inside the air-gapped offline bundle,
/// mirroring the approach of <see cref="ExcelExporter"/>. Uses the built-in Helvetica
/// fonts (no embedding) with WinAnsi encoding, so characters outside Latin-1 are
/// rendered as '?'. Content streams are Flate-compressed via <see cref="ZLibStream"/>.
///
/// Layout auto-fits the columns: it starts at A4 landscape / 8pt and, when the columns
/// need more room, first shrinks the font and then steps up to A3 landscape, so that
/// wide result sets keep every column visible without truncating whole columns away.
/// </summary>
internal static class PdfExporter
{
    private const double Margin = 36;
    private const double CellPaddingRatio = 0.375;  // of font size
    private const double RowHeightRatio = 1.625;    // of font size
    private const double BaseFontSize = 8;
    private const double MinFontSizeA4 = 6.5;       // shrink this far before going to A3
    private const double MinFontSize = 5.5;         // absolute floor; below this, truncate
    private const double TitleFontSize = 13;
    private const double FooterReserve = 28;
    private const double TitleBlockHeight = 36;
    private const double MinColumnWidth = 30;
    private const double MaxColumnWidth = 260;
    private const int WidthSampleRows = 500;

    private sealed record Layout(double PageWidth, double PageHeight, double FontSize)
    {
        public double RowHeight => FontSize * RowHeightRatio;
        public double HeaderRowHeight => FontSize * RowHeightRatio + 3;
        public double CellPadding => FontSize * CellPaddingRatio;
        public double Available => PageWidth - 2 * Margin;
    }

    private static readonly (double W, double H) A4Landscape = (842, 595);
    private static readonly (double W, double H) A3Landscape = (1191, 842);

    /// <summary>Helvetica AFM advance widths (1/1000 em) for chars 32–126; others fall back to 556.</summary>
    private static readonly short[] HelveticaWidths =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    };

    public static byte[] Export(IReadOnlyList<ExportResultSet> results, string title, bool includeHeaders)
    {
        var columns = results.Count > 0 ? results[0].Columns : Array.Empty<string>();
        var naturalWidths = MeasureNaturalWidths(columns, results);
        var layout = PickLayout(naturalWidths);
        var columnWidths = FitColumnWidths(naturalWidths, layout);
        var pages = BuildPages(results, columns, columnWidths, layout, title, includeHeaders);
        return Assemble(pages, layout);
    }

    // ---------- layout ----------

    /// <summary>Widths (points, at the base font size) each column would naturally need.</summary>
    private static double[] MeasureNaturalWidths(
        IReadOnlyList<string> columns,
        IReadOnlyList<ExportResultSet> results)
    {
        var widths = new double[columns.Count];
        for (int c = 0; c < columns.Count; c++)
            widths[c] = TextWidth(columns[c], BaseFontSize) * 1.08; // headers are bold, slightly wider

        int sampled = 0;
        foreach (var result in results)
        {
            foreach (var row in result.Rows)
            {
                if (sampled++ >= WidthSampleRows)
                    break;
                for (int c = 0; c < columns.Count && c < result.Columns.Count; c++)
                {
                    row.TryGetValue(result.Columns[c], out var value);
                    var w = TextWidth(ResultFileExporter.FormatValue(value), BaseFontSize);
                    if (w > widths[c])
                        widths[c] = w;
                }
            }
            if (sampled >= WidthSampleRows)
                break;
        }

        var basePadding = 2 * BaseFontSize * CellPaddingRatio;
        for (int c = 0; c < widths.Length; c++)
            widths[c] = Math.Clamp(widths[c] + basePadding, MinColumnWidth, MaxColumnWidth);
        return widths;
    }

    /// <summary>
    /// Picks the smallest page/font combination that shows every column at full width:
    /// A4 at 8pt, A4 shrunk to 6.5pt, A3 shrunk to 5.5pt — otherwise A3 at the floor
    /// size with proportional squeezing (cell text may truncate with "...").
    /// </summary>
    private static Layout PickLayout(double[] naturalWidths)
    {
        double total = naturalWidths.Sum();
        if (total <= 0)
            return new Layout(A4Landscape.W, A4Landscape.H, BaseFontSize);

        double a4Available = A4Landscape.W - 2 * Margin;
        if (total <= a4Available)
            return new Layout(A4Landscape.W, A4Landscape.H, BaseFontSize);

        double a4Factor = a4Available / total;
        if (BaseFontSize * a4Factor >= MinFontSizeA4)
            return new Layout(A4Landscape.W, A4Landscape.H, BaseFontSize * a4Factor);

        double a3Available = A3Landscape.W - 2 * Margin;
        double a3Factor = Math.Min(1, a3Available / total);
        return new Layout(A3Landscape.W, A3Landscape.H, Math.Max(MinFontSize, BaseFontSize * a3Factor));
    }

    /// <summary>Scales the natural widths to exactly span the chosen page's printable width.</summary>
    private static double[] FitColumnWidths(double[] naturalWidths, Layout layout)
    {
        double total = naturalWidths.Sum();
        if (total <= 0)
            return naturalWidths;

        double scale = layout.Available / total;
        var widths = new double[naturalWidths.Length];
        for (int c = 0; c < widths.Length; c++)
            widths[c] = naturalWidths[c] * scale;
        return widths;
    }

    private static List<byte[]> BuildPages(
        IReadOnlyList<ExportResultSet> results,
        IReadOnlyList<string> columns,
        double[] columnWidths,
        Layout layout,
        string title,
        bool includeHeaders)
    {
        // Flatten rows positionally against the first set's column slots, matching ExcelExporter.
        var cells = new List<string[]>();
        foreach (var result in results)
        {
            foreach (var row in result.Rows)
            {
                var line = new string[columns.Count];
                for (int c = 0; c < columns.Count; c++)
                {
                    object? value = null;
                    if (c < result.Columns.Count)
                        row.TryGetValue(result.Columns[c], out value);
                    line[c] = ResultFileExporter.FormatValue(value);
                }
                cells.Add(line);
            }
        }

        int firstPageCapacity = RowCapacity(layout, isFirstPage: true, includeHeaders);
        int otherPageCapacity = RowCapacity(layout, isFirstPage: false, includeHeaders);
        int totalPages = cells.Count <= firstPageCapacity
            ? 1
            : 1 + (int)Math.Ceiling((cells.Count - firstPageCapacity) / (double)otherPageCapacity);

        var pages = new List<byte[]>(totalPages);
        int rowIndex = 0;
        for (int page = 1; page <= totalPages; page++)
        {
            int capacity = page == 1 ? firstPageCapacity : otherPageCapacity;
            int count = Math.Min(capacity, cells.Count - rowIndex);
            pages.Add(BuildPageContent(
                columns, columnWidths, layout, cells, rowIndex, count,
                title, includeHeaders, page, totalPages));
            rowIndex += count;
        }
        return pages;
    }

    private static int RowCapacity(Layout layout, bool isFirstPage, bool includeHeaders)
    {
        double top = layout.PageHeight - Margin - (isFirstPage ? TitleBlockHeight : 0);
        double usable = top - Margin - FooterReserve - (includeHeaders ? layout.HeaderRowHeight : 0);
        return Math.Max(1, (int)(usable / layout.RowHeight));
    }

    private static byte[] BuildPageContent(
        IReadOnlyList<string> columns,
        double[] columnWidths,
        Layout layout,
        List<string[]> cells,
        int firstRow,
        int rowCount,
        string title,
        bool includeHeaders,
        int pageNumber,
        int totalPages)
    {
        var sb = new StringBuilder();
        double tableTop = layout.PageHeight - Margin;

        if (pageNumber == 1)
        {
            BeginText(sb, "F2", TitleFontSize, Margin, layout.PageHeight - Margin - TitleFontSize,
                Truncate(title, layout.Available, TitleFontSize));
            var subtitle = $"Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}  |  {cells.Count} row(s)";
            sb.Append("0.4 0.4 0.4 rg\n");
            BeginText(sb, "F1", 7.5, Margin, layout.PageHeight - Margin - TitleFontSize - 12, subtitle);
            sb.Append("0 0 0 rg\n");
            tableTop -= TitleBlockHeight;
        }

        double y = tableTop;
        if (includeHeaders && columns.Count > 0)
        {
            sb.Append("0.90 0.90 0.90 rg\n");
            AppendRect(sb, Margin, y - layout.HeaderRowHeight, layout.Available, layout.HeaderRowHeight);
            sb.Append("0 0 0 rg\n");

            double x = Margin;
            for (int c = 0; c < columns.Count; c++)
            {
                BeginText(sb, "F2", layout.FontSize, x + layout.CellPadding,
                    y - layout.HeaderRowHeight + layout.FontSize * 0.55,
                    Truncate(columns[c], columnWidths[c] - 2 * layout.CellPadding, layout.FontSize));
                x += columnWidths[c];
            }

            sb.Append("0.6 0.6 0.6 RG 0.5 w\n");
            sb.Append(Pt(Margin)).Append(' ').Append(Pt(y - layout.HeaderRowHeight)).Append(" m ")
              .Append(Pt(layout.PageWidth - Margin)).Append(' ').Append(Pt(y - layout.HeaderRowHeight)).Append(" l S\n");
            y -= layout.HeaderRowHeight;
        }

        for (int r = 0; r < rowCount; r++)
        {
            if (r % 2 == 1)
            {
                sb.Append("0.96 0.96 0.96 rg\n");
                AppendRect(sb, Margin, y - layout.RowHeight, layout.Available, layout.RowHeight);
                sb.Append("0 0 0 rg\n");
            }

            var line = cells[firstRow + r];
            double x = Margin;
            for (int c = 0; c < line.Length; c++)
            {
                if (line[c].Length > 0)
                    BeginText(sb, "F1", layout.FontSize, x + layout.CellPadding,
                        y - layout.RowHeight + layout.FontSize * 0.4,
                        Truncate(line[c], columnWidths[c] - 2 * layout.CellPadding, layout.FontSize));
                x += columnWidths[c];
            }
            y -= layout.RowHeight;
        }

        var footer = $"Page {pageNumber} of {totalPages}";
        sb.Append("0.4 0.4 0.4 rg\n");
        BeginText(sb, "F1", 7, (layout.PageWidth - TextWidth(footer, 7)) / 2, 20, footer);
        sb.Append("0 0 0 rg\n");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    // ---------- text helpers ----------

    private static void BeginText(StringBuilder sb, string font, double size, double x, double y, string text)
    {
        sb.Append("BT /").Append(font).Append(' ').Append(Pt(size)).Append(" Tf ")
          .Append(Pt(x)).Append(' ').Append(Pt(y)).Append(" Td (")
          .Append(EscapePdfString(text)).Append(") Tj ET\n");
    }

    private static void AppendRect(StringBuilder sb, double x, double y, double w, double h) =>
        sb.Append(Pt(x)).Append(' ').Append(Pt(y)).Append(' ')
          .Append(Pt(w)).Append(' ').Append(Pt(h)).Append(" re f\n");

    private static string Pt(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static double CharWidth(char ch) =>
        ch is >= ' ' and <= '~' ? HelveticaWidths[ch - ' '] : 556;

    private static double TextWidth(string text, double fontSize)
    {
        double units = 0;
        foreach (var ch in text)
            units += CharWidth(ch);
        return units * fontSize / 1000;
    }

    /// <summary>Cuts text that would overflow the cell, appending "..." to signal truncation.</summary>
    private static string Truncate(string text, double maxWidth, double fontSize)
    {
        if (TextWidth(text, fontSize) <= maxWidth)
            return text;

        double ellipsisWidth = TextWidth("...", fontSize);
        double width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            double next = width + CharWidth(text[i]) * fontSize / 1000;
            if (next + ellipsisWidth > maxWidth)
                return text[..i] + "...";
            width = next;
        }
        return text;
    }

    /// <summary>
    /// Escapes PDF string delimiters and maps text to WinAnsi: Latin-1 passes through,
    /// control chars are dropped, anything else becomes '?'.
    /// </summary>
    private static string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\' or '(' or ')':
                    sb.Append('\\').Append(ch);
                    break;
                case < ' ':
                    break;
                case <= 'ÿ':
                    sb.Append(ch);
                    break;
                default:
                    sb.Append('?');
                    break;
            }
        }
        return sb.ToString();
    }

    // ---------- document assembly ----------

    private static byte[] Assemble(List<byte[]> pageContents, Layout layout)
    {
        using var ms = new MemoryStream();
        var offsets = new long[5 + 2 * pageContents.Count]; // 1-based object offsets (index 0 unused)

        WriteAscii(ms, "%PDF-1.4\n%âãÏÓ\n");

        var kids = string.Join(' ', Enumerable.Range(0, pageContents.Count).Select(i => $"{5 + 2 * i} 0 R"));

        offsets[1] = ms.Position;
        WriteAscii(ms, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = ms.Position;
        WriteAscii(ms, $"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageContents.Count} >>\nendobj\n");

        offsets[3] = ms.Position;
        WriteAscii(ms, "3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        offsets[4] = ms.Position;
        WriteAscii(ms, "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        for (int i = 0; i < pageContents.Count; i++)
        {
            int pageObj = 5 + 2 * i;
            int contentObj = pageObj + 1;

            offsets[pageObj] = ms.Position;
            WriteAscii(ms,
                $"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R " +
                $"/MediaBox [0 0 {Pt(layout.PageWidth)} {Pt(layout.PageHeight)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> " +
                $"/Contents {contentObj} 0 R >>\nendobj\n");

            var compressed = Deflate(pageContents[i]);
            offsets[contentObj] = ms.Position;
            WriteAscii(ms, $"{contentObj} 0 obj\n<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
            ms.Write(compressed);
            WriteAscii(ms, "\nendstream\nendobj\n");
        }

        long xrefPosition = ms.Position;
        int objectCount = offsets.Length;
        WriteAscii(ms, $"xref\n0 {objectCount}\n0000000000 65535 f \n");
        for (int i = 1; i < objectCount; i++)
            WriteAscii(ms, offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        WriteAscii(ms, $"trailer\n<< /Size {objectCount} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return ms.ToArray();
    }

    private static void WriteAscii(MemoryStream ms, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        ms.Write(bytes);
    }
}
