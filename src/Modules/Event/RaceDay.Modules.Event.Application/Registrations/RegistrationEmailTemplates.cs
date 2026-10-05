using System.Net;
using System.Text.Json;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Event.Application.Registrations;

/// <summary>
/// Email vé / thanh toán — palette khớp FE User (nền sáng + cam RaceDay)
/// và header gradient giống OTP: #e85d04 → #dc2f02 → #9d0208.
/// </summary>
internal static class RegistrationEmailTemplates
{
    public static EmailMessage PaymentReceived(
        EventRegistration reg,
        RaceEvent race,
        EventDistance distance,
        decimal amount,
        string currency)
    {
        var subject = $"[RaceDay] Thanh toán thành công — chờ duyệt · {race.Name}";
        var when = FormatEventWhen(race);
        var plain =
            $"""
            RaceDay — Thanh toán đã nhận

            Xin chào {reg.FullName},

            Chúng tôi đã nhận thanh toán đăng ký của bạn.
            Giải: {race.Name}
            Cự ly: {distance.Type} · {distance.Name} ({distance.DistanceKm} km)
            Số tiền: {amount:N0} {currency}
            Trạng thái: Đang chờ Ban tổ chức duyệt hồ sơ

            Thời gian giải: {when}
            Địa điểm: {race.Location}

            Bạn sẽ nhận email vé điện tử (BIB + QR) ngay khi BTC duyệt.

            — Đội ngũ RaceDay
            """;

        var html = Wrap(
            eyebrow: "THANH TOÁN THÀNH CÔNG",
            headline: "Đã nhận lệ phí đăng ký",
            lead: $"Xin chào <strong style=\"color:#0f172a;\">{Esc(reg.FullName)}</strong>, khoản thanh toán cho <strong style=\"color:#0f172a;\">{Esc(race.Name)}</strong> đã được ghi nhận.",
            bodyHtml:
            $"""
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin:16px 0;border:1px solid #fed7aa;border-radius:12px;overflow:hidden;background:#fff7ed;">
              {Row("Giải", race.Name)}
              {Row("Cự ly", $"{distance.Type} · {distance.Name} ({distance.DistanceKm} km)")}
              {Row("Số tiền", $"{amount:N0} {currency}")}
              {Row("Trạng thái", "Chờ Ban tổ chức duyệt")}
              {Row("Thời gian", when)}
              {Row("Địa điểm", race.Location)}
            </table>
            <p style="color:#64748b;font-size:14px;line-height:1.6;margin:0;">
              Vé điện tử (số BIB + mã QR check-in) sẽ được gửi ngay sau khi BTC duyệt hồ sơ của bạn.
            </p>
            """);

        return new EmailMessage(reg.Email, subject, html, plain);
    }

