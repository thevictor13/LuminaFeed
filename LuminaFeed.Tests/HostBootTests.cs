using LuminaFeed.Data;
using LuminaFeed.Services.Categories;
using LuminaFeed.Services.Email;
using LuminaFeed.Services.Feeds;
using LuminaFeed.Services.Polling;
using LuminaFeed.Services.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Tests;

/// <summary>
/// Boots the real <c>Program</c> DI graph (against an isolated temp SQLite database) to verify that
/// registrations resolve, migrations + seeding run, and the options <c>ValidateOnStart</c> pipeline
/// behaves as configured. This is the genuine integration smoke the old placeholder <c>SmokeTests</c>
/// never provided. Host-booting tests run sequentially (see <see cref="HostCollection"/>), so the
/// connection-string environment variable used to point <c>Program</c> at the temp database does not race.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class HostBootTests
{
    [Fact]
    public void Host_WithValidConfig_BootsAndResolvesDiGraph()
    {
        using var factory = new TestAppFactory(validUnsubscribe: true);

        // Forcing the server to start runs migrations, seeding, and ValidateOnStart.
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMailSender>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEmailSender<ApplicationUser>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICategoryService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFeedService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISubscriptionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFeedPollingService>());

        // The polling loop is hosted, and running.
        var polling = Assert.Single(factory.Services.GetServices<IHostedService>().OfType<FeedPollingBackgroundService>());
        Assert.NotNull(polling.ExecuteTask);
        Assert.False(polling.ExecuteTask.IsFaulted);

        // Seeding ran against the isolated database.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(db.Feeds.Any());
    }

    [Fact]
    public async Task Host_ServicesUseTheFactoryBackedDatabase()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        // The context factory (used by application services) and the scoped context (used by Identity
        // and the seeders) must point at the same seeded database.
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Equal(115, await db.Feeds.CountAsync());

        var categories = await scope.ServiceProvider.GetRequiredService<ICategoryService>().ListAsync();
        Assert.Equal(10, categories.Count);
        Assert.Equal(115, categories.Sum(c => c.FeedCount));
    }

    [Fact]
    public void Host_WithMissingHmacSecret_FailsValidateOnStart()
    {
        using var factory = new TestAppFactory(validUnsubscribe: false);

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.True(
            ex is OptionsValidationException || ex.InnerException is OptionsValidationException,
            $"Expected an OptionsValidationException, got {ex.GetType().Name}: {ex.Message}");
    }
}
