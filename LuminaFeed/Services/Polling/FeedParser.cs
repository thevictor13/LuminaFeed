using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ErrorOr;

namespace LuminaFeed.Services.Polling;

/// <summary>One item/entry read from a feed document, before it becomes an <c>Article</c>.</summary>
/// <param name="ExternalId">The source's stable id: RSS <c>&lt;guid&gt;</c> / Atom <c>&lt;id&gt;</c> / RDF <c>rdf:about</c>, else the link.</param>
/// <param name="Link">Absolute http(s) article URL.</param>
/// <param name="Summary">Plain text (tags stripped, entities decoded, truncated); null when the feed gives none.</param>
/// <param name="ImageUrl">Absolute http(s) image URL the feed itself provides; null when it provides none.</param>
public sealed record ParsedFeedItem(
    string ExternalId,
    string Title,
    string Link,
    string? Summary,
    string? ImageUrl,
    DateTimeOffset? PublishedAt);

/// <summary>
/// A small, tolerant feed reader over <see cref="XDocument"/> for the three formats in the curated
/// catalogue: <b>RSS 2.0</b>, <b>Atom 1.0</b> and <b>RDF / RSS 1.0</b>. Elements are matched by local name, so
/// namespace prefixes and sloppy publishers don't matter. Items without a usable http(s) link are skipped
/// (the link is rendered in pages and emails, so other schemes are never let through).
/// </summary>
public static partial class FeedParser
{
    public const int SummaryMaxLength = 1000;
    public const string UntitledTitle = "(untitled)";

