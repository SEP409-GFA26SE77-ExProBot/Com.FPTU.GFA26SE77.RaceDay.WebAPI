using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RaceDay.Modules.Event.Application.Certificates;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public interface ICertificatePdfService
{
    byte[] GenerateCertificatePdf(CertificateDataDto data);
}

public class QuestPdfCertificateQuery : ICertificatePdfService
{
    public byte[] GenerateCertificatePdf(CertificateDataDto data)
    {
        // Kích hoạt license cộng đồng của QuestPDF
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape()); // Khổ A4 nằm ngang
                page.Margin(30);
                page.Background(Colors.White);

                // Header
                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("RACE_DAY OFFICIAL CERTIFICATE").FontSize(12).Bold().FontColor(Colors.Grey.Darken1);
                        col.Item().Text(data.RaceName).FontSize(20).Bold().FontColor(Colors.Blue.Darken2);
                    });
                    row.ConstantItem(120).AlignRight().Text("VERIFIED").FontSize(14).Bold().FontColor(Colors.Green.Medium);
                });

                // Nội dung chứng nhận
                page.Content().PaddingVertical(20).Column(col =>
                {
                    col.Spacing(15);

                    col.Item().AlignCenter().Text("GIẤY CHỨNG NHẬN HOÀN THÀNH").FontSize(26).Bold().FontColor(Colors.Black);
                    col.Item().AlignCenter().Text("Ban tổ chức chứng nhận vinh danh vận động viên").FontSize(13).Italic();

                    // Tên VĐV
                    col.Item().AlignCenter().PaddingVertical(10).Text(data.RunnerName).FontSize(30).Bold().FontColor(Colors.Red.Darken2);

                    col.Item().AlignCenter().Text($"Đã xuất sắc hoàn thành cự ly chạy {data.DistanceName} ({data.DistanceKm} km)").FontSize(15);

                    // Bảng thông số (Box thông tin)
                    col.Item().Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Row(row =>
                    {
                        row.RelativeItem().AlignCenter().Column(c =>
                        {
                            c.Item().Text("SỐ BIB").FontSize(10).Bold().FontColor(Colors.Grey.Darken1);
                            c.Item().Text(data.BibNumber).FontSize(16).Bold();
                        });
                        row.RelativeItem().AlignCenter().Column(c =>
                        {
                            c.Item().Text("THỜI GIAN (TIME)").FontSize(10).Bold().FontColor(Colors.Grey.Darken1);
                            c.Item().Text(data.FinishTime).FontSize(16).Bold().FontColor(Colors.Blue.Medium);
                        });
                        row.RelativeItem().AlignCenter().Column(c =>
                        {
                            c.Item().Text("PACE (PHÚT/KM)").FontSize(10).Bold().FontColor(Colors.Grey.Darken1);
                            c.Item().Text(data.Pace).FontSize(16).Bold();
                        });
                        row.RelativeItem().AlignCenter().Column(c =>
                        {
                            c.Item().Text("THỨ HẠNG (RANK)").FontSize(10).Bold().FontColor(Colors.Grey.Darken1);
                            c.Item().Text(data.Rank.HasValue ? $"#{data.Rank.Value}" : "N/A").FontSize(16).Bold().FontColor(Colors.Green.Darken1);
                        });
                    });
                });

                // Footer
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Ngày cấp chứng nhận: ").FontSize(10);
                    text.Span($"{data.CompletedAtUtc:dd/MM/yyyy HH:mm:ss} UTC").FontSize(10).Bold();
                    text.Span(" — Được chứng thực tự động bởi nền tảng RaceDay").FontSize(10).Italic();
                });
            });
        });

        return document.GeneratePdf();
    }
}