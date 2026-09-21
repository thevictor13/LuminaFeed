using LuminaFeed.Domain;
using LuminaFeed.Options;
using LuminaFeed.Services.Polling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LuminaFeed.Tests;

/// <summary>Covers the polling loop: it polls at startup, keeps ticking, survives failures and stops cleanly.</summary>
public class FeedPollingBackgroundServiceTests
{
    private sealed class ScriptedPollingService(Func<int, IReadOnlyDictionary<string, IReadOnlyList<Article>>> onPoll) : IFeedPollingService
    {
        private int _calls;
        private readonly SemaphoreSlim _polled = new(0);

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<Article>>> PollAsync(CancellationToken cancellationToken = default)
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

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<Article>> Nothing =
        new Dictionary<string, IReadOnlyList<Article>>();

    private static (FeedPollingBackgroundService Service, ListLogger<FeedPollingBackgroundService> Logger, ServiceProvider Provider)
        Create(IFeedPollingService polling, int intervalSeconds = 3600)
    {
        var provider = new ServiceCollection().AddScoped(_ => polling).BuildServiceProvider();
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
