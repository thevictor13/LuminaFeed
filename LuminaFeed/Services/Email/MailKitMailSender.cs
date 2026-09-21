using LuminaFeed.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LuminaFeed.Services.Email;

/// <summary>
/// MailKit-based SMTP transport. Creates a fresh <see cref="SmtpClient"/> per send (SmtpClient is
/// not safe to share). For local Papercut the connection is plain (no TLS); set
/// <see cref="SmtpOptions.UseStartTls"/> to negotiate STARTTLS for real servers.
/// </summary>
public sealed class MailKitMailSender(IOptions<SmtpOptions> options, ILogger<MailKitMailSender> logger) : IMailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mime = BuildMessage(message);

        using var client = new SmtpClient();
        var socketOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);

        if (!string.IsNullOrEmpty(_options.UserName))
        {
            await client.AuthenticateAsync(_options.UserName, _options.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Sent email to {Recipient} with subject {Subject}", message.ToEmail, message.Subject);
    }

    /// <summary>Builds the MIME message from options + the supplied <see cref="EmailMessage"/>.</summary>
    internal MimeMessage BuildMessage(EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? message.ToEmail, message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();
        return mime;
    }
}
