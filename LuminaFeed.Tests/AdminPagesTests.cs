using System.Net;
using System.Reflection;
using LuminaFeed.Authorization;
using LuminaFeed.Components.Admin;
using LuminaFeed.Services.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

/// <summary>The admin pages must sit behind the <c>Admin</c> policy.</summary>
[Collection(HostCollection.Name)]
public sealed class AdminPagesTests
{
    [Theory]
    [InlineData(typeof(AdminHome), "/admin")]
    [InlineData(typeof(Categories), "/admin/categories")]
    [InlineData(typeof(Feeds), "/admin/feeds")]
    [InlineData(typeof(Users), "/admin/users")]
    public void AdminPage_IsRouted_AndRequiresTheAdminPolicy(Type page, string route)
    {
        Assert.Contains(page.GetCustomAttributes<RouteAttribute>(), r => r.Template == route);

        var authorize = Assert.Single(page.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AdminAuthorization.PolicyName, authorize.Policy);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/categories")]
    [InlineData("/admin/feeds")]
    [InlineData("/admin/users")]
    public async Task AdminPage_AnonymousRequest_IsRedirectedToLogin(string route)
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/Account/Login", location);
        Assert.Contains("ReturnUrl=" + Uri.EscapeDataString(route), location);
    }

    [Fact]
    public async Task AdminPages_SignedInAdmin_RenderTheSeededCatalogue()
    {
        using var factory = new TestAppFactory(seedAdmin: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestSignIn.SignInAsync(client, TestAppFactory.AdminEmail, TestAppFactory.AdminPassword);
        CategorySummary worldNews;
        using (var scope = factory.Services.CreateScope())
        {
            var listed = await scope.ServiceProvider.GetRequiredService<ICategoryService>().ListAsync();
            worldNews = listed.Single(c => c.Name == "World News");
        }

        // Prerendered output proves the pages resolve their services and query the real database.
        var categories = await client.GetStringAsync("/admin/categories");
        Assert.Contains("Add a category", categories);
        Assert.Contains("World News", categories);

        var feeds = await client.GetStringAsync("/admin/feeds");
        Assert.Contains("Add a feed", feeds);
        // The category dropdown is populated from the seeded categories.
        Assert.Contains($"<option value=\"{worldNews.Id}\">World News</option>", feeds);
        Assert.Contains("BBC News", feeds);
    }

    [Fact]
    public async Task UsersPage_SignedInAdmin_ListsTheSignedInUser()
    {
        using var factory = new TestAppFactory(seedAdmin: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestSignIn.SignInAsync(client, TestAppFactory.AdminEmail, TestAppFactory.AdminPassword);

        // Prerendered output proves the page resolves IUserAdminService and queries the real database.
        var users = await client.GetStringAsync("/admin/users");
        Assert.Contains(TestAppFactory.AdminEmail, users);
    }
}