    public static EmailMessage TicketConfirmed(
        EventRegistration reg,
        RaceEvent race,
        EventDistance distance,
        string ticketCode,
        string qrJson)
    {
        var subject = $"[RaceDay] Vé điện tử · BIB {reg.BibNumber} · {race.Name}";
        var when = FormatEventWhen(race);
        var qrUrl =
            "https://api.qrserver.com/v1/create-qr-code/?size=240x240&data="
            + Uri.EscapeDataString(qrJson);

        var remindStart = race.EventDate.ToDateTime(race.StartTime ?? new TimeOnly(5, 30));
        var checkInHint = remindStart.AddHours(-1.5).ToString("HH:mm");

        var plain =
            $"""
            RaceDay — Vé điện tử đã sẵn sàng

            Xin chào {reg.FullName},

            Hồ sơ đăng ký của bạn đã được DUYỆT.
            Giải: {race.Name}
            Số BIB: {reg.BibNumber}
            Mã vé: {ticketCode}
            Cự ly: {distance.Type} · {distance.Name} ({distance.DistanceKm} km)
            Ngày giờ: {when}
            Địa điểm: {race.Location}

            QR payload: {qrJson}

            NHẮC HẸN RACE DAY
            - Có mặt trước {checkInHint} để check-in / nhận BIB
            - Mang theo CCCD/CMND khớp hồ sơ đăng ký
            - Mang email vé này (QR) trên điện thoại
            - Chuẩn bị trang phục, giày chạy, bình nước
            - Tuân thủ hướng dẫn BTC tại điểm xuất phát

            Ban tổ chức: {race.Organizer.Name} · {race.Organizer.Email} · {race.Organizer.Phone}

            Chúc bạn về đích an toàn!
            — Đội ngũ RaceDay
            """;

        var html = Wrap(
            eyebrow: "VÉ ĐIỆN TỬ · RACE DAY",
            headline: $"BIB {Esc(reg.BibNumber ?? "")}",
            lead: $"Chúc mừng <strong style=\"color:#0f172a;\">{Esc(reg.FullName)}</strong> — hồ sơ đã được duyệt. Đây là vé tham dự <strong style=\"color:#0f172a;\">{Esc(race.Name)}</strong>.",
            bodyHtml:
            $"""
            <div style="text-align:center;margin:20px 0 8px;">
              <div style="display:inline-block;background:#ffffff;padding:14px;border-radius:16px;border:1px solid #fed7aa;box-shadow:0 8px 24px rgba(234,88,12,0.12);">
                <img src="{qrUrl}" width="220" height="220" alt="QR check-in" style="display:block;border:0;" />
              </div>
              <p style="color:#ea580c;font-size:12px;font-weight:700;margin:12px 0 0;letter-spacing:0.06em;">Quét QR tại cổng check-in</p>
            </div>

            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin:16px 0;border:1px solid #fed7aa;border-radius:12px;overflow:hidden;background:#fff7ed;">
              {Row("Số BIB", reg.BibNumber ?? "-")}
              {Row("Mã vé", ticketCode)}
              {Row("VĐV", reg.FullName)}
              {Row("Giải", race.Name)}
              {Row("Cự ly", $"{distance.Type} · {distance.Name} · {distance.DistanceKm} km")}
              {Row("Xuất phát", when)}
              {Row("Địa điểm", race.Location)}
              {(string.IsNullOrWhiteSpace(reg.ShirtSize) ? "" : Row("Áo", $"{reg.ShirtType} {reg.ShirtSize} {reg.ShirtColor}".Trim()))}
            </table>

            <div style="background:#fff7ed;border:1px solid #fdba74;border-radius:12px;padding:16px 18px;margin:18px 0;">
              <p style="margin:0 0 10px;color:#c2410c;font-size:12px;letter-spacing:.12em;font-weight:800;">NHẮC HẸN RACE DAY</p>
              <ul style="margin:0;padding-left:18px;color:#334155;font-size:14px;line-height:1.7;">
                <li>Có mặt trước <strong style="color:#ea580c;">{checkInHint}</strong> để check-in / nhận BIB (≥ 90 phút trước xuất phát).</li>
                <li>Mang theo <strong>CCCD/CMND</strong> khớp thông tin đăng ký.</li>
                <li>Mang <strong>email vé này</strong> (QR) trên điện thoại — screenshot dự phòng.</li>
                <li>Chuẩn bị giày chạy, trang phục, bình nước; lắng nghe hướng dẫn BTC tại start line.</li>
                <li>Giữ an toàn: không đeo tai nghe quá to; tuân thủ cutoff của cự ly.</li>
              </ul>
            </div>

            <p style="color:#64748b;font-size:13px;line-height:1.6;margin:0;">
              BTC: <strong style="color:#0f172a;">{Esc(race.Organizer.Name)}</strong>
              · {Esc(race.Organizer.Email)}
              · {Esc(race.Organizer.Phone)}
            </p>
            <p style="color:#94a3b8;font-size:12px;margin:16px 0 0;line-height:1.5;">
              Chúc bạn về đích mạnh mẽ. Hẹn gặp trên đường đua — RaceDay.
            </p>
            """);

        return new EmailMessage(reg.Email, subject, html, plain);
    }

