using ErrorOr;

namespace LuminaFeed.Services.Polling;

public static class PollingErrors
{
    public static Error InvalidXml(string detail) =>
        Error.Failure("Polling.InvalidXml", $"The feed is not well-formed XML: {detail}");

    public static readonly Error UnrecognizedFormat =
        Error.Failure("Polling.UnrecognizedFormat", "The document is not an RSS, Atom or RDF feed.");

    public static Error HttpStatus(string feedUrl, int statusCode) =>
        Error.Failure("Polling.HttpStatus", $"Fetching '{feedUrl}' returned HTTP {statusCode}.");

    public static Error FetchFailed(string feedUrl, string detail) =>
        Error.Failure("Polling.FetchFailed", $"Fetching '{feedUrl}' failed: {detail}");
}