    private const string MediaNamespace = "http://search.yahoo.com/mrss/";
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>Parses raw response bytes; the XML declaration / BOM decides the text encoding.</summary>
    public static ErrorOr<IReadOnlyList<ParsedFeedItem>> Parse(byte[] content, Uri? baseUri = null)
    {
        // Some publishers emit blank lines before the XML declaration, which XML forbids.
        var start = 0;
        while (start < content.Length && content[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            start++;

        using var stream = new MemoryStream(content, start, content.Length - start, writable: false);
        return Parse(settings => XmlReader.Create(stream, settings), baseUri);
    }

    /// <summary>Parses already-decoded XML text.</summary>
    public static ErrorOr<IReadOnlyList<ParsedFeedItem>> Parse(string xml, Uri? baseUri = null)
    {
        using var text = new StringReader(xml.TrimStart(ByteOrderMark, ' ', '\t', '\r', '\n'));
        return Parse(settings => XmlReader.Create(text, settings), baseUri);
    }

    private static ErrorOr<IReadOnlyList<ParsedFeedItem>> Parse(Func<XmlReaderSettings, XmlReader> open, Uri? baseUri)
    {
        // Untrusted input: DTDs are skipped (not expanded) and nothing external is ever resolved (no XXE).
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        XDocument document;
        try
        {
            using var reader = open(settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            return PollingErrors.InvalidXml(ex.Message);
        }

        var root = document.Root;
        var isAtom = root?.Name.LocalName == "feed";
        if (root is null || (!isAtom && root.Name.LocalName is not ("rss" or "RDF")))
            return PollingErrors.UnrecognizedFormat;

        var items = new List<ParsedFeedItem>();
        foreach (var element in root.Descendants().Where(e => e.Name.LocalName == (isAtom ? "entry" : "item")))
        {
            if (ParseItem(element, isAtom, baseUri) is { } item)
                items.Add(item);
        }

        return items;
    }

    private static ParsedFeedItem? ParseItem(XElement item, bool isAtom, Uri? baseUri)
    {
        var rawId = isAtom
            ? Text(item, "id")
            : Text(item, "guid") ?? item.Attributes().FirstOrDefault(a => a.Name.LocalName == "about")?.Value.Trim();

        var rawLink = isAtom ? AtomLink(item) : Text(item, "link");
        // An RSS <guid> is a permalink unless it says otherwise, so it can stand in for a missing <link>.
        var link = HttpUrl(rawLink, baseUri) ?? (isAtom ? null : HttpUrl(rawId, baseUri: null));
        if (link is null)
            return null;

        var title = PlainText(Text(item, "title"), maxLength: null);
        var summary = isAtom
            ? Text(item, "summary") ?? Text(item, "content")
            : Text(item, "description") ?? Text(item, "encoded");
        var published = isAtom
            ? Text(item, "published") ?? Text(item, "updated")
            : Text(item, "pubDate") ?? Text(item, "date");

        return new ParsedFeedItem(
            ExternalId: string.IsNullOrEmpty(rawId) ? link : rawId,
            Title: string.IsNullOrEmpty(title) ? UntitledTitle : title,
            Link: link,
            Summary: NullIfEmpty(PlainText(summary, SummaryMaxLength)),
            ImageUrl: ImageUrl(item, baseUri),
            PublishedAt: ParseDate(published));
    }

    /// <summary>
    /// Trimmed text of the first direct child with that local name that actually has text. Media RSS elements
    /// are ignored (so <c>media:content</c> / <c>media:title</c> never shadow the real ones), and so are empty
    /// namesakes such as an <c>&lt;atom:link href="…"/&gt;</c> sitting before the RSS <c>&lt;link&gt;</c>.
    /// </summary>
    private static string? Text(XElement parent, string localName) =>
        parent.Elements()
            .Where(e => e.Name.LocalName == localName && e.Name.NamespaceName != MediaNamespace)
            .Select(e => e.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

    /// <summary>Atom carries links as attributes; prefer <c>rel="alternate"</c> (or no rel) over self/enclosure links.</summary>
    private static string? AtomLink(XElement entry)
    {
        var links = entry.Elements().Where(e => e.Name.LocalName == "link").ToList();
        var best = links.FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate") ?? links.FirstOrDefault();
        return ((string?)best?.Attribute("href"))?.Trim();
    }

    private static string? ImageUrl(XElement item, Uri? baseUri)
    {
        foreach (var element in item.Elements())
        {
            var name = element.Name.LocalName;
            var type = (string?)element.Attribute("type") ?? string.Empty;

            var candidate = name switch
            {
                // RSS <enclosure type="image/..."> and Atom <link rel="enclosure" type="image/...">.
                "enclosure" when type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => (string?)element.Attribute("url"),
                "link" when (string?)element.Attribute("rel") == "enclosure"
                            && type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => (string?)element.Attribute("href"),
                // Media RSS.
                "thumbnail" when element.Name.NamespaceName == MediaNamespace => (string?)element.Attribute("url"),
                "content" when element.Name.NamespaceName == MediaNamespace
                               && ((string?)element.Attribute("medium") == "image"
                                   || type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) => (string?)element.Attribute("url"),
                _ => null,
            };

            if (HttpUrl(candidate, baseUri) is { } url)
                return url;
        }

        return null;
    }

    /// <summary>The value as an absolute http(s) URL (relative values resolve against <paramref name="baseUri"/>), else null.</summary>
    private static string? HttpUrl(string? value, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (baseUri is null || !Uri.TryCreate(baseUri, value, out uri)))
            return null;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
    }

    /// <summary>Feed text is often HTML: drop scripts/styles and tags, decode entities, collapse whitespace.</summary>
    private static string PlainText(string? html, int? maxLength)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var text = StripTags(ScriptOrStyle().Replace(html, " "));
        text = WebUtility.HtmlDecode(text);
        // Decoding can surface escaped markup (&lt;p&gt;…), so strip once more.
        text = StripTags(text);
        text = Whitespace().Replace(text, " ").Trim();

        if (maxLength is { } max && text.Length > max)
            text = text[..(max - 1)].TrimEnd() + "…";
        return text;
    }

    /// <summary>Inline tags vanish (so "<b>world</b>." stays "world."); block-level tags become a word break.</summary>
    private static string StripTags(string html) => Tag().Replace(InlineTag().Replace(html, string.Empty), " ");

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    // RFC 822 zone names plus the abbreviations publishers actually use (e.g. Sky Sports' "BST"). Ambiguous
    // ones (IST, CST outside the US...) are left out or follow RFC 822; an unknown zone leaves the date unread.
    private static readonly Dictionary<string, string> ZoneOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UT"] = "+00:00", ["UTC"] = "+00:00", ["GMT"] = "+00:00", ["Z"] = "+00:00", ["WET"] = "+00:00",
        ["BST"] = "+01:00", ["WEST"] = "+01:00", ["CET"] = "+01:00",
        ["CEST"] = "+02:00", ["EET"] = "+02:00", ["EEST"] = "+03:00", ["MSK"] = "+03:00",
        ["JST"] = "+09:00", ["KST"] = "+09:00", ["AEST"] = "+10:00", ["AEDT"] = "+11:00",
        ["NZST"] = "+12:00", ["NZDT"] = "+13:00",
        ["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00",
        ["MST"] = "-07:00", ["MDT"] = "-06:00", ["PST"] = "-08:00", ["PDT"] = "-07:00",
        ["AKST"] = "-09:00", ["AKDT"] = "-08:00", ["HST"] = "-10:00",
    };

    private static readonly string[] Rfc822Formats =
    [
        "d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm zzz", "d MMM yyyy HH:mm:ss", "d MMM yyyy HH:mm",
        "d MMM yy HH:mm:ss zzz", "d MMM yy HH:mm zzz",
    ];

    /// <summary>
    /// ISO 8601 (Atom/RDF) or RFC 822 (RSS — including named zones, <c>+hhmm</c> offsets and a weekday that
    /// doesn't match the date); null when unreadable. Values without a zone are taken as UTC.
    /// </summary>
    internal static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // The weekday is redundant, and .NET rejects the whole date when a publisher gets it wrong.
        var text = LeadingWeekday().Replace(value.Trim(), string.Empty);
        text = NumericOffsetWithoutColon().Replace(text, "${sign}${hours}:${minutes}");
        text = NamedZone().Replace(text, m => ZoneOffsets.GetValueOrDefault(m.Value, m.Value));

        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal;
        if (DateTimeOffset.TryParseExact(text, Rfc822Formats, CultureInfo.InvariantCulture, styles, out var exact))
            return exact.ToUniversalTime();
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, styles, out var parsed))
            return parsed.ToUniversalTime();
        return null;
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"</?(a|abbr|b|cite|code|em|i|mark|q|s|small|span|strong|sub|sup|u)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[A-Za-z]{3,9},?\s+(?=\d)")]
    private static partial Regex LeadingWeekday();

    [GeneratedRegex(@"(?<sign>[+-])(?<hours>\d{2})(?<minutes>\d{2})$")]
    private static partial Regex NumericOffsetWithoutColon();

    [GeneratedRegex(@"(?<=\d\s)[A-Za-z]{1,5}$")]
    private static partial Regex NamedZone();
}
