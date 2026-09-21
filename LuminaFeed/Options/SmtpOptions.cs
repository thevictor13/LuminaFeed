using System.ComponentModel.DataAnnotations;

namespace LuminaFeed.Options;

/// <summary>
/// SMTP settings for outbound email (MailKit). In Development these are pre-populated with
/// Papercut defaults for a local SMTP server; production supplies real values via environment
/// configuration / user secrets.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 25;

    [Required, EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "LuminaFeed";

    /// <summary>Use STARTTLS. Left off for a local Papercut server.</summary>
    public bool UseStartTls { get; set; }

    /// <summary>Optional SMTP auth; omitted for local Papercut.</summary>
    public string? UserName { get; set; }

    public string? Password { get; set; }
}
