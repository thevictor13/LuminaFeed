using ErrorOr;
using LuminaFeed.Services.Email;
using LuminaFeed.Services.Notifications;

namespace LuminaFeed.Tests;

/// <summary>Records what would have been emailed; optionally fails like a dead SMTP server.</summary>
internal sealed class RecordingMailSender : IMailSender
{
    private readonly List<EmailMessage> _sent = [];

    public Exception? FailWith { get; set; }

    /// <summary>Recipients for which sending fails (others succeed).</summary>
    public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<EmailMessage> Sent
    {
        get { lock (_sent) return [.. _sent]; }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWith is not null)
            throw FailWith;
        if (FailFor.Contains(message.ToEmail))
            throw new InvalidOperationException($"SMTP rejected {message.ToEmail}");

        lock (_sent) _sent.Add(message);
        return Task.CompletedTask;
    }
}

public class EmailNotificationServiceTests
{
    private readonly RecordingMailSender _mail = new();

    private EmailNotificationService CreateService() => new(_mail);

    private sealed record FeedRef(Guid Id, string Name);

    private static FeedRef NewFeed(string name) => new(Guid.CreateVersion7(), name);

    private static NewArticle Story(FeedRef feed, string title, string? summary = null, string? link = null, DateTimeOffset? publishedAt = null) => new(
        Guid.CreateVersion7(),
        feed.Id,
        feed.Name,
        title,
        link ?? "https://articles.example.test/" + Uri.EscapeDataString(title),
        summary,
        ImageUrl: null,
        publishedAt);

    [Fact]
    public void Channel_IsEmail()
    {
        Assert.Same(NotificationChannel.Email, CreateService().Channel);
        Assert.Equal([NotificationChannel.Email, NotificationChannel.Slack], NotificationChannel.List.OrderBy(c => c.Value));
    }

    [Fact]
    public async Task NotifyAsync_SingleArticle_SendsOneEmailToTheSubscriber()
    {
        var article = Story(NewFeed("BBC News"), "Big headline", "What happened.",
            publishedAt: new DateTimeOffset(2026, 9, 21, 12, 30, 0, TimeSpan.FromHours(2)));

        var result = await CreateService().NotifyAsync(new ArticleNotification("alice@example.test", [article]));

        Assert.False(result.IsError);
        var email = Assert.Single(_mail.Sent);
        Assert.Equal("alice@example.test", email.ToEmail);
        Assert.Equal("New from BBC News: Big headline", email.Subject);
        Assert.Contains("BBC News", email.HtmlBody);
        Assert.Contains("Big headline", email.HtmlBody);
        Assert.Contains("What happened.", email.HtmlBody);
        Assert.Contains("href=\"https://articles.example.test/Big%20headline\"", email.HtmlBody);
        Assert.Contains("21 Sep 2026 10:30 UTC", email.HtmlBody);
    }

    [Fact]
    public async Task NotifyAsync_BodyIsTableBased_ForOutlook()
    {
        var article = Story(NewFeed("BBC News"), "Big headline", "What happened.");

        await CreateService().NotifyAsync(new ArticleNotification("alice@example.test", [article]));

        var html = Assert.Single(_mail.Sent).HtmlBody;
        Assert.Contains("<table", html);
        Assert.DoesNotContain("<div", html);
    }

