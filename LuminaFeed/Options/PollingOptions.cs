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

    /// <summary>Convenience accessor for the poll interval as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
}
