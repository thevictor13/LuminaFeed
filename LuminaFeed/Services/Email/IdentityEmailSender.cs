using LuminaFeed.Data;
using Microsoft.AspNetCore.Identity;

namespace LuminaFeed.Services.Email;

/// <summary>
/// Sends ASP.NET Core Identity account emails (confirmation, password reset) as table-based,
/// Outlook-safe HTML via <see cref="IMailSender"/>. Replaces the template's no-op sender so the
/// confirmed-account registration flow works end to end.
/// </summary>
public sealed class IdentityEmailSender(IMailSender mailSender) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var body = EmailLayout.Render(
            "Confirm your email",
            "<p>Thanks for registering with LuminaFeed. Please confirm your email address to activate your account.</p>"
            + EmailLayout.Button("Confirm email", confirmationLink)
            + $"<p style=\"font-size:13px; color:#666666;\">If the button doesn't work, copy and paste this link into your browser:<br /><a href=\"{confirmationLink}\">{confirmationLink}</a></p>");

        return mailSender.SendAsync(new EmailMessage(email, user.UserName, "Confirm your LuminaFeed account", body));
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var body = EmailLayout.Render(
            "Reset your password",
            "<p>We received a request to reset your LuminaFeed password.</p>"
            + EmailLayout.Button("Reset password", resetLink)
            + "<p style=\"font-size:13px; color:#666666;\">If you didn't request this, you can safely ignore this email.</p>");

        return mailSender.SendAsync(new EmailMessage(email, user.UserName, "Reset your LuminaFeed password", body));
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var body = EmailLayout.Render(
            "Reset your password",
            "<p>Use the following code to reset your LuminaFeed password:</p>"
            + $"<p style=\"font-size:20px; font-weight:bold; letter-spacing:1px;\">{resetCode}</p>");

        return mailSender.SendAsync(new EmailMessage(email, user.UserName, "Reset your LuminaFeed password", body));
    }
}
