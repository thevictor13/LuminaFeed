using LuminaFeed.Data;
using LuminaFeed.Data.Seed;
using LuminaFeed.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Tests;

/// <summary>Exercises the bootstrap admin seeder against a real Identity stack over in-memory SQLite.</summary>
public sealed class AdminUserSeederTests : IDisposable
{
    private const string Password = "ChangeMe!123";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public AdminUserSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task RunSeederAsync(AdminSeedOptions options)
    {
        using var scope = _provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await new AdminUserSeeder(
                userManager,
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<AdminUserSeeder>.Instance)
            .SeedAsync();
    }

    private async Task<ApplicationUser?> FindAsync(string email)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private int UserCount()
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().Users.Count();
    }

    [Fact]
    public async Task Seed_WhenConfigured_CreatesConfirmedAdmin()
    {
        await RunSeederAsync(new AdminSeedOptions { Email = "admin@luminafeed.local", Password = Password });

        var user = await FindAsync("admin@luminafeed.local");
        Assert.NotNull(user);
        Assert.True(user!.IsAdmin);
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public async Task Seed_WhenNotConfigured_CreatesNoUser()
    {
        await RunSeederAsync(new AdminSeedOptions());
        Assert.Equal(0, UserCount());
    }

    [Fact]
    public async Task Seed_PromotesExistingUserToAdmin()
    {
        const string email = "person@luminafeed.local";
        using (var scope = _provider.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await userManager.CreateAsync(
                new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true }, Password);
            Assert.True(created.Succeeded);
        }

        await RunSeederAsync(new AdminSeedOptions { Email = email, Password = Password });

        var user = await FindAsync(email);
        Assert.NotNull(user);
        Assert.True(user!.IsAdmin);
    }

    [Fact]
    public async Task Seed_IsIdempotent()
    {
        var options = new AdminSeedOptions { Email = "admin@luminafeed.local", Password = Password };
        await RunSeederAsync(options);
        await RunSeederAsync(options);

        Assert.Equal(1, UserCount());
    }
}
