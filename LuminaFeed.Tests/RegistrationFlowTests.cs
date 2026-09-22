using System.Net;
using System.Text.RegularExpressions;
using LuminaFeed.Data;
using LuminaFeed.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

/// <summary>
/// The spec's "register, confirm, and return to where you started" path through the real host: the real
/// Register form, the real Identity confirmation token carried by the recorded email, and the real
/// ConfirmEmail page. This is the flow the seeded admin (confirmed directly) never exercises.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class RegistrationFlowTests
{
    private const string Password = "Register!123";

    [Fact]
    public async Task Register_ConfirmViaTheEmailedLink_ThenContinueBackToTheOrigin()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await RegisterAsync(client, "newcomer@example.test", returnUrl: "/");

        var mail = Assert.Single(RecordedMail(factory).Sent);
        Assert.Equal("newcomer@example.test", mail.ToEmail);
        var link = ConfirmationLink(mail);
        // The HTML part carries the same link encoded exactly once, so a mail client decodes it back to
        // the raw URL. (Encoded twice, "&amp;amp;code=" would reach the server as a parameter named "amp;code".)
        Assert.Contains(link.Replace("&", "&amp;"), mail.HtmlBody);
        Assert.DoesNotContain("&amp;amp;", mail.HtmlBody);

        using var confirm = await client.GetAsync(link);
        var page = await confirm.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Contains("Thank you for confirming your email.", page);
        // Continue leads to login with the original location, so the visitor lands back on the feed list.
        Assert.Contains("href=\"Account/Login?ReturnUrl=%2F\"", page);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("newcomer@example.test");
            Assert.True(user!.EmailConfirmed);
        }

        // ...and, being confirmed, the account can now sign in (RequireConfirmedAccount).
        await TestSignIn.SignInAsync(client, "newcomer@example.test", Password);
    }

    [Fact]
    public async Task ConfirmEmail_OffSiteReturnUrl_IsNotFollowed()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await RegisterAsync(client, "wary@example.test", returnUrl: "//evil.example");
        var link = ConfirmationLink(Assert.Single(RecordedMail(factory).Sent));

        var page = await client.GetStringAsync(link);

        Assert.Contains("Thank you for confirming your email.", page);
        Assert.Contains("href=\"Account/Login\"", page);
        Assert.DoesNotContain("evil.example", page);
    }

    [Fact]
    public async Task ConfirmEmail_MangledCode_IsAnErrorMessage_NotAServerFault()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await RegisterAsync(client, "typo@example.test", returnUrl: "/");
        var link = ConfirmationLink(Assert.Single(RecordedMail(factory).Sent));
        var mangled = Regex.Replace(link, "code=[^&]+", "code=%21not-base64url%21");

        using var response = await client.GetAsync(mangled);
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Error confirming your email.", page);
        Assert.DoesNotContain("Continue", page);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("typo@example.test");
        Assert.False(user!.EmailConfirmed);
    }

    /// <summary>Posts the real Register form (with its antiforgery token) and expects the "check your email" redirect.</summary>
    private static async Task RegisterAsync(HttpClient client, string email, string returnUrl)
    {
        var url = "/Account/Register?ReturnUrl=" + Uri.EscapeDataString(returnUrl);
        var form = await client.GetStringAsync(url);

        using var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "register",
            ["__RequestVerificationToken"] = TestSignIn.AntiforgeryTokenFrom(form),
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        }));

        // Success redirects to RegisterConfirmation; a validation failure re-renders the form (200).
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("RegisterConfirmation", response.Headers.Location?.ToString());
    }

    private static RecordingMailSender RecordedMail(TestAppFactory factory) =>
        Assert.IsType<RecordingMailSender>(factory.Services.GetRequiredService<IMailSender>());

    /// <summary>The confirmation URL as the plain-text part carries it — raw, the way a text-only client would open it.</summary>
    private static string ConfirmationLink(EmailMessage mail)
    {
        var link = mail.TextBody!
            .Split('\n')
            .Select(line => line.Trim())
            .Single(line => line.StartsWith("http", StringComparison.Ordinal));

        Assert.Contains("/Account/ConfirmEmail?", link);
        Assert.Contains("&code=", link);
        return link;
    }
}
