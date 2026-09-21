using System.ComponentModel.DataAnnotations;
using LuminaFeed.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Tests;

public class OptionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void SmtpOptions_BindsFromConfiguration()
    {
        var config = BuildConfig(new()
        {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25",
            ["Smtp:FromAddress"] = "no-reply@luminafeed.local",
            ["Smtp:FromName"] = "LuminaFeed",
        });

        var options = config.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>();

        Assert.NotNull(options);
        Assert.Equal("localhost", options!.Host);
        Assert.Equal(25, options.Port);
        Assert.Equal("no-reply@luminafeed.local", options.FromAddress);
        Assert.Equal("LuminaFeed", options.FromName);
        Assert.False(options.UseStartTls);
    }

    [Fact]
    public void PollingOptions_DefaultsToOneMinute_AndComputesInterval()
    {
        var options = new PollingOptions();
        Assert.Equal(60, options.IntervalSeconds);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Interval);

        var config = BuildConfig(new() { ["Polling:IntervalSeconds"] = "120" });
        var bound = config.GetSection(PollingOptions.SectionName).Get<PollingOptions>();
        Assert.Equal(TimeSpan.FromSeconds(120), bound!.Interval);
    }

    [Fact]
    public void UnsubscribeOptions_BindsSecret()
    {
        var config = BuildConfig(new() { ["Unsubscribe:HmacSecret"] = "a-sufficiently-long-secret" });
        var options = config.GetSection(UnsubscribeOptions.SectionName).Get<UnsubscribeOptions>();
        Assert.Equal("a-sufficiently-long-secret", options!.HmacSecret);
    }

    [Fact]
    public void SmtpOptions_FailsValidation_WhenRequiredFieldsMissing()
    {
        var options = new SmtpOptions(); // Host and FromAddress empty
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(SmtpOptions.Host)));
    }

    [Fact]
    public void UnsubscribeOptions_FailsValidation_WhenSecretTooShort()
    {
        var options = new UnsubscribeOptions { HmacSecret = "short" };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(UnsubscribeOptions.HmacSecret)));
    }

    [Fact]
    public void OptionsPipeline_ValidatesOnResolve_WithDevLikeConfig()
    {
        // Mirrors the Program.cs registration and the merged Development configuration.
        var config = BuildConfig(new()
        {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25",
            ["Smtp:FromAddress"] = "no-reply@luminafeed.local",
            ["Polling:IntervalSeconds"] = "60",
            ["Unsubscribe:HmacSecret"] = "dev-only-unsubscribe-hmac-secret-change-me",
        });

        var services = new ServiceCollection();
        services.AddOptions<SmtpOptions>()
            .Bind(config.GetSection(SmtpOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<PollingOptions>()
            .Bind(config.GetSection(PollingOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<UnsubscribeOptions>()
            .Bind(config.GetSection(UnsubscribeOptions.SectionName)).ValidateDataAnnotations();

        using var provider = services.BuildServiceProvider();

        // Resolving forces validation; valid dev-like config must not throw.
        Assert.Equal("localhost", provider.GetRequiredService<IOptions<SmtpOptions>>().Value.Host);
        Assert.Equal(TimeSpan.FromMinutes(1), provider.GetRequiredService<IOptions<PollingOptions>>().Value.Interval);
        Assert.Equal(
            "dev-only-unsubscribe-hmac-secret-change-me",
            provider.GetRequiredService<IOptions<UnsubscribeOptions>>().Value.HmacSecret);
    }

    [Fact]
    public void OptionsPipeline_Throws_WhenSecretMissing()
    {
        var config = BuildConfig(new() { ["Unsubscribe:HmacSecret"] = "" });
        var services = new ServiceCollection();
        services.AddOptions<UnsubscribeOptions>()
            .Bind(config.GetSection(UnsubscribeOptions.SectionName)).ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<UnsubscribeOptions>>().Value);
    }
}
