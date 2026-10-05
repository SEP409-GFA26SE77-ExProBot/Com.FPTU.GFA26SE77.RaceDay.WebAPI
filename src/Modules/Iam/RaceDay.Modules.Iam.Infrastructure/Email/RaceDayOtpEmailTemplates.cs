using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Iam.Infrastructure.Email;

/// <summary>HTML email layouts for RaceDay (Vietnamese running / race-day product).</summary>
internal static class RaceDayOtpEmailTemplates
{
    public static EmailMessage Build(string toEmail, string username, OtpPurpose purpose, string otpCode)
    {
        var minutes = EmailOtp.DefaultTtlMinutes;
        var accountLine = $"Tên đăng nhập: <strong style=\"color:#9d0208;\">{System.Net.WebUtility.HtmlEncode(username)}</strong>";
        var accountPlain = $"Tên đăng nhập: {username}";

        return purpose switch
        {
            OtpPurpose.EmailVerification => CreateMessage(
                toEmail,
                subject: "RaceDay — Xác thực email đăng ký",
                eyebrow: "XÁC THỰC TÀI KHOẢN",
                headline: "Chào mừng đến RaceDay",
                lead: "Bạn vừa đăng ký tham gia hệ thống sự kiện chạy bộ RaceDay. Nhập mã OTP bên dưới để kích hoạt tài khoản và sẵn sàng cho những đường đua tiếp theo.",
                accountLine,
                accountPlain,
                otpCode,
                minutes,
                tip: "Không chia sẻ mã này với bất kỳ ai. RaceDay không bao giờ hỏi OTP qua điện thoại."),
            OtpPurpose.PasswordReset => CreateMessage(
                toEmail,
                subject: "RaceDay — Đặt lại mật khẩu",
                eyebrow: "BẢO MẬT TÀI KHOẢN",
                headline: "Yêu cầu đặt lại mật khẩu",
                lead: "Chúng tôi nhận được yêu cầu đặt lại mật khẩu RaceDay của bạn. Dùng mã OTP bên dưới để tiếp tục. Nếu bạn không yêu cầu, hãy bỏ qua email này.",
                accountLine,
                accountPlain,
                otpCode,
                minutes,
                tip: "Vì an toàn, mã chỉ dùng một lần và sẽ hết hạn sau vài phút."),
            _ => CreateMessage(
                toEmail,
                subject: "RaceDay — Mã OTP",
                eyebrow: "RACE DAY",
                headline: "Mã xác thực của bạn",
                lead: "Đây là mã OTP dành cho tài khoản RaceDay của bạn.",
                accountLine,
                accountPlain,
                otpCode,
                minutes,
                tip: "Giữ mã ở chế độ riêng tư.")
        };
    }

    private static EmailMessage CreateMessage(
        string toEmail,
        string subject,
        string eyebrow,
        string headline,
        string lead,
        string accountLineHtml,
        string accountLinePlain,
        string otpCode,
        int minutes,
        string tip)
    {
        var spacedOtp = string.Join(" ", otpCode.ToCharArray());
        var plain =
            $"""
            RaceDay
            {headline}

            {lead}

            {accountLinePlain}

            Mã OTP: {otpCode}
            Có hiệu lực trong {minutes} phút.

            {tip}

            — Đội ngũ RaceDay | Sự kiện chạy Việt Nam
            """;

        var html =
            $"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{subject}</title>
            </head>
            <body style="margin:0;padding:0;background:#fff7ed;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fff7ed;padding:32px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #fed7aa;box-shadow:0 12px 40px rgba(234,88,12,0.08);">
                      <tr>
                        <td style="background:linear-gradient(135deg,#e85d04 0%,#dc2f02 55%,#9d0208 100%);padding:28px 28px 24px;">
                          <div style="font-size:12px;letter-spacing:0.22em;color:rgba(255,255,255,0.85);font-weight:700;">RACEDAY</div>
                          <div style="margin-top:8px;font-size:22px;line-height:1.25;color:#ffffff;font-weight:800;">Đường đua của bạn bắt đầu tại đây</div>
                          <div style="margin-top:8px;font-size:13px;color:rgba(255,255,255,0.9);">Nền tảng sự kiện chạy bộ Việt Nam</div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:28px;">
                          <div style="font-size:11px;letter-spacing:0.16em;color:#ea580c;font-weight:700;margin-bottom:10px;">{eyebrow}</div>
                          <h1 style="margin:0 0 12px;font-size:22px;line-height:1.3;color:#0f172a;font-weight:700;">{headline}</h1>
                          <p style="margin:0 0 12px;font-size:15px;line-height:1.65;color:#475569;">{lead}</p>
                          <p style="margin:0 0 24px;font-size:14px;line-height:1.5;color:#64748b;background:#fff7ed;border:1px solid #fed7aa;border-radius:10px;padding:12px 14px;">{accountLineHtml}</p>
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fff7ed;border:1px solid #fed7aa;border-radius:12px;">
                            <tr>
                              <td align="center" style="padding:22px 16px;">
                                <div style="font-size:12px;color:#c2410c;letter-spacing:0.12em;margin-bottom:10px;font-weight:700;">MÃ OTP</div>
                                <div style="font-size:34px;letter-spacing:0.35em;font-weight:800;color:#9d0208;font-family:Consolas,'Courier New',monospace;">{spacedOtp}</div>
                                <div style="margin-top:12px;font-size:13px;color:#64748b;">Hiệu lực {minutes} phút</div>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:22px 0 0;font-size:13px;line-height:1.6;color:#64748b;">{tip}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:18px 28px 26px;border-top:1px solid #ffedd5;background:#fffbeb;">
                          <div style="font-size:12px;color:#94a3b8;line-height:1.6;">
                            Email tự động từ <strong style="color:#ea580c;">RaceDay</strong> — ứng dụng cuộc thi / sự kiện chạy Việt Nam.<br />
                            Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email.
                          </div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new EmailMessage(toEmail, subject, html, plain);
    }
}
