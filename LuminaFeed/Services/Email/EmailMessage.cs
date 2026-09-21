namespace LuminaFeed.Services.Email;

/// <summary>A fully-composed outbound email, ready for the transport to send.</summary>
public sealed record EmailMessage(
    string ToEmail,
    string? ToName,
    string Subject,
    string HtmlBody,
    string? TextBody = null);
