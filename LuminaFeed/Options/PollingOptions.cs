using System.ComponentModel.DataAnnotations;

namespace LuminaFeed.Options;

/// <summary>
/// Controls the RSS polling background task. Defaults to once per minute (per the spec) and is
/// configurable via the "Polling" section.
/// </summary>
public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    [Range(1, int.MaxValue)]
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// The first time a feed is polled every item in it is new. All of them are stored, but only this many of
    /// the newest are reported for notification, so a fresh subscriber isn't sent the whole backlog.
    /// 0 makes the first poll a silent baseline.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int FirstPollNotificationCap { get; set; } = 5;

    /// <summary>Convenience accessor for the poll interval as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
}
