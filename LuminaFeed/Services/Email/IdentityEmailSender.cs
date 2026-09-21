using System.Text.Encodings.Web;
using LuminaFeed.Data;
using Microsoft.AspNetCore.Identity;

namespace LuminaFeed.Services.Email;

/// <summary>
/// Sends ASP.NET Core Identity account emails (confirmation, password reset) as table-based,
/// Outlook-safe HTML via <see cref="IMailSender"/>. Replaces the template's no-op sender so the
/// confirmed-account registration flow works end to end. Each email carries a plain-text
/// alternative alongside the HTML, and any value interpolated into the HTML body is HTML-encoded
/// (mirroring <see cref="EmailLayout"/>), so untrusted inputs can't inject markup.
/// </summary>
public sealed class IdentityEmailSender(IMailSender mailSender) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var encodedLink = HtmlEncoder.Default.Encode(confirmationLink);
        var body = EmailLayout.Render(
            "Confirm your email",
            "<p>Thanks for registering with LuminaFeed. Please confirm your email address to activate your account.</p>"
            + EmailLayout.Button("Confirm email", confirmationLink)
            + $"<p style=\"font-size:13px; color:#666666;\">If the button doesn't work, copy and paste this link into your browser:<br /><a href=\"{encodedLink}\">{encodedLink}</a></p>");

        var text =
            "Thanks for registering with LuminaFeed. Please confirm your email address to activate your account:\n"
            + confirmationLink;

        return mailSender.SendAsync(new EmailMessage(email, user.UserName, "Confirm your LuminaFeed account", body, text));
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var encodedLink = HtmlEncoder.Default.Encode(resetLink);
        var body = EmailLayout.Render(
            "Reset your password",
            "<p>We received a request to reset your LuminaFeed password.</p>"
            + EmailLayout.Button("Reset password", resetLink)
            + "<p style=\"font-size:13px; color:#666666;\">If you didn't request this, you can safely ignore this email.</p>");

        var text =
            "We received a request to reset your LuminaFeed password. Use the link below to reset it:\n"
            + resetLink
            + "\n\nIf you didn't request this, you can safely ignore this email.";

        return mailSender.SendAsync(new EmailMessage(email, user.UserName, "Reset your LuminaFeed password", body, text));
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var encodedCode = HtmlEncoder.Default.Encode(resetCode);
        var body = EmailLayout.Render(
            "Reset your password",
            "<p>Use the following code to reset your LuminaFeed password:</p>"
            + $"<p style=\"font-size:20px; font-weight:bold; letter-spacing:1px;\">{encodedCode}</p>");

        var text = $"Use the following code to reset your LuminaFeed password:\n{resetCode}";

        return mailSender.SendAsync(new EmailMessage(email, user.UserName, "Reset your LuminaFeed password", body, text));
    }
}
