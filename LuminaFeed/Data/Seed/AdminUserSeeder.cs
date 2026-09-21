using LuminaFeed.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Data.Seed;

/// <summary>
/// Ensures a bootstrap admin account exists so the admin area is reachable on a fresh database
/// (G0.7 follow-up). Idempotent: when <see cref="AdminSeedOptions"/> is configured it creates the
/// admin (with a pre-confirmed email) if missing, or promotes an existing user to admin; it does
/// nothing when unconfigured.
/// </summary>
internal sealed class AdminUserSeeder(
    UserManager<ApplicationUser> userManager,
    IOptions<AdminSeedOptions> options,
    ILogger<AdminUserSeeder> logger)
{
    public async Task SeedAsync()
    {
        var opts = options.Value;
        if (!opts.IsConfigured)
        {
            logger.LogInformation("Admin seed skipped: AdminSeed:Email/Password not configured.");
            return;
        }

        var email = opts.Email!;

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (!existing.IsAdmin)
            {
                existing.IsAdmin = true;
                await userManager.UpdateAsync(existing);
                logger.LogInformation("Promoted existing user '{Email}' to admin.", email);
            }
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsAdmin = true,
        };

        var result = await userManager.CreateAsync(user, opts.Password!);
        if (result.Succeeded)
        {
            logger.LogInformation("Seeded admin user '{Email}'.", email);
        }
        else
        {
            logger.LogError("Failed to seed admin user '{Email}': {Errors}",
                email, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
