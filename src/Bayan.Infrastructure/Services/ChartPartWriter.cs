using System.Globalization;
using System.Security;
using System.Text;
using Bayan.Application.Common.Models;
using Bayan.Domain.Enums;

namespace Bayan.Infrastructure.Services;

/// <summary>
/// Writes a chart as a native DrawingML chart part, the same kind Word itself produces.
///
/// <para>The values are embedded as <b>literal caches</b> — <c>&lt;c:strLit&gt;</c> for the
/// categories and <c>&lt;c:numLit&gt;</c> for the numbers — with no <c>&lt;c:externalData&gt;</c>
/// and so no embedded spreadsheet part. Word and LibreOffice both render a literal-cache chart;
/// the only thing missing is "Edit Data", which is right for a generated report.</para>
///
/// <para>The alternative was drawing the chart into a PNG. That needs more than an encoder: it
/// needs a rasteriser and a bitmap font to put labels on the axes, and this bundle is air-gapped
/// — <c>System.Drawing.Common</c> is a package and Windows-only, SkiaSharp is out. Hand-rolled
/// glyph rendering, in Arabic as well as English, is a text-shaping project rather than a chart
/// feature. Vector DrawingML also stays sharp when the document is printed or converted to PDF.</para>
///
/// <para>A second reason, specific to this codebase: the other vector option is a shape group
/// with <c>wps:txbx</c> text boxes, which injects <c>&lt;w:p&gt;</c> elements inside a paragraph
/// — precisely the input that the paragraph regexes in <see cref="WordExporter"/> have to guard
/// against. A <c>c:chart</c> reference contains no <c>w:p</c> at all.</para>
/// </summary>
internal static class ChartPartWriter
{
    private const string ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WordDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    public const string ChartContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";

    public const string ChartRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";

    /// <summary>
    /// The series palette, matching the colours the on-screen chart component uses so the figure
    /// in the document and the figure in the viewer are recognisably the same chart.
    /// </summary>
    private static readonly string[] Palette =
    {
        "4F46E5", "0891B2", "16A34A", "CA8A04", "DC2626",
        "7C3AED", "0D9488", "65A30D", "EA580C", "BE123C"
    };

    private static string Colour(int index) => Palette[index % Palette.Length];

    /// <summary>
    /// Axis ids only have to be unique and consistent within one chart part, so they are derived
    /// from the chart's position rather than tracked globally.
    /// </summary>
    private static (int Category, int Value) AxisIds(int chartNumber) =>
        (100_000_000 + chartNumber * 2, 100_000_001 + chartNumber * 2);

    /// <summary>The chart part itself: <c>/word/charts/chartN.xml</c>.</summary>
    public static string BuildChartXml(ReportChartData chart, int chartNumber)
    {
        var (catAxId, valAxId) = AxisIds(chartNumber);
        var sb = new StringBuilder();

        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
          .Append($"<c:chartSpace xmlns:c=\"{ChartNs}\" xmlns:a=\"{DrawingNs}\" xmlns:r=\"{RelationshipNs}\">")
          .Append("<c:chart>");

        if (!string.IsNullOrWhiteSpace(chart.Title))
        {
            sb.Append("<c:title><c:tx><c:rich>")
              .Append("<a:bodyPr/><a:lstStyle/>")
              .Append("<a:p><a:r><a:t>").Append(Escape(chart.Title)).Append("</a:t></a:r></a:p>")
              .Append("</c:rich></c:tx><c:overlay val=\"0\"/></c:title>")
              .Append("<c:autoTitleDeleted val=\"0\"/>");
        }
        else
        {
            sb.Append("<c:autoTitleDeleted val=\"1\"/>");
        }

        sb.Append("<c:plotArea><c:layout/>");

        switch (chart.Type)
        {
            case ReportChartType.Pie:
                AppendPie(sb, chart);
                break;
            case ReportChartType.Line:
                AppendLine(sb, chart, catAxId, valAxId);
                AppendAxes(sb, catAxId, valAxId, horizontalValues: false);
                break;
            case ReportChartType.Bar:
                AppendBar(sb, chart, catAxId, valAxId, direction: "bar");
                AppendAxes(sb, catAxId, valAxId, horizontalValues: true);
                break;
            default:
                AppendBar(sb, chart, catAxId, valAxId, direction: "col");
                AppendAxes(sb, catAxId, valAxId, horizontalValues: false);
                break;
        }

        sb.Append("</c:plotArea>");

        // One series needs no legend: the title already says what is plotted.
        if (chart.Series.Count > 1 || chart.Type == ReportChartType.Pie)
            sb.Append("<c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend>");

        sb.Append("<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/>")
          .Append("</c:chart></c:chartSpace>");

        return sb.ToString();
    }

