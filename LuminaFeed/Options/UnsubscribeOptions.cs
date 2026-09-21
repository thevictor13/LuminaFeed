using System.ComponentModel.DataAnnotations;

namespace LuminaFeed.Options;

/// <summary>
/// Settings for HMAC-signed, token-based one-click unsubscribe (RFC 8058). The secret signs and
/// verifies unsubscribe tokens and must be supplied per environment (a placeholder dev value lives
/// in appsettings.Development.json; production supplies a real secret out of source control).
/// </summary>
public sealed class UnsubscribeOptions
{
    public const string SectionName = "Unsubscribe";

    [Required, MinLength(16)]
    public string HmacSecret { get; set; } = string.Empty;
}
