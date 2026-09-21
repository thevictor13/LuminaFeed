using System.Security.Claims;
using LuminaFeed.Data;

namespace LuminaFeed.Authorization;

/// <summary>
/// Central definition of the admin authorization boundary: the policy name, the backing claim, and
/// the mapping from <see cref="ApplicationUser.IsAdmin"/> to that claim.
/// </summary>
public static class AdminAuthorization
{
    public const string PolicyName = "Admin";
    public const string ClaimType = "IsAdmin";
    public const string ClaimValue = "true";

    /// <summary>The extra claims an admin user's principal should carry (empty for standard users).</summary>
    public static IEnumerable<Claim> ClaimsFor(ApplicationUser user) =>
        user.IsAdmin ? [new Claim(ClaimType, ClaimValue)] : [];
}
