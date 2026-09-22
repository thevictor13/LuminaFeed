using ErrorOr;
using LuminaFeed.Options;
using LuminaFeed.Services.Notifications;
using LuminaFeed.Services.Polling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LuminaFeed.Tests;

/// <summary>Covers the polling loop: it polls at startup, keeps ticking, survives failures and stops cleanly.</summary>
public class FeedPollingBackgroundServiceTests
{
    private sealed class ScriptedPollingService(Func<int, IReadOnlyDictionary<string, IReadOnlyList<NewArticle>>> onPoll) : IFeedPollingService
    {
        private int _calls;
        private readonly SemaphoreSlim _polled = new(0);

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<NewArticle>>> PollAsync(CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            _polled.Release();
            return Task.FromResult(onPoll(call));
        }

        /// <summary>Waits until at least <paramref name="count"/> polls have started.</summary>
        public async Task WaitForCallsAsync(int count, TimeSpan timeout)
        {
            for (var i = 0; i < count; i++)
                Assert.True(await _polled.WaitAsync(timeout), $"Expected {count} polls within {timeout}; saw {Calls}.");
        }
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<NewArticle>> Nothing =
        new Dictionary<string, IReadOnlyList<NewArticle>>();

    private sealed class FakeNotificationService(NotificationChannel channel, Func<ArticleNotification, ErrorOr<Success>>? onNotify = null)
        : INotificationService
    {
        public List<ArticleNotification> Received { get; } = [];

        public NotificationChannel Channel => channel;

        public Task<ErrorOr<Success>> NotifyAsync(ArticleNotification notification, CancellationToken cancellationToken = default)
        {
            Received.Add(notification);
            return Task.FromResult(onNotify?.Invoke(notification) ?? Result.Success);
        }
    }

    private static NewArticle Story(string title) => new(
        Guid.CreateVersion7(), Guid.Empty, "Feed", title, "https://articles.example.test/" + title,
        Summary: null, ImageUrl: null, PublishedAt: null);

