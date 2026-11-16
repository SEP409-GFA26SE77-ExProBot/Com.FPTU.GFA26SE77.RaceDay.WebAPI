using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RaceDay.Api.Dashboard;

public sealed record DashboardReportSection(string Title, string Note, string[] Headers, object[][] Rows);
public sealed record DashboardReport(string Title, string Scope, DateOnly From, DateOnly To,
    DateTime GeneratedAtUtc, IReadOnlyList<DashboardReportSection> Sections);

public static class DashboardReportWriter
{
    public static byte[] Excel(DashboardReport report)
    {
        using var workbook = new XLWorkbook();
        foreach (var section in report.Sections)
        {
            var sheet = workbook.AddWorksheet(section.Title);
            var width = section.Headers.Length;
            sheet.Style.Font.FontName = "Arial";
            sheet.Style.Font.FontSize = 11;
            string[] metadata = [report.Title, report.Scope,
                $"Kỳ biểu đồ: {report.From:dd/MM/yyyy} - {report.To:dd/MM/yyyy} | Asia/Ho_Chi_Minh (UTC+7)",
                $"Xuất lúc: {report.GeneratedAtUtc.AddHours(7):dd/MM/yyyy HH:mm:ss} (UTC+7)",
                section.Note];
            for (var i = 0; i < metadata.Length; i++)
            {
                sheet.Range(i + 1, 1, i + 1, width).Merge().Value = metadata[i];
                sheet.Row(i + 1).Height = i == 1 ? 72 : i == 4 ? 60 : 30;
            }
            sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(18);
            sheet.Range(1, 1, 5, width).Style.Alignment.WrapText = true;
            sheet.Cell(7, 1).Value = section.Title;
            sheet.Cell(7, 1).Style.Font.SetBold();
            for (var column = 0; column < width; column++)
                sheet.Cell(8, column + 1).Value = section.Headers[column];
            sheet.Range(8, 1, 8, width).Style.Fill.BackgroundColor = XLColor.FromHtml("#C2410C");
            sheet.Range(8, 1, 8, width).Style.Font.SetBold().Font.FontColor = XLColor.White;
            sheet.Row(8).Height = 32;
            for (var row = 0; row < section.Rows.Length; row++)
            for (var column = 0; column < width; column++)
            {
                var cell = sheet.Cell(row + 9, column + 1);
                // Assign text as literal cell values, never interpret external names as formulas.
                cell.Value = XLCellValue.FromObject(section.Rows[row][column], CultureInfo.InvariantCulture);
                if (section.Rows[row][column] is int or decimal)
                    cell.Style.NumberFormat.Format = "#,##0.##";
            }
            if (section.Rows.Length == 0) sheet.Cell(9, 1).Value = "Chưa có dữ liệu.";
            sheet.Range(8, 1, Math.Max(9, section.Rows.Length + 8), width).Style.Alignment.WrapText = true;
            sheet.Columns(1, width).Width = width <= 3 ? 38 : 26;
            sheet.Rows(9, Math.Max(9, section.Rows.Length + 8)).AdjustToContents();
            sheet.SheetView.FreezeRows(8);
            if (section.Rows.Length > 0) sheet.Range(8, 1, section.Rows.Length + 8, width).SetAutoFilter();
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Pdf(DashboardReport report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document =>
        {
            document.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(10));
                    page.Header().Column(column =>
                    {
                        column.Item().Text(report.Title).FontSize(19).Bold().FontColor("#C2410C");
                        column.Item().Text(report.Scope);
                        column.Item().Text($"Kỳ biểu đồ: {report.From:dd/MM/yyyy} - {report.To:dd/MM/yyyy} | Asia/Ho_Chi_Minh (UTC+7)");
                        column.Item().Text($"Xuất lúc: {report.GeneratedAtUtc.AddHours(7):dd/MM/yyyy HH:mm:ss} (UTC+7)").FontSize(9);
                    });
                    page.Content().PaddingVertical(12).Column(column =>
                    {
                        column.Spacing(16);
                        foreach (var section in report.Sections)
                        column.Item().EnsureSpace(100).Column(sectionColumn =>
                        {
                            sectionColumn.Spacing(8);
                            sectionColumn.Item().Text(section.Title).FontSize(14).Bold();
                            sectionColumn.Item().Text(section.Note).FontSize(9).FontColor(Colors.Grey.Darken2);
                            sectionColumn.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    foreach (var _ in section.Headers) columns.RelativeColumn();
                                });
                                table.Header(header =>
                                {
                                    foreach (var label in section.Headers)
                                        header.Cell().Background("#C2410C").Padding(7).Text(label).FontColor(Colors.White).Bold();
                                });
                                foreach (var row in section.Rows)
                                foreach (var value in row)
                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6)
                                        .Text(value is int or decimal ? ((IFormattable)value).ToString("#,##0.##", CultureInfo.GetCultureInfo("vi-VN"))
                                            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                            });
                            if (section.Rows.Length == 0) sectionColumn.Item().Text("Chưa có dữ liệu.");
                        });
                    });
                    page.Footer().AlignRight().Text(text =>
                    {
                        text.Span("RaceDay | Trang ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
        }).GeneratePdf();
    }
}
