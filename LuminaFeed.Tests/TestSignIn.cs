using System.Net;
using System.Text.RegularExpressions;

namespace LuminaFeed.Tests;

/// <summary>Signs a test <see cref="HttpClient"/> in through the real Identity login form.</summary>
internal static partial class TestSignIn
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    /// <summary>
    /// Posts the login form (with its antiforgery token); the client's cookie container then carries
    /// the auth cookie. The client must not follow redirects automatically.
    /// </summary>
    public static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryToken().Match(loginPage);
        Assert.True(token.Success, "The login page did not contain an antiforgery token.");

        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }));

        // A successful sign-in redirects away from the login page; a failure re-renders it (200).
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
