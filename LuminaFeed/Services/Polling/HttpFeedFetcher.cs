using System.Net;
using ErrorOr;

namespace LuminaFeed.Services.Polling;

/// <summary>
/// <see cref="IFeedFetcher"/> over a typed <see cref="HttpClient"/> (see <see cref="Configure"/>). Plain GET for
/// now — conditional requests (ETag / Last-Modified / 304) arrive with C4.
/// </summary>
public sealed class HttpFeedFetcher(HttpClient http) : IFeedFetcher
{
    public const int MaxResponseBytes = 10 * 1024 * 1024;

    /// <summary>Client defaults: bounded time and size, and a browser-compatible identity (some publishers
    /// reject unknown agents — the catalogue was validated with a browser user agent).</summary>
    public static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LuminaFeed/1.0)");
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/rss+xml, application/atom+xml, application/rdf+xml;q=0.9, application/xml;q=0.9, text/xml;q=0.9, */*;q=0.5");
    }

    /// <summary>How many redirects <see cref="FetchAsync"/> follows itself (see <see cref="SecureRedirectTarget"/>).</summary>
    public const int MaxManualRedirects = 3;

    /// <summary>
    /// The handler follows ordinary redirects on its own, but (rightly) refuses an https → http downgrade and
    /// hands back the 3xx. Several publishers redirect their https feed URL to an <c>http://</c> one that only
    /// bounces back to https, so such a redirect is followed here with the scheme <b>upgraded to https</b> —
    /// the fetch never leaves TLS. Null when the response isn't a redirect with a usable target.
    /// </summary>
    private static string? SecureRedirectTarget(HttpResponseMessage response, string requestedUrl)
    {
        if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)
            || response.Headers.Location is not { } location)
            return null;

        // The handler may already have followed some hops; relative targets resolve against the last one.
        var requestUri = response.RequestMessage?.RequestUri ?? new Uri(requestedUrl, UriKind.Absolute);
        var target = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        if (target.Scheme == Uri.UriSchemeHttp)
            target = new UriBuilder(target) { Scheme = Uri.UriSchemeHttps, Port = target.IsDefaultPort ? -1 : target.Port }.Uri;

        return target.Scheme == Uri.UriSchemeHttps ? target.AbsoluteUri : null;
    }

    /// <summary>Transparent gzip/deflate/brotli, which most feed hosts offer.</summary>
    public static HttpMessageHandler CreateHandler() =>
        new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };

    public async Task<ErrorOr<byte[]>> FetchAsync(string feedUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestUrl = feedUrl;
            for (var redirects = 0; ; redirects++)
            {
                // Buffered (the default completion option), so MaxResponseContentBufferSize caps the download.
                using var response = await http.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);

                if (redirects < MaxManualRedirects && SecureRedirectTarget(response, requestUrl) is { } target)
                {
                    requestUrl = target;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    return PollingErrors.HttpStatus(feedUrl, (int)response.StatusCode);

                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return PollingErrors.FetchFailed(feedUrl, "the request timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            return PollingErrors.FetchFailed(feedUrl, ex.Message);
        }
    }
}
