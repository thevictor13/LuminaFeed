using LuminaFeed.Data;
using LuminaFeed.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LuminaFeed.Tests;

/// <summary>
/// A real in-memory SQLite database (kept-open connection, schema from the EF model) exposed as the
/// <see cref="IDbContextFactory{TContext}"/> the application services depend on. Every context shares
/// the one connection, so data written through a service is visible to the asserting test.
/// </summary>
internal sealed class SqliteTestDatabase : IDbContextFactory<ApplicationDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();

    // --- Arrange helpers -------------------------------------------------------------------------

    public Category AddCategory(string name = "World News", string? description = null)
    {
        var category = new Category { Name = name, Description = description };
        using var ctx = CreateDbContext();
        ctx.Categories.Add(category);
        ctx.SaveChanges();
        return category;
    }

    public Feed AddFeed(Guid categoryId, string name = "BBC News", string? feedUrl = null, int popularity = 0)
    {
        var feed = new Feed
        {
            Name = name,
            CategoryId = categoryId,
            FeedUrl = feedUrl ?? $"https://feeds.example.test/{Guid.NewGuid():N}.xml",
            SiteUrl = "https://www.example.test",
            Popularity = popularity,
        };
        using var ctx = CreateDbContext();
        ctx.Feeds.Add(feed);
        ctx.SaveChanges();
        return feed;
    }

    public ApplicationUser AddUser(string email = "alice@example.test")
    {
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
        };
        using var ctx = CreateDbContext();
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }
}
