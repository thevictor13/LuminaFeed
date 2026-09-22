using LuminaFeed.Data;

namespace LuminaFeed.Domain;

/// <summary>
/// A user's subscription to a <see cref="Feed"/>, with the chosen notification channels. One row per
/// (user, feed). Email uses the user's verified address; Slack posts to <see cref="SlackWebhookUrl"/>.
/// </summary>
public sealed class Subscription : EntityBase
{
    // Column limits: the single source for the EF configuration and (C1) the subscribe-dialog validator.
    /// <summary>Matches ASP.NET Identity's AspNetUsers.Id key width.</summary>
    public const int UserIdMaxLength = 450;
    public const int SlackWebhookUrlMaxLength = 2048;

    /// <summary>
    /// The prefix every genuine Slack incoming webhook starts with. The single source for the C1 subscribe-dialog
    /// validator's <see cref="SlackWebhookUrl"/> check.
    /// </summary>
    public const string SlackWebhookUrlPrefix = "https://hooks.slack.com/services";

    /// <summary>FK to the Identity user (<see cref="ApplicationUser.Id"/>, a string key).</summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid FeedId { get; set; }
    public Feed Feed { get; set; } = null!;

    public bool EmailEnabled { get; set; }

    public bool SlackEnabled { get; set; }

    /// <summary>Slack incoming-webhook URL; required when <see cref="SlackEnabled"/> is set.</summary>
    public string? SlackWebhookUrl { get; set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