    private static void AppendBar(
        StringBuilder sb, ReportChartData chart, int catAxId, int valAxId, string direction)
    {
        sb.Append("<c:barChart>")
          .Append($"<c:barDir val=\"{direction}\"/>")
          .Append("<c:grouping val=\"clustered\"/>")
          .Append("<c:varyColors val=\"0\"/>");

        for (var i = 0; i < chart.Series.Count; i++)
            AppendSeries(sb, chart, i);

        sb.Append("<c:gapWidth val=\"75\"/>")
          .Append($"<c:axId val=\"{catAxId}\"/><c:axId val=\"{valAxId}\"/>")
          .Append("</c:barChart>");
    }

    private static void AppendLine(StringBuilder sb, ReportChartData chart, int catAxId, int valAxId)
    {
        sb.Append("<c:lineChart>")
          .Append("<c:grouping val=\"standard\"/>")
          .Append("<c:varyColors val=\"0\"/>");

        for (var i = 0; i < chart.Series.Count; i++)
            AppendSeries(sb, chart, i);

        sb.Append("<c:marker val=\"1\"/>")
          .Append($"<c:axId val=\"{catAxId}\"/><c:axId val=\"{valAxId}\"/>")
          .Append("</c:lineChart>");
    }

    private static void AppendPie(StringBuilder sb, ReportChartData chart)
    {
        // A pie shows parts of one whole, so only the first series is meaningful.
        sb.Append("<c:pieChart><c:varyColors val=\"1\"/>");
        AppendSeries(sb, chart, 0);
        sb.Append("<c:firstSliceAng val=\"0\"/></c:pieChart>");
    }

    /// <summary>
    /// An explicit fill for a series or a pie slice.
    ///
    /// <para>This is not decoration. A series with no <c>spPr</c> takes its colour from the
    /// package's theme, and a hand-built package has no <c>theme1.xml</c> to take it from — so the
    /// shapes resolve to no fill and the plot area comes out empty while the axes, gridlines and
    /// labels still draw, because those fall back to a default black. Stating the colour here
    /// makes the chart self-describing, and identical whether the template was generated by
    /// <see cref="WordExporter.BuildStarterTemplate"/> or uploaded from Word.</para>
    /// </summary>
    private static void AppendSolidFill(StringBuilder sb, string colour, bool separator = false)
    {
        sb.Append("<c:spPr><a:solidFill><a:srgbClr val=\"").Append(colour).Append("\"/></a:solidFill>")
          .Append(separator
              // Adjacent slices of the same pie need a visible edge between them.
              ? "<a:ln w=\"19050\"><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill></a:ln>"
              : "<a:ln><a:noFill/></a:ln>")
          .Append("</c:spPr>");
    }

    private static void AppendLineStroke(StringBuilder sb, string colour)
    {
        sb.Append("<c:spPr><a:ln w=\"28575\" cap=\"rnd\">")
          .Append("<a:solidFill><a:srgbClr val=\"").Append(colour).Append("\"/></a:solidFill>")
          .Append("<a:round/></a:ln></c:spPr>");
    }

