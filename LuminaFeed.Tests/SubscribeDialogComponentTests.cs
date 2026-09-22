using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using ErrorOr;
using LuminaFeed.Components.Shared;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Services.Subscriptions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

/// <summary>
/// The C1 subscribe dialog rendered with bUnit over the <b>real subscription service</b> on in-memory SQLite: the
/// switches, the revealed/prefilled Slack webhook, its validation, per-channel vs full unsubscribe, the in-flight
/// gate and the error alert — the interactive behaviour the host page tests cannot reach.
/// </summary>
public sealed class SubscribeDialogComponentTests : BunitContext
{
    private const string ValidWebhook = "https://hooks.slack.com/services/T000/B000/XXXXXXXX";

    private readonly SqliteTestDatabase _db = new();
    private readonly Feed _feed;
    private readonly ApplicationUser _user;

    private bool? _lastChanged;
    private int _closeCount;

    public SubscribeDialogComponentTests()
    {
        _feed = _db.AddFeed(_db.AddCategory("World News").Id, "BBC News");
        _user = _db.AddUser("alice@example.test");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _db.Dispose();
    }

    private SubscriptionService RealService() => new(_db, new SaveSubscriptionRequestValidator());

    private IRenderedComponent<SubscribeDialog> RenderDialog(ISubscriptionService? service = null, bool visible = true)
    {
        Services.AddSingleton(service ?? RealService());
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return Render<SubscribeDialog>(ps => ps
            .Add(p => p.FeedId, _feed.Id)
            .Add(p => p.FeedName, "BBC News")
            .Add(p => p.UserId, _user.Id)
            .Add(p => p.AccountEmail, _user.Email)
            .Add(p => p.Visible, visible)
            .Add(p => p.OnChanged, (bool subscribed) => _lastChanged = subscribed)
            .Add(p => p.OnClose, () => _closeCount++));
    }

    private static bool IsChecked(IElement element) => ((IHtmlInputElement)element).IsChecked;

    [Fact]
    public void NotVisible_RendersNothing()
    {
        var cut = RenderDialog(visible: false);

        Assert.Empty(cut.FindAll(".modal"));
    }

    [Fact]
    public void Open_NotSubscribed_DefaultsToEmailOn_SlackOff_NoWebhook_NoUnsubscribe()
    {
        var cut = RenderDialog();

        cut.WaitForElement(".modal");
        Assert.True(IsChecked(cut.Find("#sub-email")));
        Assert.False(IsChecked(cut.Find("#sub-slack")));
        Assert.Empty(cut.FindAll("#sub-webhook"));         // hidden until Slack is on
        Assert.Empty(cut.FindAll(".modal-footer .btn-danger")); // no full-unsubscribe for a new subscription
    }

