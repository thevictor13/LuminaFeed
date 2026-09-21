using LuminaFeed.Data;

namespace LuminaFeed.Domain;

/// <summary>
/// A user's subscription to a <see cref="Feed"/>, with the chosen notification channels. One row per
/// (user, feed). Email uses the user's verified address; Slack posts to <see cref="SlackWebhookUrl"/>.
/// </summary>
public sealed class Subscription : EntityBase
{
    /// <summary>FK to the Identity user (<see cref="ApplicationUser.Id"/>, a string key).</summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid FeedId { get; set; }
    public Feed Feed { get; set; } = null!;

    public bool EmailEnabled { get; set; }

    public bool SlackEnabled { get; set; }

    /// <summary>Slack incoming-webhook URL; required when <see cref="SlackEnabled"/> is set.</summary>
    public string? SlackWebhookUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
