using LuminaFeed.Data;
using LuminaFeed.Options;
using LuminaFeed.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Tests;

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
}
