using LuminaFeed.Domain;
using LuminaFeed.Options;
using LuminaFeed.Services.Notifications;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Services.Polling;

/// <summary>
/// Drives <see cref="IFeedPollingService"/>: one pass at startup, then one every
/// <see cref="PollingOptions.Interval"/> (default one minute). Each pass gets its own DI scope, and passes what
/// the poll found on to the <see cref="INotificationService"/>s.
/// </summary>
public sealed class FeedPollingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<PollingOptions> options,
    TimeProvider timeProvider,
    ILogger<FeedPollingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up host startup with the first pass.
        await Task.Yield();

        var interval = options.Value.Interval;
        logger.LogInformation("Feed polling started; polling every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval, timeProvider);
        try
        {
            do
            {
                await RunCycleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        logger.LogInformation("Feed polling stopped.");
    }

    /// <summary>
    /// One polling pass. Never throws (other than for shutdown): an exception escaping a
    /// <see cref="BackgroundService"/> stops the whole host, and the next tick may well succeed.
    /// </summary>
    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var polling = scope.ServiceProvider.GetRequiredService<IFeedPollingService>();

            var newArticlesByEmail = await polling.PollAsync(cancellationToken);
            if (newArticlesByEmail.Count == 0)
                return;

            logger.LogInformation(
                "Polling pass found {ArticleCount} new article notification(s) for {SubscriberCount} subscriber(s).",
                newArticlesByEmail.Values.Sum(articles => articles.Count), newArticlesByEmail.Count);

            // The polling result is keyed by email address and only covers email-enabled subscriptions, so it
            // goes to the email channel. (Per-subscriber channel choice, incl. Slack, arrives with C2/C4.)
            var notifiers = scope.ServiceProvider.GetServices<INotificationService>()
                .Where(n => n.Channel == NotificationChannel.Email)
                .ToList();
            await DispatchAsync(notifiers, newArticlesByEmail, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feed polling pass failed; retrying on the next tick.");
        }
    }

    /// <summary>One notification per subscriber per channel; a failing recipient or channel never blocks the rest.</summary>
    private async Task DispatchAsync(
        IReadOnlyList<INotificationService> notifiers,
        IReadOnlyDictionary<string, IReadOnlyList<Article>> newArticlesByEmail,
        CancellationToken cancellationToken)
    {
        if (notifiers.Count == 0)
        {
            logger.LogWarning("New articles were found but no email notification service is registered.");
            return;
        }

        foreach (var (email, articles) in newArticlesByEmail)
        {
            var notification = new ArticleNotification(email, articles);
            foreach (var notifier in notifiers)
            {
                try
                {
                    var result = await notifier.NotifyAsync(notification, cancellationToken);
                    if (result.IsError)
                    {
                        logger.LogWarning(
                            "{Channel} notification to {Recipient} failed: {Reason}",
                            notifier.Channel.Name, email, result.FirstError.Description);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "{Channel} notification to {Recipient} threw.", notifier.Channel.Name, email);
                }
            }
        }
    }
}
