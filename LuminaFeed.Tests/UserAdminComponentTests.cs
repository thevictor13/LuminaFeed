using System.Security.Claims;
using Bunit;
using LuminaFeed.Components.Admin;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Services.Users;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

/// <summary>
/// The interactive admin user-management page (A3), rendered with bUnit over the <b>real</b> <see cref="UserAdminService"/>
/// on in-memory SQLite: the modal confirmations the page tests (prerendered HTML only) cannot reach — removing a
/// subscription, deleting a registration, and the self-guard on the acting admin's own row. (Authorization is a routing
/// concern — <see cref="AdminPagesTests"/> covers the policy; here the page renders directly.)
/// </summary>
public sealed class UserAdminComponentTests : BunitContext
{
    private readonly SqliteTestDatabase _db = new();

    public UserAdminComponentTests() =>
        Services.AddSingleton<IUserAdminService>(new UserAdminService(_db));

    /// <summary>
    /// Renders the page as an interactive server renderer would (it declares <c>@rendermode InteractiveServer</c>).
    /// Setting the renderer info locks bUnit's service container, so it happens last.
    /// </summary>
    private IRenderedComponent<Users> RenderUsers()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return Render<Users>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _db.Dispose();
    }

    /// <summary>Seeds the signed-in admin and wires the cascading auth state the page reads its own id from.</summary>
    private ApplicationUser SignInAs(string email)
    {
        var user = _db.AddUser(email);
        AddAuthorization().SetAuthorized(email).SetClaims(new Claim(ClaimTypes.NameIdentifier, user.Id));
        return user;
    }

    private Feed AddFeed(string name) => _db.AddFeed(_db.AddCategory(name + " category").Id, name);

    private void Subscribe(string userId, Guid feedId)
    {
        using var ctx = _db.CreateDbContext();
        ctx.Subscriptions.Add(new Subscription { UserId = userId, FeedId = feedId, EmailEnabled = true });
        ctx.SaveChanges();
    }

    [Fact]
    public void Table_ListsUsersAndTheirFeeds()
    {
        SignInAs("admin@example.test");
        var alice = _db.AddUser("alice@example.test");
        Subscribe(alice.Id, AddFeed("BBC News").Id);

        var cut = RenderUsers();

        var table = cut.WaitForElement("table").TextContent;
        Assert.Contains("alice@example.test", table);
        Assert.Contains("admin@example.test", table);
        Assert.Contains("BBC News", table);
    }

    [Fact]
    public async Task RemovingASubscription_OpensAConfirm_AndDeletesIt()
    {
        SignInAs("admin@example.test");
        var alice = _db.AddUser("alice@example.test");
        Subscribe(alice.Id, AddFeed("BBC News").Id);
        var cut = RenderUsers();
        cut.WaitForElement("tbody tr");

        await cut.Find("button[aria-label='Remove BBC News subscription from alice@example.test']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Contains("Remove", cut.Find(".modal-body").TextContent));
        await cut.Find(".modal .btn-danger").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            Assert.Contains("Removed \"BBC News\" from alice@example.test.", cut.Find(".alert-success").TextContent));
        Assert.Empty(cut.FindAll(".modal"));
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Subscriptions);
    }

    [Fact]
    public async Task DeletingAUser_OpensAConfirmWithTheCount_AndRemovesTheRow()
    {
        var admin = SignInAs("admin@example.test");
        var alice = _db.AddUser("alice@example.test");
        Subscribe(alice.Id, AddFeed("BBC News").Id);
        Subscribe(alice.Id, AddFeed("DW").Id);
        var cut = RenderUsers();
        cut.WaitForElement("tbody tr");

        await cut.Find("button[aria-label='Delete user alice@example.test']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Contains("2 subscriptions", cut.Find(".modal-body").TextContent));
        await cut.Find(".modal .btn-danger").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            Assert.Contains("User \"alice@example.test\" was deleted.", cut.Find(".alert-success").TextContent));
        Assert.Empty(cut.FindAll(".modal"));
        using var ctx = _db.CreateDbContext();
        // Only the admin remains, with no orphaned subscriptions.
        Assert.Equal([admin.Id], ctx.Users.Select(u => u.Id).ToList());
        Assert.Empty(ctx.Subscriptions);
    }

    [Fact]
    public void TheDeleteButtonOnYourOwnRow_IsDisabled()
    {
        SignInAs("admin@example.test");

        var cut = RenderUsers();

        var ownDelete = cut.WaitForElement("button[aria-label='Delete user admin@example.test']");
        Assert.True(ownDelete.HasAttribute("disabled"));
    }
}
