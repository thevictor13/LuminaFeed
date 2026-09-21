using LuminaFeed.Data;
using LuminaFeed.Options;
using LuminaFeed.Services.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Tests;

/// <summary>Captures log entries so tests can assert on level/exception.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
}

public class EmailLayoutTests
{
    [Fact]
    public void Render_ProducesTableBasedShell_NotDivBased()
    {
        var html = EmailLayout.Render("Heading", "<p>Body content</p>");

        Assert.Contains("<table", html);
        Assert.DoesNotContain("<div", html);
        Assert.Contains("Heading", html);
        Assert.Contains("<p>Body content</p>", html);
        // VML namespace so Outlook can render bulletproof buttons.
        Assert.Contains("urn:schemas-microsoft-com:vml", html);
    }

    [Fact]
    public void Render_HtmlEncodesHeading()
    {
        var html = EmailLayout.Render("<script>alert(1)</script>", "<p>ok</p>");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Button_HtmlEncodesTextAndUrl()
    {
        var html = EmailLayout.Button("<b>Click</b>", "https://x.test/a?a=1&b=2");

        Assert.DoesNotContain("<b>Click</b>", html);
        Assert.Contains("&lt;b&gt;Click&lt;/b&gt;", html);
        // The ampersand between query params is HTML-encoded inside the href attribute.
        Assert.Contains("a=1&amp;b=2", html);
    }

    [Fact]
    public void Button_EmitsOutlookAndStandardVariants()
    {
        var html = EmailLayout.Button("Confirm email", "https://example.test/confirm?x=1");

        // Outlook-only VML variant, gated behind an mso conditional comment.
        Assert.Contains("[if mso]", html);
        Assert.Contains("v:roundrect", html);
        // Standard anchor for every other client.
        Assert.Contains("[if !mso]", html);
        Assert.Contains("<a href=\"https://example.test/confirm?x=1\"", html);
        Assert.Contains("Confirm email", html);
    }
}

public class MailKitMailSenderTests
{
    private static MailKitMailSender CreateSender() => new(
        Microsoft.Extensions.Options.Options.Create(new SmtpOptions
        {
            Host = "localhost",
            Port = 25,
            FromAddress = "no-reply@luminafeed.local",
            FromName = "LuminaFeed",
        }),
        NullLogger<MailKitMailSender>.Instance);

    [Fact]
    public void BuildMessage_SetsFromToSubjectAndHtmlBody()
    {
        var sender = CreateSender();

        var mime = sender.BuildMessage(new EmailMessage("user@example.test", "User", "Subject line", "<p>Hi</p>"));

        Assert.Equal("Subject line", mime.Subject);
        Assert.Equal("no-reply@luminafeed.local", ((MimeKit.MailboxAddress)mime.From[0]).Address);
        Assert.Equal("LuminaFeed", ((MimeKit.MailboxAddress)mime.From[0]).Name);
        Assert.Equal("user@example.test", ((MimeKit.MailboxAddress)mime.To[0]).Address);
        Assert.Contains("<p>Hi</p>", mime.HtmlBody);
    }

    [Fact]
    public async Task SendAsync_OnTransportFailure_LogsErrorAndRethrows()
    {
        var logger = new ListLogger<MailKitMailSender>();
        // Port 1 on loopback refuses immediately, forcing ConnectAsync to throw.
        var sender = new MailKitMailSender(
            Microsoft.Extensions.Options.Options.Create(new SmtpOptions
            {
                Host = "127.0.0.1",
                Port = 1,
                FromAddress = "no-reply@luminafeed.local",
                FromName = "LuminaFeed",
            }),
            logger);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            sender.SendAsync(new EmailMessage("user@example.test", null, "Subject", "<p>Hi</p>")));

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception is not null);
    }
}

public class IdentityEmailSenderTests
{
    private sealed class CapturingMailSender : IMailSender
    {
        public EmailMessage? Sent { get; private set; }
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent = message;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_BuildsTableBasedEmailWithLink()
    {
        var mail = new CapturingMailSender();
        var sender = new IdentityEmailSender(mail);
        var user = new ApplicationUser { UserName = "alice", Email = "alice@example.test" };
        const string link = "https://example.test/Account/ConfirmEmail?code=abc";

        await sender.SendConfirmationLinkAsync(user, user.Email, link);

        Assert.NotNull(mail.Sent);
        Assert.Equal("alice@example.test", mail.Sent!.ToEmail);
        Assert.Equal("Confirm your LuminaFeed account", mail.Sent.Subject);
        Assert.Contains(link, mail.Sent.HtmlBody);
        Assert.Contains("<table", mail.Sent.HtmlBody);
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_IncludesPlainTextAlternative_WithLink()
    {
        var mail = new CapturingMailSender();
        var sender = new IdentityEmailSender(mail);
        var user = new ApplicationUser { UserName = "alice", Email = "alice@example.test" };
        const string link = "https://example.test/Account/ConfirmEmail?code=abc";

        await sender.SendConfirmationLinkAsync(user, user.Email, link);

        Assert.NotNull(mail.Sent!.TextBody);
        Assert.False(string.IsNullOrWhiteSpace(mail.Sent.TextBody));
        Assert.Contains(link, mail.Sent.TextBody!);
        // The plain-text alternative is exactly that — text, not the HTML shell.
        Assert.DoesNotContain("<table", mail.Sent.TextBody);
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_IncludesPlainTextAlternative_WithCode()
    {
        var mail = new CapturingMailSender();
        var sender = new IdentityEmailSender(mail);
        var user = new ApplicationUser { UserName = "alice", Email = "alice@example.test" };
        const string code = "RESET-CODE-123";

        await sender.SendPasswordResetCodeAsync(user, user.Email, code);

        Assert.Contains(code, mail.Sent!.TextBody!);
        Assert.Contains(code, mail.Sent.HtmlBody);
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_HtmlEncodesInterpolatedLink()
    {
        var mail = new CapturingMailSender();
        var sender = new IdentityEmailSender(mail);
        var user = new ApplicationUser { UserName = "alice", Email = "alice@example.test" };
        // A hostile link value must not break out of the HTML it is interpolated into.
        const string link = "https://example.test/confirm?code=\"><script>alert(1)</script>";

        await sender.SendConfirmationLinkAsync(user, user.Email, link);

        Assert.DoesNotContain("<script>alert(1)</script>", mail.Sent!.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.Sent.HtmlBody);
        // The plain-text alternative carries the raw link (no HTML context to escape).
        Assert.Contains(link, mail.Sent.TextBody!);
    }
}
