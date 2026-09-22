using ErrorOr;

namespace LuminaFeed.Services.Users;

public static class UserErrors
{
    public static readonly Error UserNotFound =
        Error.NotFound("User.NotFound", "The user account was not found.");

    public static readonly Error SubscriptionNotFound =
        Error.NotFound("User.SubscriptionNotFound", "That subscription was not found.");

    public static readonly Error CannotDeleteSelf =
        Error.Conflict("User.CannotDeleteSelf", "You cannot delete your own account.");
}
