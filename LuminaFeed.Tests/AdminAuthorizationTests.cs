using System.Security.Claims;
using LuminaFeed.Authorization;
using LuminaFeed.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

public class AdminAuthorizationTests
{
    [Fact]
    public void ClaimsFor_AdminUser_IncludesAdminClaim()
    {
        var claims = AdminAuthorization.ClaimsFor(new ApplicationUser { IsAdmin = true }).ToList();

        Assert.Single(claims);
        Assert.Equal(AdminAuthorization.ClaimType, claims[0].Type);
        Assert.Equal(AdminAuthorization.ClaimValue, claims[0].Value);
    }

    [Fact]
    public void ClaimsFor_StandardUser_IsEmpty()
    {
        Assert.Empty(AdminAuthorization.ClaimsFor(new ApplicationUser { IsAdmin = false }));
    }

    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminAuthorization.PolicyName, policy =>
                policy.RequireClaim(AdminAuthorization.ClaimType, AdminAuthorization.ClaimValue));
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Fact]
    public async Task AdminPolicy_Succeeds_ForPrincipalWithAdminClaim()
    {
        var auth = BuildAuthorizationService();
        var principal = PrincipalWith(new Claim(AdminAuthorization.ClaimType, AdminAuthorization.ClaimValue));

        var result = await auth.AuthorizeAsync(principal, AdminAuthorization.PolicyName);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AdminPolicy_Fails_ForPrincipalWithoutAdminClaim()
    {
        var auth = BuildAuthorizationService();
        var principal = PrincipalWith(new Claim(ClaimTypes.Name, "alice"));

        var result = await auth.AuthorizeAsync(principal, AdminAuthorization.PolicyName);

        Assert.False(result.Succeeded);
    }
}
