using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Shared.Infrastructure.Email;

public sealed class SmtpEmailSender(
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var host = configuration["Smtp:Host"]
            ?? throw new InvalidOperationException("Smtp:Host is missing.");
        var port = int.TryParse(configuration["Smtp:Port"], out var p) ? p : 587;
        var username = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"]?.Replace(" ", string.Empty);
        var fromAddress = configuration["Smtp:From"] ?? username
            ?? throw new InvalidOperationException("Smtp:From is missing.");
        var fromName = configuration["Smtp:FromName"] ?? "RaceDay";
        var useSsl = !string.Equals(configuration["Smtp:UseSsl"], "false", StringComparison.OrdinalIgnoreCase);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(fromName, fromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Headers.Add("X-Entity-Ref-ID", Guid.NewGuid().ToString("N"));
        var builder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.PlainTextBody ?? StripTags(message.HtmlBody)
        };
        mime.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            host,
            port,
            useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
            cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                await client.AuthenticateAsync(username, password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            logger.LogInformation(
                "SMTP email sent via {Host}:{Port} as {From} to {To} subject {Subject}",
                host,
                port,
                fromAddress,
                message.To,
                message.Subject);
        }
        catch (AuthenticationException ex)
        {
            logger.LogError(ex, "SMTP auth failed for {Username} on {Host}", username, host);
            throw new InvalidOperationException(
                "Gmail SMTP từ chối đăng nhập. Kiểm tra Smtp__Username / Smtp__Password (App Password 16 ký tự, không khoảng trắng) trong .env.",
                ex);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }

    private static string StripTags(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}

/// <summary>Dev fallback — logs email content (including OTP) when SMTP is not configured.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "SMTP not configured — email to {To} | {Subject} | {Body}",
            message.To,
            message.Subject,
            message.PlainTextBody ?? message.HtmlBody);
        return Task.CompletedTask;
    }
}
