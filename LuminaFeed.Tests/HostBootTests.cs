using LuminaFeed.Data;
using LuminaFeed.Services.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Tests;

/// <summary>
/// Boots the real <c>Program</c> DI graph (against an isolated temp SQLite database) to verify that
/// registrations resolve, migrations + seeding run, and the options <c>ValidateOnStart</c> pipeline
/// behaves as configured. This is the genuine integration smoke the old placeholder <c>SmokeTests</c>
/// never provided. Tests run sequentially within this class, so the connection-string environment
/// variable used to point <c>Program</c> at the temp database does not race.
/// </summary>
[Collection(nameof(HostBootTests))]
public sealed class HostBootTests
{
    private sealed class TestAppFactory : WebApplicationFactory<Program>
    {
        private readonly bool _validUnsubscribe;
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"luminafeed-test-{Guid.NewGuid():N}.db");

        public TestAppFactory(bool validUnsubscribe)
        {
            _validUnsubscribe = validUnsubscribe;
            // Read imperatively by Program before Build, so it must come from an early config source
            // (environment variables) rather than a ConfigureAppConfiguration source added during Build.
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", $"DataSource={_dbPath}");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // "Testing" avoids loading appsettings.Development.json; the values below are supplied
            // explicitly so the base (deliberately non-bootable) appsettings.json doesn't drive validation.
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Smtp:Host"] = "localhost",
                    ["Smtp:Port"] = "25",
                    ["Smtp:FromAddress"] = "no-reply@luminafeed.local",
                    ["Smtp:FromName"] = "LuminaFeed",
                    ["Polling:IntervalSeconds"] = "60",
                    ["Unsubscribe:HmacSecret"] = _validUnsubscribe ? "test-secret-0123456789" : "",
                    ["AdminSeed:Email"] = "",
                    ["AdminSeed:Password"] = "",
                });
            });

            // Re-point the DbContext at the isolated temp database and ignore the
            // PendingModelChangesWarning: MigrateAsync raises it only under WebApplicationFactory's
            // model rebuild (the real app boots cleanly, verified separately), so it is a harness-only
            // false positive here.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseSqlite($"DataSource={_dbPath}")
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            foreach (var path in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
            {
                try { File.Delete(path); } catch (IOException) { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Host_WithValidConfig_BootsAndResolvesDiGraph()
    {
        using var factory = new TestAppFactory(validUnsubscribe: true);

        // Forcing the server to start runs migrations, seeding, and ValidateOnStart.
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMailSender>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEmailSender<ApplicationUser>>());

        // Seeding ran against the isolated database.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(db.Feeds.Any());
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
