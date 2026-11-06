using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RaceDay.Modules.Event.Application.Registrations;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public interface ILeaderboardPdfService
{
    byte[] GenerateLeaderboardPdf(string eventName, string distanceName, IReadOnlyList<LeaderboardItemDto> items);
}

public sealed class QuestPdfLeaderboardService : ILeaderboardPdfService
{
    public byte[] GenerateLeaderboardPdf(
        string eventName,
        string distanceName,
        IReadOnlyList<LeaderboardItemDto> items)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.Header().Column(column =>
                {
                    column.Item().Text(eventName).FontSize(18).Bold();
                    column.Item().Text($"BẢNG XẾP HẠNG - {distanceName}")
                        .FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                });
                page.Content().PaddingTop(16).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(42);
                        columns.ConstantColumn(58);
                        columns.RelativeColumn(2);
                        columns.ConstantColumn(72);
                        columns.ConstantColumn(72);
                        columns.ConstantColumn(72);
                    });
                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "Hạng");
                        HeaderCell(header.Cell(), "BIB");
                        HeaderCell(header.Cell(), "Họ tên");
                        HeaderCell(header.Cell(), "Giới tính");
                        HeaderCell(header.Cell(), "Thời gian");
                        HeaderCell(header.Cell(), "Pace");
                    });
                    foreach (var item in items)
                    {
                        Cell(table.Cell(), item.Rank.ToString());
                        Cell(table.Cell(), item.Bib);
                        Cell(table.Cell(), item.FullName);
                        Cell(table.Cell(), item.Gender ?? "-");
                        Cell(table.Cell(), item.TotalTime);
                        Cell(table.Cell(), item.Pace);
                    }
                });
                page.Footer().AlignCenter().Text($"Tổng số VĐV hoàn thành: {items.Count}");
            });
        });

        return document.GeneratePdf();
    }

    private static void HeaderCell(IContainer container, string text)
        => container.Background(Colors.Blue.Darken2).Padding(5).Text(text).FontColor(Colors.White).Bold();

    private static void Cell(IContainer container, string text)
        => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(text);
}
