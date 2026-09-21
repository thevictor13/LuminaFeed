namespace LuminaFeed.Options;

/// <summary>
/// Optional bootstrap admin account. When both <see cref="Email"/> and <see cref="Password"/> are
/// supplied, the app ensures an admin user exists at startup (creating it, or promoting an existing
/// user). Left empty by default, so <b>no admin is created unless explicitly configured</b> — dev
/// supplies a local admin in appsettings.Development.json.
/// </summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}
