using System.Text;
using LuminaFeed.Services.Polling;

namespace LuminaFeed.Tests;

/// <summary>Covers the hand-rolled RSS 2.0 / Atom / RDF reader, including the sloppy input real feeds produce.</summary>
public class FeedParserTests
{
    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/"
             xmlns:content="http://purl.org/rss/1.0/modules/content/">
          <channel>
            <title>Example News</title>
            <link>https://news.example.test/</link>
            <atom:link href="https://news.example.test/rss.xml" rel="self" type="application/rss+xml"/>
            <item>
              <title>First &amp; foremost</title>
              <atom:link href="https://news.example.test/ignored-self-link"/>
              <link>https://news.example.test/articles/1</link>
              <guid isPermaLink="false">example-guid-1</guid>
              <pubDate>Mon, 21 Sep 2026 10:30:00 GMT</pubDate>
              <description><![CDATA[<p>Hello <b>world</b>.</p><script>alert(1)</script><p>Second&nbsp;paragraph.</p>]]></description>
              <media:title>Shadow title that must be ignored</media:title>
              <media:thumbnail url="https://news.example.test/img/1-thumb.jpg"/>
            </item>
            <item>
              <title>Second</title>
              <link>https://news.example.test/articles/2</link>
              <pubDate>Mon, 21 Sep 2026 06:00:00 -0400</pubDate>
              <enclosure url="https://news.example.test/audio/2.mp3" type="audio/mpeg" length="1"/>
              <enclosure url="https://news.example.test/img/2.png" type="image/png" length="1"/>
            </item>
          </channel>
        </rss>
        """;

    private const string Atom = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/">
          <title>Example Atom</title>
          <link href="https://atom.example.test/"/>
          <entry>
            <title type="html">Atom &lt;em&gt;entry&lt;/em&gt;</title>
            <link rel="self" href="https://atom.example.test/api/entries/1"/>
            <link rel="alternate" href="https://atom.example.test/entries/1"/>
            <link rel="enclosure" type="image/jpeg" href="https://atom.example.test/img/1.jpg"/>
            <id>tag:atom.example.test,2026:1</id>
            <updated>2026-09-21T12:00:00Z</updated>
            <published>2026-09-21T09:15:00+02:00</published>
            <media:content url="https://atom.example.test/video.mp4" medium="video"/>
            <summary>Short summary.</summary>
            <content type="html">&lt;p&gt;Long content.&lt;/p&gt;</content>
          </entry>
          <entry>
            <title>No summary, only content</title>
            <link href="/entries/2"/>
            <id>tag:atom.example.test,2026:2</id>
            <updated>2026-09-20T12:00:00Z</updated>
            <content type="html">&lt;p&gt;Only content.&lt;/p&gt;</content>
          </entry>
        </feed>
        """;

