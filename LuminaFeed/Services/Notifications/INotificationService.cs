using Ardalis.SmartEnum;
using ErrorOr;
using LuminaFeed.Domain;

namespace LuminaFeed.Services.Notifications;

/// <summary>The ways a subscriber can be alerted. A subscription enables either or both.</summary>
public sealed class NotificationChannel : SmartEnum<NotificationChannel>
{
    public static readonly NotificationChannel Email = new(nameof(Email), 1);
    public static readonly NotificationChannel Slack = new(nameof(Slack), 2);

    private NotificationChannel(string name, int value) : base(name, value)
    {
    }
}

/// <summary>New articles to tell one subscriber about. Each article should have its <see cref="Article.Feed"/> populated.</summary>
/// <param name="RecipientEmail">The subscriber's registered, verified email address (the polling result's key).</param>
public sealed record ArticleNotification(string RecipientEmail, IReadOnlyList<Article> Articles);

/// <summary>
/// Delivers new-article alerts over one <see cref="Channel"/>. The polling background service invokes the
/// implementations — individually or together — according to each subscriber's chosen channels.
/// </summary>
public interface INotificationService
{
    NotificationChannel Channel { get; }

    /// <summary>
    /// Sends the notification. Delivery problems come back as errors rather than exceptions, so one failing
    /// recipient or channel never takes the others down with it.
    /// </summary>
    Task<ErrorOr<Success>> NotifyAsync(ArticleNotification notification, CancellationToken cancellationToken = default);
}