    [Fact]
    public async Task NotifyAsync_SeveralArticlesOfOneFeed_UsesACountSubject()
    {
        var feed = NewFeed("BBC News");

        await CreateService().NotifyAsync(new ArticleNotification(
            "alice@example.test", [Story(feed, "One"), Story(feed, "Two"), Story(feed, "Three")]));

        var email = Assert.Single(_mail.Sent);
        Assert.Equal("3 new articles from BBC News", email.Subject);
        // Articles keep the order they were given in (newest first from the poll).
        Assert.True(email.HtmlBody.IndexOf("One", StringComparison.Ordinal) < email.HtmlBody.IndexOf("Two", StringComparison.Ordinal));
        Assert.True(email.HtmlBody.IndexOf("Two", StringComparison.Ordinal) < email.HtmlBody.IndexOf("Three", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotifyAsync_SeveralFeeds_AreGroupedUnderTheirFeedNames()
    {
        var bbc = NewFeed("BBC News");
        var dw = NewFeed("Deutsche Welle");

        await CreateService().NotifyAsync(new ArticleNotification(
            "alice@example.test", [Story(dw, "DW story"), Story(bbc, "BBC story"), Story(dw, "DW second")]));

        var email = Assert.Single(_mail.Sent);
        Assert.Equal("3 new articles from 2 feeds", email.Subject);
        var html = email.HtmlBody;
        int At(string value) => html.IndexOf(value, StringComparison.Ordinal);
        Assert.True(At(">BBC News<") < At("BBC story"));
        Assert.True(At("BBC story") < At(">Deutsche Welle<"));
        Assert.True(At(">Deutsche Welle<") < At("DW story"));
        Assert.True(At("DW story") < At("DW second"));
    }

    [Fact]
    public async Task NotifyAsync_HtmlEncodesEverythingThatCameFromTheFeed()
    {
        var article = Story(
            NewFeed("Evil <b>Feed</b>"),
            title: "<script>alert('title')</script>",
            summary: "<img src=x onerror=alert(1)> & more",
            link: "https://articles.example.test/?a=1&b=\"><script>alert(2)</script>");

        await CreateService().NotifyAsync(new ArticleNotification("alice@example.test", [article]));

        var html = Assert.Single(_mail.Sent).HtmlBody;
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<b>Feed</b>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("a=1&amp;b=", html);
    }

    [Fact]
    public async Task NotifyAsync_IncludesAPlainTextAlternative()
    {
        var feed = NewFeed("BBC News");

        await CreateService().NotifyAsync(new ArticleNotification(
            "alice@example.test", [Story(feed, "Big headline", "What happened.")]));

        var text = Assert.Single(_mail.Sent).TextBody;
        Assert.NotNull(text);
        Assert.Contains("BBC News", text);
        Assert.Contains("Big headline", text);
        Assert.Contains("What happened.", text);
        Assert.Contains("https://articles.example.test/Big%20headline", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public async Task NotifyAsync_LongSummary_IsShortened()
    {
        var article = Story(NewFeed("BBC News"), "Headline", new string('s', 2000));

        await CreateService().NotifyAsync(new ArticleNotification("alice@example.test", [article]));

        var html = Assert.Single(_mail.Sent).HtmlBody;
        Assert.DoesNotContain(new string('s', EmailNotificationService.SummaryMaxLength), html);
        Assert.Contains(new string('s', EmailNotificationService.SummaryMaxLength - 1), html);
    }

    [Fact]
    public async Task NotifyAsync_TwoFeedsWithTheSameName_StayTwoGroups()
    {
        // Feed names are not unique (only feed URLs are), so grouping goes by feed id.
        var first = NewFeed("News");
        var second = NewFeed("News");

        await CreateService().NotifyAsync(new ArticleNotification(
            "alice@example.test", [Story(first, "From the first"), Story(second, "From the second")]));

        var email = Assert.Single(_mail.Sent);
        Assert.Equal("2 new articles from 2 feeds", email.Subject);
        Assert.Equal(2, email.HtmlBody.Split(">News<").Length - 1);
    }

    [Fact]
    public async Task NotifyAsync_ArticleWithoutSummaryOrDate_StillSends()
    {
        var article = Story(NewFeed("BBC News"), "Bare");

        var result = await CreateService().NotifyAsync(new ArticleNotification("alice@example.test", [article]));

        Assert.False(result.IsError);
        var email = Assert.Single(_mail.Sent);
        Assert.Equal("New from BBC News: Bare", email.Subject);
        Assert.DoesNotContain("UTC", email.HtmlBody);
    }

    [Fact]
    public async Task NotifyAsync_NoArticles_SendsNothing()
    {
        var result = await CreateService().NotifyAsync(new ArticleNotification("alice@example.test", []));

        Assert.False(result.IsError);
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task NotifyAsync_TransportFailure_IsAnErrorCarryingTheCause_NotAnException()
    {
        _mail.FailWith = new InvalidOperationException("SMTP is down");
        var article = Story(NewFeed("BBC News"), "Headline");

        var result = await CreateService().NotifyAsync(new ArticleNotification("alice@example.test", [article]));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
        Assert.Equal("Notification.EmailFailed", result.FirstError.Code);
        Assert.Contains("alice@example.test", result.FirstError.Description);
        Assert.Contains("SMTP is down", result.FirstError.Description);
    }

    [Fact]
    public async Task NotifyAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var article = Story(NewFeed("BBC News"), "Headline");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService().NotifyAsync(new ArticleNotification("alice@example.test", [article]), cts.Token));
    }
}
