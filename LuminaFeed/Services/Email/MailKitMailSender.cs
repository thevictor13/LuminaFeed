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

        // MailKit's SmtpClient is IDisposable only (not IAsyncDisposable); disposal runs after the
        // awaited DisconnectAsync above, so a synchronous using is correct here.
        using var client = new SmtpClient();
        var socketOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(_options.UserName))
            {
                await client.AuthenticateAsync(_options.UserName, _options.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }

            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log with context, then rethrow so callers (e.g. the Identity account flows) still see the failure.
            logger.LogError(ex, "Failed to send email to {Recipient} with subject {Subject}", message.ToEmail, message.Subject);
            throw;
        }

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
