namespace LuminaFeed.Services.Email;

/// <summary>
/// Low-level email transport. Implementations deliver a composed <see cref="EmailMessage"/> via an
/// SMTP server. Higher-level senders (Identity emails, article notifications) build the message and
/// delegate here.
/// </summary>
public interface IMailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
