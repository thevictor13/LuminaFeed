using FluentValidation;
using LuminaFeed.Domain;

namespace LuminaFeed.Services.Subscriptions;

/// <summary>
/// The channel choices captured by the C1 subscribe dialog for one (user, feed): which channels are on, and the
/// Slack webhook when Slack is on. Email always targets the user's verified account address, so it carries no value.
/// </summary>
public sealed record SaveSubscriptionRequest(bool EmailEnabled, bool SlackEnabled, string? SlackWebhookUrl = null);

public sealed class SaveSubscriptionRequestValidator : AbstractValidator<SaveSubscriptionRequest>
{
    public SaveSubscriptionRequestValidator()
    {
        // A subscription with no channels is not a subscription — the caller unsubscribes instead.
        RuleFor(r => r.EmailEnabled)
            .Must((r, _) => r.EmailEnabled || r.SlackEnabled)
            .WithMessage("Enable at least one notification channel, or unsubscribe.");

        // The webhook only matters when Slack is on. Stop at the first failure so an empty field isn't also
        // reported as "not a Slack hook". Length is the entity's own column limit, so it can't drift from the schema.
        RuleFor(r => r.SlackWebhookUrl).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A Slack webhook URL is required when Slack notifications are on.")
            .MaximumLength(Subscription.SlackWebhookUrlMaxLength)
            .Must(BeSlackWebhook)
            .WithMessage($"The webhook must be a Slack incoming webhook (it must start with {Subscription.SlackWebhookUrlPrefix}).")
            .When(r => r.SlackEnabled);
    }

    /// <summary>A genuine Slack incoming webhook: an absolute https URL under the Slack services host.</summary>
    internal static bool BeSlackWebhook(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && url!.StartsWith(Subscription.SlackWebhookUrlPrefix, StringComparison.Ordinal);
}