    public static EmailMessage RegistrationRejected(
        EventRegistration reg,
        RaceEvent race,
        string reason)
    {
        var subject = $"[RaceDay] Hồ sơ chưa được duyệt · {race.Name}";
        var plain =
            $"""
            RaceDay — Thông báo duyệt hồ sơ

            Xin chào {reg.FullName},
            Rất tiếc, đăng ký tham gia {race.Name} chưa được duyệt.
            Lý do: {reason}

            Liên hệ BTC: {race.Organizer.Email} / {race.Organizer.Phone}
            — Đội ngũ RaceDay
            """;

        var html = Wrap(
            eyebrow: "THÔNG BÁO DUYỆT",
            headline: "Hồ sơ chưa được duyệt",
            lead: $"Xin chào <strong style=\"color:#0f172a;\">{Esc(reg.FullName)}</strong>, đăng ký <strong style=\"color:#0f172a;\">{Esc(race.Name)}</strong> chưa được chấp nhận lần này.",
            bodyHtml:
            $"""
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin:16px 0;border:1px solid #fed7aa;border-radius:12px;overflow:hidden;background:#fff7ed;">
              {Row("Lý do", reason)}
              {Row("BTC", $"{race.Organizer.Name} · {race.Organizer.Email}")}
            </table>
            """);

        return new EmailMessage(reg.Email, subject, html, plain);
    }

    private static string FormatEventWhen(RaceEvent race)
    {
        var date = race.EventDate.ToString("dddd, dd/MM/yyyy", new System.Globalization.CultureInfo("vi-VN"));
        var time = race.StartTime?.ToString("HH:mm") ?? "sẽ thông báo";
        return $"{date} · xuất phát {time}";
    }

    private static string Row(string label, string value) =>
        $"""
        <tr>
          <td style="padding:10px 14px;color:#c2410c;font-size:12px;font-weight:700;width:34%;border-bottom:1px solid #ffedd5;vertical-align:top;background:#fff7ed;">{Esc(label)}</td>
          <td style="padding:10px 14px;color:#0f172a;font-size:14px;border-bottom:1px solid #ffedd5;font-weight:600;background:#ffffff;">{Esc(value)}</td>
        </tr>
        """;

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static string Wrap(string eyebrow, string headline, string lead, string bodyHtml) =>
        $"""
        <!DOCTYPE html>
        <html lang="vi"><head><meta charset="utf-8"/><meta name="viewport" content="width=device-width,initial-scale=1"/>
        <title>RaceDay</title></head>
        <body style="margin:0;padding:0;background:#fff7ed;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fff7ed;padding:32px 12px;">
            <tr><td align="center">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #fed7aa;box-shadow:0 12px 40px rgba(234,88,12,0.08);">
                <tr><td style="background:linear-gradient(135deg,#e85d04 0%,#dc2f02 55%,#9d0208 100%);padding:28px 28px 24px;">
                  <div style="font-size:12px;letter-spacing:0.22em;color:rgba(255,255,255,0.85);font-weight:700;">RACEDAY</div>
                  <div style="margin-top:8px;font-size:11px;letter-spacing:0.16em;color:rgba(255,255,255,0.9);font-weight:700;">{eyebrow}</div>
                  <h1 style="margin:10px 0 0;color:#ffffff;font-size:26px;line-height:1.2;font-weight:800;">{headline}</h1>
                  <div style="margin-top:8px;font-size:13px;color:rgba(255,255,255,0.9);">Nền tảng sự kiện chạy bộ Việt Nam</div>
                </td></tr>
                <tr><td style="padding:28px;">
                  <p style="margin:0 0 8px;color:#475569;font-size:15px;line-height:1.65;">{lead}</p>
                  {bodyHtml}
                </td></tr>
                <tr><td style="padding:18px 28px 26px;border-top:1px solid #ffedd5;background:#fffbeb;">
                  <p style="margin:0;color:#94a3b8;font-size:12px;line-height:1.6;">
                    Email tự động từ <strong style="color:#ea580c;">RaceDay</strong> — sự kiện chạy Việt Nam.<br/>
                    Không chia sẻ vé / QR cho người khác.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body></html>
        """;

    public static string BuildQrJson(EventRegistration reg, EventDistance? distance)
    {
        return JsonSerializer.Serialize(new
        {
            user_id = reg.UserId,
            bib = reg.BibNumber,
            event_id = reg.EventId,
            distance = distance?.DistanceKm ?? 0
        });
    }

    public static string BuildTicketCode(EventRegistration reg)
    {
        var raw = $"RD-{reg.EventId:N}-{reg.BibNumber}-{reg.UserId:N}";
        return raw[..Math.Min(80, raw.Length)];
    }
}