    private static void AppendSeries(StringBuilder sb, ReportChartData chart, int index)
    {
        var series = chart.Series[index];
        var isPie = chart.Type == ReportChartType.Pie;
        var isLine = chart.Type == ReportChartType.Line;

        sb.Append("<c:ser>")
          .Append($"<c:idx val=\"{index}\"/><c:order val=\"{index}\"/>")
          .Append("<c:tx><c:v>").Append(Escape(series.Name)).Append("</c:v></c:tx>");

        // Every series type puts spPr straight after tx, and a line puts it before its marker.
        if (isLine)
            AppendLineStroke(sb, Colour(index));
        else if (!isPie)
            AppendSolidFill(sb, Colour(index));

        if (isLine)
        {
            sb.Append("<c:marker><c:symbol val=\"circle\"/><c:size val=\"5\"/>");
            AppendSolidFill(sb, Colour(index));
            sb.Append("</c:marker>");
        }

        // A pie is one series drawn in many colours, so every slice states its own.
        if (isPie)
        {
            for (var i = 0; i < chart.Categories.Count; i++)
            {
                sb.Append($"<c:dPt><c:idx val=\"{i}\"/><c:bubble3D val=\"0\"/>");
                AppendSolidFill(sb, Colour(i), separator: true);
                sb.Append("</c:dPt>");
            }
        }

        // Categories, cached literally so the chart needs no backing workbook.
        sb.Append("<c:cat><c:strLit>")
          .Append($"<c:ptCount val=\"{chart.Categories.Count}\"/>");
        for (var i = 0; i < chart.Categories.Count; i++)
        {
            sb.Append($"<c:pt idx=\"{i}\"><c:v>")
              .Append(Escape(chart.Categories[i]))
              .Append("</c:v></c:pt>");
        }
        sb.Append("</c:strLit></c:cat>");

        sb.Append("<c:val><c:numLit>")
          .Append("<c:formatCode>General</c:formatCode>")
          .Append($"<c:ptCount val=\"{chart.Categories.Count}\"/>");
        for (var i = 0; i < chart.Categories.Count; i++)
        {
            // A missing point is omitted rather than written as zero, so the chart shows a gap.
            var value = i < series.Values.Count ? series.Values[i] : null;
            if (value is null)
                continue;

            sb.Append($"<c:pt idx=\"{i}\"><c:v>")
              .Append(value.Value.ToString("0.##########", CultureInfo.InvariantCulture))
              .Append("</c:v></c:pt>");
        }
        sb.Append("</c:numLit></c:val>");

        if (isLine)
            sb.Append("<c:smooth val=\"0\"/>");

        sb.Append("</c:ser>");
    }

    private static void AppendAxes(StringBuilder sb, int catAxId, int valAxId, bool horizontalValues)
    {
        // A bar chart lies on its side: its categories run up the left and its values along the
        // bottom, which is the opposite of every other type here.
        var categoryPosition = horizontalValues ? "l" : "b";
        var valuePosition = horizontalValues ? "b" : "l";

        sb.Append("<c:catAx>")
          .Append($"<c:axId val=\"{catAxId}\"/>")
          .Append("<c:scaling><c:orientation val=\"minMax\"/></c:scaling>")
          .Append("<c:delete val=\"0\"/>")
          .Append($"<c:axPos val=\"{categoryPosition}\"/>")
          .Append($"<c:crossAx val=\"{valAxId}\"/>")
          .Append("</c:catAx>");

        sb.Append("<c:valAx>")
          .Append($"<c:axId val=\"{valAxId}\"/>")
          .Append("<c:scaling><c:orientation val=\"minMax\"/></c:scaling>")
          .Append("<c:delete val=\"0\"/>")
          .Append($"<c:axPos val=\"{valuePosition}\"/>")
          .Append("<c:majorGridlines/>")
          .Append("<c:numFmt formatCode=\"General\" sourceLinked=\"1\"/>")
          .Append($"<c:crossAx val=\"{catAxId}\"/>")
          .Append("</c:valAx>");
    }

    /// <summary>
    /// The paragraph that places the chart in the body: an inline drawing pointing at the chart
    /// part through <paramref name="relationshipId"/>.
    /// </summary>
    public static string BuildDrawingParagraph(
        ReportChartData chart, string relationshipId, int chartNumber)
    {
        return "<w:p><w:r><w:drawing>" +
               $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" xmlns:wp=\"{WordDrawingNs}\">" +
               $"<wp:extent cx=\"{chart.WidthEmu}\" cy=\"{chart.HeightEmu}\"/>" +
               "<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>" +
               $"<wp:docPr id=\"{1000 + chartNumber}\" name=\"Chart {chartNumber}\"/>" +
               $"<a:graphic xmlns:a=\"{DrawingNs}\">" +
               $"<a:graphicData uri=\"{ChartNs}\">" +
               $"<c:chart xmlns:c=\"{ChartNs}\" xmlns:r=\"{RelationshipNs}\" r:id=\"{relationshipId}\"/>" +
               "</a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
    }

    private static string Escape(string value) =>
        SecurityElement.Escape(value) ?? string.Empty;
}
