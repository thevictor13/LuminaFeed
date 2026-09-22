using LuminaFeed.Data;
using LuminaFeed.Services.Email;
using LuminaFeed.Services.Polling;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LuminaFeed.Tests;

/// <summary>
/// Boots the real <c>Program</c> against an isolated temp SQLite database. The connection string is
/// handed to <c>Program</c> through a process-wide environment variable, so every test class using this
/// factory must join <see cref="HostCollection"/> to run sequentially.
/// </summary>
internal sealed class TestAppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@luminafeed.test";
    public const string AdminPassword = "ChangeMe!123";

    private readonly bool _validUnsubscribe;
    private readonly bool _seedAdmin;
    private readonly bool _hostPollingLoop;
    private readonly Action<IServiceCollection>? _configureServices;

    // Next to the test binaries (gitignored bin/), so a test run never writes outside the repository.
    private readonly string _dbPath = Path.Combine(AppContext.BaseDirectory, $"luminafeed-test-{Guid.NewGuid():N}.db");

    /// <param name="validUnsubscribe">False blanks the HMAC secret to exercise <c>ValidateOnStart</c>.</param>
    /// <param name="seedAdmin">True seeds a confirmed admin (<see cref="AdminEmail"/>) that tests can sign in as.</param>
    /// <param name="hostPollingLoop">
    /// True keeps the real <see cref="FeedPollingBackgroundService"/> hosted (to prove it is). Off by default: a live
    /// loop polls the same database the test is driving, and would race a test's own polling cycles for the
    /// "first poll" of a feed the test just subscribed to.
    /// </param>
    /// <param name="configureServices">Extra test doubles, applied after the defaults below.</param>
    public TestAppFactory(
        bool validUnsubscribe = true,
        bool seedAdmin = false,
        bool hostPollingLoop = false,
        Action<IServiceCollection>? configureServices = null)
    {
        _validUnsubscribe = validUnsubscribe;
        _seedAdmin = seedAdmin;
        _hostPollingLoop = hostPollingLoop;
        _configureServices = configureServices;
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
                // A day: even when the loop is hosted, no tick lands during a test.
                ["Polling:IntervalSeconds"] = "86400",
                ["Unsubscribe:HmacSecret"] = _validUnsubscribe ? "test-secret-0123456789" : "",
                ["AdminSeed:Email"] = _seedAdmin ? AdminEmail : "",
                ["AdminSeed:Password"] = _seedAdmin ? AdminPassword : "",
            });
        });

        // Layer onto Program's DbContext factory registration: point it at the isolated temp database
        // and ignore the PendingModelChangesWarning. MigrateAsync raises it only under
        // WebApplicationFactory's model rebuild (the real app boots cleanly, verified separately), so
        // it is a harness-only false positive here.
        builder.ConfigureTestServices(services =>
        {
            services.ConfigureDbContext<ApplicationDbContext>(options => options
                .UseSqlite($"DataSource={_dbPath}")
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

            if (!_hostPollingLoop)
            {
                // Only this descriptor goes; the web host's own hosted service stays.
                services.Remove(services.Single(d =>
                    d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(FeedPollingBackgroundService)));
            }

            // Whatever polls in this host must never reach the real catalogue's publishers.
            services.RemoveAll<IFeedFetcher>();
            services.AddSingleton<IFeedFetcher, NoNetworkFeedFetcher>();

            // ...and nothing may try to reach an SMTP server either.
            services.RemoveAll<IMailSender>();
            services.AddSingleton<IMailSender, RecordingMailSender>();

            _configureServices?.Invoke(services);
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

        // Microsoft.Data.Sqlite pools connections, which keeps the file locked (and undeletable on
        // Windows) until this database's pool is cleared.
        SqliteConnection.ClearPool(new SqliteConnection($"DataSource={_dbPath}"));
        foreach (var path in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
        {
            try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }
}

/// <summary>Serialises the test classes that boot the real host (see <see cref="TestAppFactory"/>).</summary>
[CollectionDefinition(Name)]
public sealed class HostCollection
{
    public const string Name = "Host";
}