    [Fact]
    public void PressingEscape_ClosesTheDialog()
    {
        // The dialog renders through the shared Modal, which closes on Escape (a11y parity with the admin dialogs).
        var cut = RenderDialog();
        cut.WaitForElement(".modal");

        cut.Find(".modal").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => Assert.Equal(1, _closeCount));
    }

    [Fact]
    public async Task TogglingSlackOn_RevealsTheWebhook_PrefilledFromTheUsersLastWebhook()
    {
        // A prior subscription on another feed carries the webhook the dialog should offer as the prefill.
        var other = _db.AddFeed(_db.AddCategory("Tech").Id, "DW");
        await RealService().SaveSubscriptionAsync(_user.Id, other.Id, new SaveSubscriptionRequest(true, true, ValidWebhook));

        var cut = RenderDialog();
        cut.WaitForElement(".modal");
        cut.Find("#sub-slack").Change(true);

        var webhook = cut.WaitForElement("#sub-webhook");
        Assert.Equal(ValidWebhook, ((IHtmlInputElement)webhook).Value);
    }

    [Fact]
    public async Task Save_EmailOnly_Persists_FiresOnChanged_AndCloses()
    {
        var cut = RenderDialog();
        cut.WaitForElement(".modal");

        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            using var ctx = _db.CreateDbContext();
            var row = Assert.Single(ctx.Subscriptions);
            Assert.True(row.EmailEnabled);
            Assert.False(row.SlackEnabled);
        });
        Assert.True(_lastChanged);
        Assert.Equal(1, _closeCount);
    }

    [Fact]
    public async Task Save_WithSlackAndAValidWebhook_PersistsBothChannels()
    {
        var cut = RenderDialog();
        cut.WaitForElement(".modal");
        cut.Find("#sub-slack").Change(true);
        cut.WaitForElement("#sub-webhook").Change(ValidWebhook);

        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            using var ctx = _db.CreateDbContext();
            var row = Assert.Single(ctx.Subscriptions);
            Assert.True(row.SlackEnabled);
            Assert.Equal(ValidWebhook, row.SlackWebhookUrl);
        });
    }

    [Fact]
    public async Task Save_WithSlackAndABadWebhook_ShowsTheValidationMessage_AndPersistsNothing()
    {
        var cut = RenderDialog();
        cut.WaitForElement(".modal");
        cut.Find("#sub-slack").Change(true);
        cut.WaitForElement("#sub-webhook").Change("https://example.com/not-slack");

        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() =>
            Assert.Contains("Slack incoming webhook", cut.Find(".modal .alert-danger").TextContent));
        Assert.Null(_lastChanged);
        using var check = _db.CreateDbContext();
        Assert.Empty(check.Subscriptions);
    }

    [Fact]
    public async Task Save_BadWebhookTypedThenSlackToggledOff_PersistsEmailOnly_WithoutTheWebhook()
    {
        var cut = RenderDialog();
        cut.WaitForElement(".modal");
        cut.Find("#sub-slack").Change(true);
        cut.WaitForElement("#sub-webhook").Change("https://example.com/not-slack");
        cut.Find("#sub-slack").Change(false);   // toggle Slack back off — the typed webhook is now hidden

        await cut.Find(".modal form").SubmitAsync();

        // The hidden bad value is never submitted, so the save succeeds as email-only rather than erroring.
        cut.WaitForAssertion(() =>
        {
            using var ctx = _db.CreateDbContext();
            var row = Assert.Single(ctx.Subscriptions);
            Assert.True(row.EmailEnabled);
            Assert.False(row.SlackEnabled);
            Assert.Null(row.SlackWebhookUrl);
        });
        Assert.True(_lastChanged);
    }

    [Fact]
    public async Task PerChannelUnsubscribe_TurningEmailOff_KeepsTheSlackSubscription()
    {
        await RealService().SaveSubscriptionAsync(_user.Id, _feed.Id, new SaveSubscriptionRequest(true, true, ValidWebhook));

        var cut = RenderDialog();
        cut.WaitForAssertion(() => Assert.True(IsChecked(cut.Find("#sub-email"))));
        cut.Find("#sub-email").Change(false);   // per-channel unsubscribe: drop email, keep Slack

        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            using var ctx = _db.CreateDbContext();
            var row = Assert.Single(ctx.Subscriptions);
            Assert.False(row.EmailEnabled);
            Assert.True(row.SlackEnabled);
        });
        Assert.True(_lastChanged); // still subscribed (via Slack)
    }

    [Fact]
    public async Task DialogUnsubscribe_RemovesTheSubscription_AndReportsUnsubscribed()
    {
        await RealService().SaveSubscriptionAsync(_user.Id, _feed.Id, new SaveSubscriptionRequest(true, false));

        var cut = RenderDialog();
        var remove = cut.WaitForElement(".modal-footer .btn-danger");
        await remove.ClickAsync(new());

        cut.WaitForAssertion(() =>
        {
            using var ctx = _db.CreateDbContext();
            Assert.Empty(ctx.Subscriptions);
        });
        Assert.False(_lastChanged);
        Assert.Equal(1, _closeCount);
    }

    [Fact]
    public async Task WhileSaving_TheSaveButtonIsDisabled()
    {
        var gated = new GatedSubscriptionService(RealService());
        var cut = RenderDialog(gated);
        cut.WaitForElement(".modal");

        var submit = cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.True(cut.Find(".modal-footer .btn-primary").HasAttribute("disabled")));
        gated.Release();
        await submit;
        Assert.True(_lastChanged);
    }

    [Fact]
    public async Task WhenSaveFails_TheErrorIsShown()
    {
        var cut = RenderDialog(new SaveFailingSubscriptionService("The database is on fire."));
        cut.WaitForElement(".modal");

        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() =>
            Assert.Contains("The database is on fire.", cut.Find(".modal .alert-danger").TextContent));
        Assert.Null(_lastChanged);
    }

    /// <summary>Passes reads through to the real service but holds every save until released.</summary>
    private sealed class GatedSubscriptionService(ISubscriptionService inner) : ISubscriptionService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.SetResult();

        public Task<IReadOnlySet<Guid>> GetSubscribedFeedIdsAsync(string? userId, CancellationToken cancellationToken = default) =>
            inner.GetSubscribedFeedIdsAsync(userId, cancellationToken);

        public Task<ErrorOr<SubscriptionState>> GetSubscriptionForEditAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            inner.GetSubscriptionForEditAsync(userId, feedId, cancellationToken);

        public async Task<ErrorOr<Success>> SaveSubscriptionAsync(string? userId, Guid feedId, SaveSubscriptionRequest request, CancellationToken cancellationToken = default)
        {
            await _gate.Task;
            return await inner.SaveSubscriptionAsync(userId, feedId, request, cancellationToken);
        }

        public Task<ErrorOr<Success>> SubscribeByEmailAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            inner.SubscribeByEmailAsync(userId, feedId, cancellationToken);

        public Task<ErrorOr<Deleted>> UnsubscribeAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            inner.UnsubscribeAsync(userId, feedId, cancellationToken);
    }

    /// <summary>Opens with clean defaults so the dialog renders, then fails the save so the error path is exercised.</summary>
    private sealed class SaveFailingSubscriptionService(string reason) : ISubscriptionService
    {
        public Task<IReadOnlySet<Guid>> GetSubscribedFeedIdsAsync(string? userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<ErrorOr<SubscriptionState>> GetSubscriptionForEditAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ErrorOr<SubscriptionState>>(new SubscriptionState(Exists: false, EmailEnabled: true, SlackEnabled: false, WebhookPrefill: null));

        public Task<ErrorOr<Success>> SaveSubscriptionAsync(string? userId, Guid feedId, SaveSubscriptionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<ErrorOr<Success>>(ErrorOr.Error.Failure("Subscription.Failed", reason));

        public Task<ErrorOr<Success>> SubscribeByEmailAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ErrorOr<Success>>(ErrorOr.Error.Failure("Subscription.Failed", reason));

        public Task<ErrorOr<Deleted>> UnsubscribeAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ErrorOr<Deleted>>(ErrorOr.Error.Failure("Subscription.Failed", reason));
    }
}
