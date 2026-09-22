# Feature: Feed Page (B4)

A per-feed page (`/feed/{FeedId:guid}`) listing that feed's latest articles as cards, with a subscribe button at the
top. Reached from the **View articles** button on each public card (see [Public Feed List](./public-feed-list.md)).

## Page (`LuminaFeed/Components/Pages/Feed.razor`)

- Route `/feed/{FeedId:guid}` — the `:guid` constraint rejects a non-GUID id as a 404. Anonymous-accessible,
  `@rendermode InteractiveServer`.
- **Load.** On init it reads the user id from the authenticated principal (never from client input); on parameter
  set it loads `IFeedService.GetFeedDetailAsync(FeedId, maxArticles: 50)`. States: _loading_, _"Feed not found"_
  (with a link home) when the feed does not exist, otherwise the header + articles. Re-loads when navigated to a
  different feed id.
- **Header.** A back link to `/`, the feed name and category, the description when present, and a top action bar with
  a **Visit site** link (the publisher, external, `rel="noopener noreferrer"`) and a **Subscribe** button.
- **Subscribe.** Reuses the same [`SubscribeButton`](./subscriptions.md) as the public list: an anonymous visitor
  gets a login link carrying a `ReturnUrl` back to this feed page; a signed-in user gets Subscribe / red Unsubscribe,
  toggled through `ISubscriptionService` with a busy gate, exactly like the home page. Errors render through
  `ErrorList`.
- **Persisted state.** Only a single `[PersistentState] bool? Subscribed` crosses the prerender → circuit hand-off,
  so the button does not flicker Subscribe→Unsubscribe on the interactive render; it is one bool, so the 32 KB
  circuit-start payload stays tiny. The articles are **re-queried** on the interactive render, never persisted (a
  list of summaries would be far too large — the same reasoning as the home page).
- **Articles.** An `ArticleCard` per article in a responsive grid, or _"No articles have been fetched for this feed
  yet."_ when empty (articles only exist after polling). The newest 50 are shown; there is no per-page "more" (the
  spec caps only the main view).

## Article card (`LuminaFeed/Components/Shared/ArticleCard.razor`)

The "different kind of card" the spec calls for: the article **image** when present (letterboxed, lazy-loaded,
`referrerpolicy="no-referrer"`), the **title** as an external link to the article, the **published date**, and the
**first paragraphs** (`Summary`) — each subject to availability (all are nullable and guarded). Everything comes from
an untrusted feed, but the `Summary` is already plain text (the parser strips markup) and links/images are validated
absolute http(s); Blazor auto-encodes `@expr`, so nothing here needs manual encoding — and it must never use
`MarkupString`.

## Service (`IFeedService.GetFeedDetailAsync`)

Returns `FeedDetail(FeedSummary Feed, IReadOnlyList<ArticleSummary> Articles)`, or `null` when the feed does not
exist. Articles are ordered **newest first** (`PublishedAt` descending), with dateless articles last and `FetchedAt`
breaking ties, then capped at `maxArticles`.

- **Ordering is done in memory, not in SQL.** EF Core's SQLite provider throws `NotSupportedException` for a
  `DateTimeOffset` in an `ORDER BY` clause, so the feed's rows are read (filtered by `FeedId`, which the existing
  `(FeedId, ExternalId)` index serves on its prefix) and then sorted with LINQ-to-Objects — which orders a null key
  last under `OrderByDescending`, giving the dateless-last behaviour. This matches the notification path, which
  already sorts articles client-side. (Correct text ordering of the stored UTC `DateTimeOffset` — the codebase's UTC
  invariant — would apply if SQL ordering were possible; it is the reason the values are uniform, and it keeps the
  in-memory sort meaningful.)
- **Accepted-risk decision — the fetch is unbounded.** Because the sort cannot run in SQL, `GetFeedDetailAsync`
  reads **every** stored row for the feed into memory on **each** feed-page view, then sorts and takes the newest
  `maxArticles` (50). This is acceptable at current volumes and is kept as-is deliberately. It grows with retained
  articles, so the bound is a **deferred follow-up**, to be picked up with whichever lands first: the retention /
  cleanup job (see the master plan), or a sortable key column (e.g. a `long` UTC-ticks value, indexed per feed) that
  would let the query `ORDER BY … LIMIT` at the database and read only the page's worth of rows.

## Public card button (`LuminaFeed/Components/Shared/FeedCard.razor`)

Each public card carries a **View articles** link (`href="feed/{id}"`, `aria-label="View {name} articles"`) to this
page, alongside the existing external **Visit site** link and the page-supplied **Subscribe** button.

## Tests

- `FeedServiceTests` — `GetFeedDetailAsync`: newest-first ordering with dateless-last, other feeds' articles excluded,
  the cap honoured (newest kept), article fields mapped, a feed with no articles returns an empty list, and an
  unknown feed returns `null`.
- `ArticleCardComponentTests` (bUnit) — renders the title link (with `rel="noopener noreferrer"`), summary, image and
  date when present; omits image/summary/date when absent.
- `FeedPageComponentTests` (bUnit, interactive over real services) — header + article cards + Visit site; anonymous
  Subscribe is a login link with a `ReturnUrl`; a signed-in Subscribe toggles to red Unsubscribe and persists a row;
  an already-subscribed user sees red Unsubscribe; an unknown feed shows the not-found message; a feed without
  articles shows the empty state.
- `PublicPagesTests` — the real host serves `/feed/{id}` (with a seeded article) anonymously with the feed name and
  article, keeping the persisted payload small; an unknown-but-valid guid returns 200 with the not-found message; a
  non-GUID route returns 404; and every visible card on `/` carries a "View articles" link.
- Interactivity (circuit boot on the feed page, "View articles" navigation, article cards after hydration, the
  anonymous subscribe login link, and the not-found page) is verified in a real browser against an isolated app
  instance, per the "prove it in a browser" convention — bUnit does not run `blazor.web.js`.