    private static (FeedPollingBackgroundService Service, ListLogger<FeedPollingBackgroundService> Logger, ServiceProvider Provider)
        Create(IFeedPollingService polling, int intervalSeconds = 3600, params INotificationService[] notifiers)
    {
        var services = new ServiceCollection().AddScoped(_ => polling);
        foreach (var notifier in notifiers)
            services.AddSingleton(notifier);
        var provider = services.BuildServiceProvider();
        var logger = new ListLogger<FeedPollingBackgroundService>();
        var service = new FeedPollingBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new PollingOptions { IntervalSeconds = intervalSeconds }),
            TimeProvider.System,
            logger);
        return (service, logger, provider);
    }

    [Fact]
    public async Task StartAsync_PollsImmediately_ThenStopsCleanly()
    {
        var polling = new ScriptedPollingService(_ => Nothing);
        var (service, logger, provider) = Create(polling);
        using var disposeProvider = provider;

        await service.StartAsync(CancellationToken.None);
        await polling.WaitForCallsAsync(1, TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // An hour-long interval: only the startup pass ran, and shutdown wasn't treated as a failure.
        Assert.Equal(1, polling.Calls);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Loop_KeepsPollingOnTheInterval_EvenAfterAPassThrows()
    {
        var polling = new ScriptedPollingService(call =>
            call == 1 ? throw new InvalidOperationException("database is on fire") : Nothing);
        var (service, logger, provider) = Create(polling, intervalSeconds: 1);
        using var disposeProvider = provider;

        await service.StartAsync(CancellationToken.None);
        await polling.WaitForCallsAsync(2, TimeSpan.FromSeconds(15));
        await service.StopAsync(CancellationToken.None);

        Assert.True(polling.Calls >= 2);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully, "A failed pass must not fault the background service (that would stop the host).");
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task RunCycleAsync_SwallowsAndLogsFailures()
    {
        var polling = new ScriptedPollingService(_ => throw new InvalidOperationException("boom"));
        var (service, logger, provider) = Create(polling);
        using var disposeProvider = provider;

        await service.RunCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception?.Message == "boom");
    }

    [Fact]
    public async Task RunCycleAsync_PropagatesShutdownCancellation()
    {
        using var cts = new CancellationTokenSource();
        var polling = new ScriptedPollingService(_ => throw new OperationCanceledException(cts.Token));
        var (service, logger, provider) = Create(polling);
        using var disposeProvider = provider;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunCycleAsync(cts.Token));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task RunCycleAsync_SendsEachSubscriberTheirArticles_ThroughTheEmailChannelOnly()
    {
        var forAlice = new[] { Story("A1"), Story("A2") };
        var forBob = new[] { Story("B1") };
        var polling = new ScriptedPollingService(_ => new Dictionary<string, IReadOnlyList<NewArticle>>
        {
            ["alice@example.test"] = forAlice,
            ["bob@example.test"] = forBob,
        });
        var email = new FakeNotificationService(NotificationChannel.Email);
        var slack = new FakeNotificationService(NotificationChannel.Slack);
        var (service, _, provider) = Create(polling, notifiers: [email, slack]);
        using var disposeProvider = provider;

        await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, email.Received.Count);
        Assert.Equal(forAlice, email.Received.Single(n => n.RecipientEmail == "alice@example.test").Articles);
        Assert.Equal(forBob, email.Received.Single(n => n.RecipientEmail == "bob@example.test").Articles);
        // The poll result only covers email-enabled subscriptions, so nothing may leak to another channel.
        Assert.Empty(slack.Received);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunCycleAsync_OneFailingRecipient_DoesNotBlockTheOthers(bool failureThrows)
    {
        var polling = new ScriptedPollingService(_ => new Dictionary<string, IReadOnlyList<NewArticle>>
        {
            ["alice@example.test"] = [Story("A1")],
            ["bob@example.test"] = [Story("B1")],
            ["carol@example.test"] = [Story("C1")],
        });
        var email = new FakeNotificationService(NotificationChannel.Email, notification =>
            notification.RecipientEmail != "bob@example.test" ? Result.Success
            : failureThrows ? throw new InvalidOperationException("kaboom")
            : Error.Failure("Notification.EmailFailed", "SMTP said no"));
        var (service, logger, provider) = Create(polling, notifiers: email);
        using var disposeProvider = provider;

        await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(
            ["alice@example.test", "bob@example.test", "carol@example.test"],
            email.Received.Select(n => n.RecipientEmail).Order());
        Assert.Contains(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task RunCycleAsync_NothingNew_NotifiesNobody()
    {
        var email = new FakeNotificationService(NotificationChannel.Email);
        var (service, _, provider) = Create(new ScriptedPollingService(_ => Nothing), notifiers: email);
        using var disposeProvider = provider;

        await service.RunCycleAsync(CancellationToken.None);

        Assert.Empty(email.Received);
    }

    [Fact]
    public async Task RunCycleAsync_NewArticlesButNoEmailService_LogsAWarning()
    {
        var polling = new ScriptedPollingService(_ => new Dictionary<string, IReadOnlyList<NewArticle>>
        {
            ["alice@example.test"] = [Story("A1")],
        });
        var (service, logger, provider) = Create(polling);
        using var disposeProvider = provider;

        await service.RunCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RunCycleAsync_UsesAFreshScopePerPass()
    {
        var created = 0;
        var provider = new ServiceCollection()
            .AddScoped<IFeedPollingService>(_ =>
            {
                Interlocked.Increment(ref created);
                return new ScriptedPollingService(_ => Nothing);
            })
            .BuildServiceProvider();
        using var disposeProvider = provider;
        var service = new FeedPollingBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new PollingOptions()),
            TimeProvider.System,
            new ListLogger<FeedPollingBackgroundService>());

        await service.RunCycleAsync(CancellationToken.None);
        await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, created);
    }
}
