# Feature: Feed Polling (S4 — minimal)

A background task polls the subscribed feeds on an interval, stores the items it hasn't seen as `Article`s, and hands
back what to notify about. This is the walking-skeleton slice; **C4** hardens it (ETag / Last-Modified / 304,
`og:image` scraping, Slack dispatch).

## Flow

```
FeedPollingBackgroundService ──every Polling:IntervalSeconds──▶ IFeedPollingService.PollAsync
   for each feed with ≥ 1 subscription:
      IFeedFetcher.FetchAsync(FeedUrl) ─▶ FeedParser.Parse ─▶ insert unseen (FeedId, ExternalId) ─▶ LastPolledAt = now
   returns  email address ─▶ [new articles]      (dispatched to the notification services — see Notifications)
```

## Components (`LuminaFeed/Services/Polling/`)

- **`FeedPollingBackgroundService`** — `BackgroundService` + `PeriodicTimer(PollingOptions.Interval)`. Runs one pass at
  startup, then one per interval; each pass resolves its services from a **fresh DI scope**. A pass **never lets an
  exception escape** (an unhandled exception in a `BackgroundService` stops the host): failures are logged and the next
  tick tries again; only shutdown cancellation propagates.
- **`IFeedPollingService` → `FeedPollingService`** — one pass:
  - **Active feeds only** — feeds with at least one subscription; the rest of the catalogue is never fetched.
  - **Isolation** — each feed gets its own `DbContext` and its own try/catch, so a broken feed (fetch error, unparsable
    document, failed save) is logged and skipped without costing the others their poll.
  - **De-dup** — candidates are de-duplicated within the document, then filtered against the stored
    `(FeedId, ExternalId)` rows; only unseen items are inserted. The same guid in two feeds is two articles.
  - **Column fit** — title and external id are truncated to the entity's own limits (`Article.TitleMaxLength`,
    `Article.ExternalIdMaxLength`); an item whose link is too long is dropped, and an over-long image URL is discarded
    (a truncated URL is a broken URL).
  - **`LastPolledAt`** is stamped on **successful** polls only. Per-feed outcomes are logged at **Debug** (they fire
    for every active feed every interval); the loop logs the pass summary at Information.
  - **Result** — the spec's dictionary keyed by **subscriber email address** (case-insensitive) → the new articles to
    notify about as **`NewArticle` values** (article id, feed id + name, title, link, summary, image, date — see
    [Notifications](./notifications.md)), never the tracked entities. Only subscriptions with `EmailEnabled` and a
    **confirmed** account email are included; a subscriber of several feeds gets one combined list, newest first per
    feed. The key stays the email address; with per-subscriber channel dispatch (C4) the value grows into a
    per-subscription digest (channels, Slack webhook, subscription id).
- **First-poll / catch-up cap** — the first successful poll of a feed finds its whole backlog "new". Everything is
  stored, but only the **newest `Polling:FirstPollNotificationCap` (default 5)** are reported (by publication date;
  undated items rank last). The same cap applies to a **catch-up** poll: one where the feed's `LastPolledAt` is older
  than **`Polling:CatchUpAfterMinutes` (default 360)** — it lost all its subscribers for a while, or the host was down
  — so a returning subscriber is not sent the whole gap. Polls within the window report every new article. `0` makes
  first/catch-up polls a silent baseline. Because a failed poll leaves `LastPolledAt` unchanged, a feed that was
  unreachable at first still gets the cap when it finally answers.
- **`IFeedFetcher` → `HttpFeedFetcher`** — typed `HttpClient`: 30 s timeout, **10 MB** response cap, automatic
  gzip/deflate/brotli, a browser-compatible `User-Agent`, feed `Accept` types. Returns `ErrorOr<byte[]>` — non-2xx,
  transport errors, timeouts, oversize bodies and bad URLs are **errors, not exceptions**; caller cancellation still
  propagates. Redirects are followed by the handler, except the https → http downgrade it refuses: several publishers
  (the Future plc sites) redirect their https feed URL to an `http://` one that just bounces back, so those are
  followed here with the scheme **upgraded to https** (max 3 hops) — a fetch never leaves TLS.