    private const string Rdf = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/"
                 xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel rdf:about="https://rdf.example.test/">
            <title>Example RDF</title>
            <link>https://rdf.example.test/</link>
            <items><rdf:Seq><rdf:li rdf:resource="https://rdf.example.test/a/1"/></rdf:Seq></items>
          </channel>
          <item rdf:about="https://rdf.example.test/a/1?ref=rdf">
            <title>RDF item</title>
            <link>https://rdf.example.test/a/1</link>
            <description>RDF description.</description>
            <dc:date>2026-09-21T08:00:00Z</dc:date>
          </item>
        </rdf:RDF>
        """;

    [Fact]
    public void Parse_Rss_ReadsItems()
    {
        var result = FeedParser.Parse(Rss);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);

        var first = result.Value[0];
        Assert.Equal("example-guid-1", first.ExternalId);
        Assert.Equal("First & foremost", first.Title);
        Assert.Equal("https://news.example.test/articles/1", first.Link);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 10, 30, 0, TimeSpan.Zero), first.PublishedAt);
        Assert.Equal("https://news.example.test/img/1-thumb.jpg", first.ImageUrl);
    }

    [Fact]
    public void Parse_Rss_SummaryIsPlainText_WithoutScriptsOrTags()
    {
        var summary = FeedParser.Parse(Rss).Value[0].Summary;

        Assert.Equal("Hello world. Second paragraph.", summary);
    }

    [Fact]
    public void Parse_Rss_WithoutGuid_FallsBackToLink_AndPicksTheImageEnclosure()
    {
        var second = FeedParser.Parse(Rss).Value[1];

        Assert.Equal("https://news.example.test/articles/2", second.ExternalId);
        Assert.Equal("https://news.example.test/img/2.png", second.ImageUrl);
        Assert.Null(second.Summary);
        // -0400 offset is normalised to UTC.
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero), second.PublishedAt);
    }

    [Fact]
    public void Parse_Atom_ReadsEntries_PreferringAlternateLinkSummaryAndPublished()
    {
        var result = FeedParser.Parse(Atom, new Uri("https://atom.example.test/feed.xml"));

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);

        var first = result.Value[0];
        Assert.Equal("tag:atom.example.test,2026:1", first.ExternalId);
        Assert.Equal("Atom entry", first.Title);
        Assert.Equal("https://atom.example.test/entries/1", first.Link);
        Assert.Equal("Short summary.", first.Summary);
        Assert.Equal("https://atom.example.test/img/1.jpg", first.ImageUrl);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 7, 15, 0, TimeSpan.Zero), first.PublishedAt);
    }

    [Fact]
    public void Parse_Atom_FallsBackToContentAndUpdated_AndResolvesRelativeLinks()
    {
        var second = FeedParser.Parse(Atom, new Uri("https://atom.example.test/feed.xml")).Value[1];

        Assert.Equal("https://atom.example.test/entries/2", second.Link);
        Assert.Equal("Only content.", second.Summary);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero), second.PublishedAt);
        Assert.Null(second.ImageUrl);
    }

    [Fact]
    public void Parse_Atom_RelativeLinkWithoutBaseUri_SkipsTheEntry()
    {
        var result = FeedParser.Parse(Atom);

        Assert.Equal("tag:atom.example.test,2026:1", Assert.Single(result.Value).ExternalId);
    }

    [Fact]
    public void Parse_Rdf_ReadsItems_UsingRdfAboutAndDcDate()
    {
        var result = FeedParser.Parse(Rdf);

        Assert.False(result.IsError);
        var item = Assert.Single(result.Value);
        Assert.Equal("https://rdf.example.test/a/1?ref=rdf", item.ExternalId);
        Assert.Equal("RDF item", item.Title);
        Assert.Equal("https://rdf.example.test/a/1", item.Link);
        Assert.Equal("RDF description.", item.Summary);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero), item.PublishedAt);
    }

    [Fact]
    public void Parse_Rss_GuidPermalink_StandsInForAMissingLink()
    {
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>Permalink only</title><guid>https://news.example.test/p/9</guid></item>
            </channel></rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml).Value);

        Assert.Equal("https://news.example.test/p/9", item.Link);
        Assert.Equal("https://news.example.test/p/9", item.ExternalId);
    }

    [Fact]
    public void Parse_Rss_NonPermalinkGuid_DoesNotStandInForAMissingLink()
    {
        // isPermaLink="false" says the guid is only an identifier, even when it happens to look like a URL.
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>Id only</title><guid isPermaLink="false">https://ids.example.test/opaque/9</guid></item>
              <item><title>Linked</title><guid isPermaLink="false">https://ids.example.test/opaque/10</guid><link>https://news.example.test/p/10</link></item>
            </channel></rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml).Value);

        Assert.Equal("Linked", item.Title);
        Assert.Equal("https://news.example.test/p/10", item.Link);
        Assert.Equal("https://ids.example.test/opaque/10", item.ExternalId);
    }

    [Theory]
    [InlineData("<link>javascript:alert(1)</link>")]
    [InlineData("<link>ftp://news.example.test/file</link>")]
    [InlineData("<link>   </link>")]
    [InlineData("")]
    public void Parse_Rss_ItemWithoutAnHttpLink_IsSkipped(string linkXml)
    {
        var xml = $"""
            <rss version="2.0"><channel>
              <item><title>Bad</title>{linkXml}<guid isPermaLink="false">bad-1</guid></item>
              <item><title>Good</title><link>https://news.example.test/good</link></item>
            </channel></rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml).Value);

        Assert.Equal("Good", item.Title);
    }

    [Fact]
    public void Parse_NonHttpImage_IsIgnored()
    {
        const string xml = """
            <rss version="2.0"><channel><item>
              <title>T</title><link>https://news.example.test/1</link>
              <enclosure url="data:image/png;base64,AAAA" type="image/png"/>
            </item></channel></rss>
            """;

        Assert.Null(Assert.Single(FeedParser.Parse(xml).Value).ImageUrl);
    }

    [Fact]
    public void Parse_MissingTitle_UsesPlaceholder()
    {
        const string xml = """
            <rss version="2.0"><channel><item><link>https://news.example.test/1</link></item></channel></rss>
            """;

        Assert.Equal(FeedParser.UntitledTitle, Assert.Single(FeedParser.Parse(xml).Value).Title);
    }

    [Fact]
    public void Parse_LongSummary_IsTruncatedWithEllipsis()
    {
        var xml = $"""
            <rss version="2.0"><channel><item>
              <title>T</title><link>https://news.example.test/1</link>
              <description>{new string('x', FeedParser.SummaryMaxLength * 2)}</description>
            </item></channel></rss>
            """;

        var summary = Assert.Single(FeedParser.Parse(xml).Value).Summary!;

        Assert.Equal(FeedParser.SummaryMaxLength, summary.Length);
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public void Parse_EscapedMarkupInDescription_IsStrippedAfterDecoding()
    {
        const string xml = """
            <rss version="2.0"><channel><item>
              <title>T</title><link>https://news.example.test/1</link>
              <description>&amp;lt;p&amp;gt;Double escaped&amp;lt;/p&amp;gt; &amp;amp; done</description>
            </item></channel></rss>
            """;

        Assert.Equal("Double escaped & done", Assert.Single(FeedParser.Parse(xml).Value).Summary);
    }

    [Theory]
    [InlineData("Mon, 21 Sep 2026 10:30:00 GMT", "2026-09-21T10:30:00Z")]
    [InlineData("Mon, 21 Sep 2026 10:30:00 +0000", "2026-09-21T10:30:00Z")]
    [InlineData("Mon, 21 Sep 2026 10:30:00 +0200", "2026-09-21T08:30:00Z")]
    [InlineData("Mon, 21 Sep 2026 06:30:00 EDT", "2026-09-21T10:30:00Z")]
    [InlineData("Mon, 21 Sep 2026 03:30:00 pst", "2026-09-21T11:30:00Z")]
    [InlineData("21 Sep 2026 10:30 UT", "2026-09-21T10:30:00Z")]
    [InlineData("Mon, 21 Sep 2026 17:00:00 BST", "2026-09-21T16:00:00Z")] // as published by Sky Sports
    [InlineData("Mon, 21 Sep 2026 12:30:00 CEST", "2026-09-21T10:30:00Z")]
    [InlineData("Fri, 21 Sep 2026 10:30:00 GMT", "2026-09-21T10:30:00Z")] // wrong weekday (it is a Monday)
    [InlineData("Mon, 21 Sep 2026 10:30:00", "2026-09-21T10:30:00Z")] // no zone: taken as UTC
    [InlineData("2026-09-21T10:30:00Z", "2026-09-21T10:30:00Z")]
    [InlineData("2026-09-21T12:30:00+02:00", "2026-09-21T10:30:00Z")]
    [InlineData("2026-09-21T12:30:00+0200", "2026-09-21T10:30:00Z")]
    [InlineData("2026-09-21T10:30:00.123Z", "2026-09-21T10:30:00.123Z")]
    [InlineData("2026-09-21", "2026-09-21T00:00:00Z")]
    public void ParseDate_ReadsRfc822AndIso8601_AsUtc(string input, string expectedUtc)
    {
        Assert.Equal(DateTimeOffset.Parse(expectedUtc), FeedParser.ParseDate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday-ish")]
    [InlineData("Mon, 21 Sep 2026 10:30:00 XYZT")] // unknown zone: better no date than a wrong one
    [InlineData("32 Foo 2026")]
    public void ParseDate_Unreadable_IsNull(string? input)
    {
        Assert.Null(FeedParser.ParseDate(input));
    }

    [Fact]
    public void Parse_BadDate_KeepsTheItemWithoutADate()
    {
        const string xml = """
            <rss version="2.0"><channel><item>
              <title>T</title><link>https://news.example.test/1</link><pubDate>not a date</pubDate>
            </item></channel></rss>
            """;

        Assert.Null(Assert.Single(FeedParser.Parse(xml).Value).PublishedAt);
    }

    [Fact]
    public void Parse_MalformedXml_IsAnError()
    {
        var result = FeedParser.Parse("<rss><channel><item></rss>");

        Assert.True(result.IsError);
        Assert.Equal("Polling.InvalidXml", result.FirstError.Code);
    }

    [Theory]
    [InlineData("<html><body>Not a feed</body></html>")]
    [InlineData("<opml version=\"2.0\"><body/></opml>")]
    public void Parse_WellFormedButNotAFeed_IsAnError(string xml)
    {
        var result = FeedParser.Parse(xml);

        Assert.True(result.IsError);
        Assert.Equal(PollingErrors.UnrecognizedFormat.Code, result.FirstError.Code);
    }

    [Fact]
    public void Parse_EmptyFeed_IsAnEmptyList()
    {
        var result = FeedParser.Parse("<rss version=\"2.0\"><channel><title>Empty</title></channel></rss>");

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Parse_Doctype_IsIgnored_AndEntitiesAreNeverExpanded()
    {
        // Classic XXE probe: with DTD processing ignored the entity is undefined, so the document is rejected
        // rather than the file being read.
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///c:/windows/win.ini">]>
            <rss version="2.0"><channel><item>
              <title>&xxe;</title><link>https://news.example.test/1</link>
            </item></channel></rss>
            """;
        const string harmlessDoctype = """
            <?xml version="1.0"?>
            <!DOCTYPE rss PUBLIC "-//Netscape Communications//DTD RSS 0.91//EN" "http://my.netscape.com/publish/formats/rss-0.91.dtd">
            <rss version="0.91"><channel><item>
              <title>Old school</title><link>https://news.example.test/1</link>
            </item></channel></rss>
            """;

        Assert.True(FeedParser.Parse(xxe).IsError);
        Assert.Equal("Old school", Assert.Single(FeedParser.Parse(harmlessDoctype).Value).Title);
    }

    [Fact]
    public void Parse_Bytes_HonoursTheDeclaredEncoding_AndLeadingWhitespace()
    {
        const string xml = """


            <?xml version="1.0" encoding="ISO-8859-1"?>
            <rss version="2.0"><channel><item>
              <title>Café Zürich</title><link>https://news.example.test/1</link>
            </item></channel></rss>
            """;

        var result = FeedParser.Parse(Encoding.Latin1.GetBytes(xml));

        Assert.False(result.IsError);
        Assert.Equal("Café Zürich", Assert.Single(result.Value).Title);
    }

    [Fact]
    public void Parse_Bytes_WithUtf8Bom_Works()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Rss)).ToArray();

        var result = FeedParser.Parse(bytes);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
    }
}
