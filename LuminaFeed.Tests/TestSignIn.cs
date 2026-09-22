using System.Net;
using System.Text.RegularExpressions;

namespace LuminaFeed.Tests;

/// <summary>Signs a test <see cref="HttpClient"/> in through the real Identity login form.</summary>
internal static partial class TestSignIn
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    /// <summary>The antiforgery token a rendered Identity form carries, ready to post back.</summary>
    public static string AntiforgeryTokenFrom(string formHtml)
    {
        var token = AntiforgeryToken().Match(formHtml);
        Assert.True(token.Success, "The page did not contain an antiforgery token.");
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    /// <summary>
    /// Posts the login form (with its antiforgery token); the client's cookie container then carries
    /// the auth cookie. The client must not follow redirects automatically.
    /// </summary>
    public static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetStringAsync("/Account/Login");

        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = AntiforgeryTokenFrom(loginPage),
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }));

        // A successful sign-in redirects away from the login page; a failure re-renders it (200)...
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // ...but lockout and two-factor prompts redirect too, so prove the cookie really signs the client in:
        // an authorised page renders instead of bouncing to login.
        using var manage = await client.GetAsync("/Account/Manage");
        Assert.Equal(HttpStatusCode.OK, manage.StatusCode);
    }
}
