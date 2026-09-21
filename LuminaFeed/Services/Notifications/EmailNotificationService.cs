using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using ErrorOr;
using LuminaFeed.Domain;
using LuminaFeed.Services.Email;

namespace LuminaFeed.Services.Notifications;

/// <summary>
/// Emails a subscriber their new articles through <see cref="IMailSender"/> (MailKit). Walking-skeleton scope:
/// one plain digest per polling pass, grouped by feed, in the table-based <see cref="EmailLayout"/>. Richer
/// article templates and Outlook buttons arrive with C3; the RFC 8058 unsubscribe link and headers with C5.
/// </summary>
public sealed class EmailNotificationService(IMailSender mailSender) : INotificationService
{
    public const int SummaryMaxLength = 300;

    private const string UnknownFeedName = "LuminaFeed";

    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<ErrorOr<Success>> NotifyAsync(
        ArticleNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.Articles.Count == 0)
            return Result.Success;

        var message = Compose(notification);
        try
        {
            await mailSender.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not logged here: the transport logs its own failure in detail, and the caller reports this error.
            return Error.Failure(
                "Notification.EmailFailed",
                $"The email to '{notification.RecipientEmail}' could not be sent: {ex.Message}");
        }

        return Result.Success;
    }

    /// <summary>Builds the digest. Everything that comes from a feed is HTML-encoded — titles, summaries and links are untrusted.</summary>
    internal static EmailMessage Compose(ArticleNotification notification)
    {
        var groups = notification.Articles
            .GroupBy(FeedName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var subject = notification.Articles.Count == 1
            ? $"New from {groups[0].Key}: {notification.Articles[0].Title}"
            : groups.Count == 1
                ? $"{notification.Articles.Count} new articles from {groups[0].Key}"
                : $"{notification.Articles.Count} new articles from {groups.Count} feeds";

        var html = new StringBuilder();
        var text = new StringBuilder();
        html.Append("<p>There is something new in the feeds you follow on LuminaFeed.</p>");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");

        foreach (var group in groups)
        {
            html.Append("<tr><td style=\"padding:16px 0 6px 0; font-family:Arial, sans-serif; font-size:17px; font-weight:bold; color:#111111; border-bottom:1px solid #e5e5e5;\">")
                .Append(Encode(group.Key))
                .Append("</td></tr>");
            text.AppendLine(group.Key).AppendLine(new string('-', Math.Min(group.Key.Length, 60)));

            foreach (var article in group)
            {
                var published = article.PublishedAt?.ToUniversalTime().ToString("d MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
                var summary = Shorten(article.Summary);

                html.Append("<tr><td style=\"padding:12px 0; font-family:Arial, sans-serif; font-size:15px; line-height:22px; color:#333333;\">")
                    .Append("<a href=\"").Append(Encode(article.Link)).Append("\" style=\"color:#0d6efd; font-size:16px; font-weight:bold; text-decoration:none;\">")
                    .Append(Encode(article.Title)).Append("</a>");
                if (published is not null)
                    html.Append("<br /><span style=\"font-size:12px; color:#999999;\">").Append(published).Append("</span>");
                if (summary is not null)
                    html.Append("<br />").Append(Encode(summary));
                html.Append("</td></tr>");

                text.AppendLine(article.Title);
                if (published is not null)
                    text.AppendLine(published);
                if (summary is not null)
                    text.AppendLine(summary);
                text.AppendLine(article.Link).AppendLine();
            }
        }

        html.Append("</table>");
        html.Append("<p style=\"font-size:12px; color:#999999;\">You are receiving this because you subscribed to these feeds on LuminaFeed. ")
            .Append("To stop, sign in and press Unsubscribe on the feed.</p>");
        text.AppendLine("You are receiving this because you subscribed to these feeds on LuminaFeed.")
            .AppendLine("To stop, sign in and press Unsubscribe on the feed.");

        return new EmailMessage(
            notification.RecipientEmail,
            ToName: null,
            subject,
            EmailLayout.Render(notification.Articles.Count == 1 ? "New article" : "New articles", html.ToString()),
            text.ToString());
    }

    private static string FeedName(Article article) =>
        // Feed is a navigation the caller is expected to populate; don't fail a delivery over a missing name.
        string.IsNullOrWhiteSpace(article.Feed?.Name) ? UnknownFeedName : article.Feed.Name;

    private static string? Shorten(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return null;

        summary = summary.Trim();
        return summary.Length <= SummaryMaxLength ? summary : summary[..(SummaryMaxLength - 1)].TrimEnd() + "…";
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