- **`FeedParser`** — a small, tolerant reader over `XDocument` for the three formats in the catalogue: **RSS 2.0**,
  **Atom 1.0**, **RDF / RSS 1.0**. Elements are matched by local name.
  - `ExternalId` = RSS `<guid>` / Atom `<id>` / RDF `rdf:about`, else the link. An RSS guid stands in for a missing
    `<link>` unless it says `isPermaLink="false"` (then it is only an id, however URL-like); Atom prefers
    `rel="alternate"`; relative links resolve against the feed URL.
  - **Only absolute http(s) links and images are accepted** — an item without one is skipped, so `javascript:` /
    `data:` URLs can never reach a page or an email.
  - Summary (`description` / `content:encoded` / `summary` / `content`) becomes plain text: scripts/styles and tags
    removed, entities decoded (twice-escaped markup too), whitespace collapsed, capped at 1000 characters.
    **Bounded work on hostile input:** only the first 64 KB (`FeedParser.MarkupMaxLength`) of a title or description
    is examined, and markup is stripped in a single linear scan rather than by backtracking regexes, so a
    publisher-controlled value (a megabyte of unclosed `<script>` openers, say) cannot stall the pass.
  - Image from `enclosure` / Atom enclosure link (image types), `media:thumbnail`, `media:content`.
  - Dates: ISO 8601 and RFC 822 including `+hhmm` offsets, named zones (GMT, EST…, BST, CEST…) and a wrong weekday;
    an unreadable date (or unknown zone) leaves `PublishedAt` null rather than guessing. Missing title → `(untitled)`.
  - **Untrusted XML**: DTDs are ignored and no external resolver is used (no XXE); bytes are decoded per the XML
    declaration/BOM, tolerating blank lines before the declaration.

## Configuration (`Polling`)

| Key | Default | Meaning |
|---|---|---|
| `IntervalSeconds` | `60` | Seconds between polling passes (1 … 86 400). |
| `FirstPollNotificationCap` | `5` | Newest N articles reported on a feed's first successful poll, or on a catch-up poll (≥ 0). |
| `CatchUpAfterMinutes` | `360` | A feed last polled longer ago than this is treated like a first poll (≥ 1; keep it well above the interval). |

## Validated against the live catalogue (2026-09-21)

A one-off run of the real fetcher + parser over all 115 seeded feeds: **114 fetched and parsed** (3,539 items, 99 %
with a readable date, 0 parse failures). Known outliers for C4: *New Scientist* answers **406** to .NET's HTTP stack
(curl with identical headers gets 200 — apparently bot fingerprinting); *Space.com* and *NYT Sports* currently publish
an empty channel; some *CNN Politics* items have no title (shown as `(untitled)`).

## Known limitations (deliberate, until C4)

Every poll is a full GET (no conditional requests); feeds are polled sequentially; an admin-supplied feed URL is
fetched as-is (admins are trusted — there is no private-network filter); two app instances polling at once can race
on the unique index (the loser's feed is logged and retried next tick).

## Tests

`FeedParserTests` (RSS/Atom/RDF fixtures, guid/link fallbacks incl. `isPermaLink="false"`, relative links, non-http
links and images, HTML and double-escaped summaries, truncation, date formats, malformed/non-feed XML, DOCTYPE + XXE
probe, declared encoding, BOM), `HttpFeedFetcherTests` (body + headers, non-2xx, transport failure, timeout, caller cancellation, bad URL,
oversize body, https-upgraded redirects, redirect loop, client/handler configuration), `FeedPollingServiceTests` (active
feeds only, persistence + `LastPolledAt`, email-keyed result, de-dup across polls / within a document / per feed,
first-poll cap by date, uncapped later polls, catch-up cap after the window / uncapped within it (`FakeTimeProvider`),
cap 0, failed first fetch keeps the cap, email-enabled + confirmed only,
multi-feed subscriber, broken-feed isolation, cancellation, column fit; `LimitsTests` keeps the entity constants equal
to the mapped columns), `FeedPollingBackgroundServiceTests`
(immediate first pass, clean stop, keeps ticking after a throwing pass, missed ticks coalesce, fresh scope per pass —
all on a `FakeTimeProvider`, so no test waits for real time), `OptionsTests`,
`HostBootTests` (the loop is hosted and running). Host-booting tests swap in a `NoNetworkFeedFetcher`, so no test
ever reaches a real publisher — and, apart from that one hosting check, `TestAppFactory` removes the loop's hosted
service so a live timer can never race a test's own polling cycles against the same database (`WalkingSkeletonTests`
drives its own `FeedPollingBackgroundService` instance by hand).
