using LuminaFeed.Options;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Services.Polling;

/// <summary>
/// Drives <see cref="IFeedPollingService"/>: one pass at startup, then one every
/// <see cref="PollingOptions.Interval"/> (default one minute). Each pass gets its own DI scope.
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

            if (newArticlesByEmail.Count > 0)
            {
                logger.LogInformation(
                    "Polling pass found {ArticleCount} new article notification(s) for {SubscriberCount} subscriber(s).",
                    newArticlesByEmail.Values.Sum(articles => articles.Count), newArticlesByEmail.Count);
            }
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
}
