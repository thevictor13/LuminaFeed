using System.Security.Claims;
using LuminaFeed.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Authorization;

/// <summary>
/// Adds the admin claim (from <see cref="ApplicationUser.IsAdmin"/>) to the user's principal so the
/// "Admin" policy can be evaluated without a database hit on every authorization check.
/// </summary>
public sealed class AdminClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaims(AdminAuthorization.ClaimsFor(user));
        return identity;
    }
}
