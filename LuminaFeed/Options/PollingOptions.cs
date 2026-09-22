using System.ComponentModel.DataAnnotations;

namespace LuminaFeed.Options;

/// <summary>
/// Controls the RSS polling background task. Defaults to once per minute (per the spec) and is
/// configurable via the "Polling" section.
/// </summary>
public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    /// <summary>Seconds between polling passes. Capped at a day: PeriodicTimer rejects periods beyond ~49 days.</summary>
    [Range(1, 86_400)]
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// The first time a feed is polled every item in it is new. All of them are stored, but only this many of
    /// the newest are reported for notification, so a fresh subscriber isn't sent the whole backlog. The same
    /// cap applies to a <b>catch-up</b> poll — one after the feed went unpolled for longer than
    /// <see cref="CatchUpAfterMinutes"/>. 0 makes such polls a silent baseline.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int FirstPollNotificationCap { get; set; } = 5;

    /// <summary>
    /// A feed last polled longer ago than this (it had no subscribers for a while, or the host was down) is
    /// treated like a first poll: its accumulated backlog is stored but only the newest
    /// <see cref="FirstPollNotificationCap"/> are reported. Must be comfortably longer than the interval.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int CatchUpAfterMinutes { get; set; } = 360;

    /// <summary>Convenience accessor for the poll interval as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);

    /// <summary>Convenience accessor for <see cref="CatchUpAfterMinutes"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan CatchUpAfter => TimeSpan.FromMinutes(CatchUpAfterMinutes);
}
