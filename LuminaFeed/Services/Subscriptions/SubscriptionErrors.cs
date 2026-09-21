using ErrorOr;

namespace LuminaFeed.Services.Subscriptions;

public static class SubscriptionErrors
{
    public static readonly Error UserRequired =
        Error.Validation("Subscription.UserRequired", "A signed-in user is required to manage subscriptions.");

    public static readonly Error UserNotFound =
        Error.NotFound("Subscription.UserNotFound", "The user account was not found.");

    public static readonly Error NotSubscribed =
        Error.NotFound("Subscription.NotSubscribed", "You are not subscribed to this feed.");
}
