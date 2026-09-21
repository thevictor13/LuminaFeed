using LuminaFeed.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    // Next to the test binaries (gitignored bin/), so a test run never writes outside the repository.
    private readonly string _dbPath = Path.Combine(AppContext.BaseDirectory, $"luminafeed-test-{Guid.NewGuid():N}.db");

    /// <param name="validUnsubscribe">False blanks the HMAC secret to exercise <c>ValidateOnStart</c>.</param>
    /// <param name="seedAdmin">True seeds a confirmed admin (<see cref="AdminEmail"/>) that tests can sign in as.</param>
    public TestAppFactory(bool validUnsubscribe = true, bool seedAdmin = false)
    {
        _validUnsubscribe = validUnsubscribe;
        _seedAdmin = seedAdmin;
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
                ["AdminSeed:Email"] = _seedAdmin ? AdminEmail : "",
                ["AdminSeed:Password"] = _seedAdmin ? AdminPassword : "",
            });
        });

        // Layer onto Program's DbContext factory registration: point it at the isolated temp database
        // and ignore the PendingModelChangesWarning. MigrateAsync raises it only under
        // WebApplicationFactory's model rebuild (the real app boots cleanly, verified separately), so
        // it is a harness-only false positive here.
        builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ApplicationDbContext>(options => options
                .UseSqlite($"DataSource={_dbPath}")
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))));
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
        // Windows) until the pools are cleared.
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
        {
            try { File.Delete(path); } catch (IOException) { /* best effort */ }
        }
    }
}

/// <summary>Serialises the test classes that boot the real host (see <see cref="TestAppFactory"/>).</summary>
[CollectionDefinition(Name)]
public sealed class HostCollection
{
    public const string Name = "Host";
}
