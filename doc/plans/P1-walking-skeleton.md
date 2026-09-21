# P1 — Phase 1 Walking Skeleton (S1–S5) — LuminaFeed

> **INVIOLABLE SESSION RULE (verbatim):**
>
> You must NEVER, EVER change anything beyond the contents of your working directory. You may however read files that were gitignored in your working directory, which you will find in d:\Work\Sonrisa\LuminaFeed\. This is a rule you must not breach for the entirety of this session

Consequences of the rule for this plan: **no new NuGet packages** (a restore would download into the global
NuGet cache outside the working directory), no writes to the Claude memory/scratchpad folders, no pushes. Builds and
tests write only `bin/`/`obj/` here (plus the temp SQLite file the pre-existing `HostBootTests` harness already creates).

---

## Context

Phase 0 (groundwork) is complete: domain model + EF persistence, `Admin` policy, typed options, MailKit email
infrastructure with the Outlook-safe `EmailLayout`, and the seeded catalogue (10 categories / 115 feeds). Phase 1 of
[`00-master-plan.md`](./00-master-plan.md) builds **one deliberately thin end-to-end path** to de-risk integration:

> admin adds a feed → it appears publicly → a signed-in user subscribes → polling fetches new articles → the user
> receives an email.

Everything is happy-path/minimal; polish is Phase 2. Each step ships with xUnit tests and a `doc/features/` doc, ticks
its master-plan checkbox, and is **committed separately** (this plan first, then S1 … S5).

## Decisions confirmed with the user

1. **First-poll backlog = capped.** On a feed's *first* poll every item is new; all are persisted, but the
   notification lists only the **newest 5** (configurable: `Polling:FirstPollNotificationCap`). Later polls notify
   every new article.
2. **RSS parsing = hand-rolled `XDocument` parser** (RSS 2.0 + Atom 1.0 + RDF/RSS 1.0 — the catalogue contains all
   three, e.g. Deutsche Welle is RDF). No new package; tolerant of sloppy dates.
3. **Verification = tests + app logs only.** Papercut is not running on `localhost:25`, so the live mailbox
   walkthrough is left to the user; delivery is asserted with a capturing `IMailSender` and a fake HTTP fetcher.

## Cross-cutting design

- **`IDbContextFactory<ApplicationDbContext>`** replaces `AddDbContext` in `Program.cs`. Interactive Server circuits
  and the singleton polling background service both outlive a request, so services create a **short-lived context per
  operation** instead of sharing a circuit-scoped one (avoids "a second operation was started" and stale tracking).
  `AddDbContextFactory` still registers a scoped `ApplicationDbContext`, so Identity stores and the seeders are
  unchanged. `HostBootTests` is adjusted to re-point the factory.
- **Services** live in `Services/<Area>/`, return **`ErrorOr<T>`**, and validate requests with **FluentValidation**
  (validators registered via `AddValidatorsFromAssemblyContaining<Program>()`). Validation failures map to
  `Error.Validation(property, message)`; uniqueness → `Error.Conflict`; missing rows → `Error.NotFound`.
- **UI**: new pages use `@rendermode InteractiveServer`, stay thin (no business logic), and render `ErrorOr` errors in
  a Bootstrap alert. No component-test library is available without a new package, so page behaviour is covered by
  service tests plus `WebApplicationFactory` page smoke tests (shared `TestAppFactory`).
- **Test idiom** (unchanged): real in-memory SQLite over a kept-open connection + `EnsureCreated`, hand-written fakes,
  `NullLogger`/`ListLogger`.

---

## S1 — Admin: create category + feed (minimal create/list)

**Code**
- `Program.cs`: `AddDbContextFactory`, validator registration, service registrations.
- `Services/ErrorOrExtensions.cs` — `ValidationResult → List<Error>` helper.
- `Services/Categories/`: `CreateCategoryRequest` (+ validator: name required ≤100, description ≤1000),
  `CategoryErrors`, `ICategoryService` / `CategoryService` (`ListAsync` ordered by name with feed counts,
  `CreateAsync` with trimmed input + duplicate-name conflict).
- `Services/Feeds/`: `CreateFeedRequest` (+ validator: name ≤200, category required, absolute http/https `FeedUrl` /
  `SiteUrl` ≤2048, optional absolute `ImageUrl`, description ≤1000, popularity ≥0), `FeedErrors`, `IFeedService` /
  `FeedService` (`ListAsync` with category, `CreateAsync` with category-exists + duplicate-URL checks).
- `Components/Admin/Categories.razor` (`/admin/categories`), `Components/Admin/Feeds.razor` (`/admin/feeds`,
  category **dropdown**), both `[Authorize(Policy = "Admin")]`; `AdminHome.razor` links to them.

**Tests** — `CategoryServiceTests`, `FeedServiceTests` (validation, trimming, conflict, not-found, list ordering),
`AdminPagesTests` (anonymous request to an admin page is redirected to login).
**Docs** — `doc/features/admin-catalog-management.md`; READMEs; master-plan tick.

## S2 — Public: minimal feed list

**Code**
- `IFeedService.ListByCategoryAsync` → categories (by name) each with their feeds (by popularity desc — the fixed
  default; no ordering control/filter/pagination yet).
- `Components/Pages/Home.razor` — feeds as Bootstrap **cards** (image when available, name, description, site link),
  grouped under category headings. `Components/Shared/FeedCard.razor`.
- Remove the template sample pages (`Counter`, `Weather`, `Auth`) and their nav links.

**Tests** — service grouping/ordering; `PublicPagesTests`: `GET /` returns 200 and contains seeded feed names.
**Docs** — `doc/features/public-feed-list.md`; master-plan tick.

## S3 — Subscribe (email only)

**Code**
- `Services/Subscriptions/`: `SubscriptionErrors`, `ISubscriptionService` / `SubscriptionService` —
  `SubscribeByEmailAsync(userId, feedId)` (idempotent: creates the row with `EmailEnabled = true`, or re-enables it),
  `UnsubscribeAsync(userId, feedId)` (deletes the row — a deliberately minimal toggle so a test subscriber is not
  emailed forever), `GetSubscribedFeedIdsAsync(userId)`.
- `FeedCard` gets the button: **Subscribe** (primary) / **Unsubscribe** (red) for signed-in users; anonymous users
  see a "Log in to subscribe" link carrying `ReturnUrl`. The user id always comes from the authenticated principal.
- Skipped until C1: dialog, Slack switch, redirect polish, per-channel unsubscribe.

**Tests** — `SubscriptionServiceTests` (create, idempotency, re-enable, unknown feed, unsubscribe, not-subscribed,
listing isolation per user).
**Docs** — `doc/features/subscriptions.md`; master-plan tick.

## S4 — Polling (minimal)

**Code** (`Services/Polling/`)
- `FeedParser` (static) → `ParsedFeedItem(ExternalId, Title, Link, Summary?, ImageUrl?, PublishedAt?)`. Handles RSS
  2.0, Atom, RDF; `ExternalId` = `<guid>`/`<id>` else link; tolerant date parsing; summary is tag-stripped,
  entity-decoded and truncated; image from `enclosure` / `media:content` / `media:thumbnail` when present. DTDs are
  ignored and no external resolver is used (XXE-safe).
- `IFeedFetcher` / `HttpFeedFetcher` — typed `HttpClient` (30 s timeout, 10 MB cap, browser-compatible
  `User-Agent`), returns `ErrorOr<string>`.
- `IFeedPollingService` / `FeedPollingService.PollAsync` — selects feeds with **≥1 subscription**, and per feed
  (own `DbContext`, failures isolated + logged): fetch → parse → insert only unseen `(FeedId, ExternalId)` rows →
  stamp `LastPolledAt`. Returns the spec's **dictionary keyed by subscriber email → new articles** (email-enabled
  subscriptions only), applying the first-poll cap.
- `FeedPollingBackgroundService` — `BackgroundService` + `PeriodicTimer(PollingOptions.Interval)`; polls once at
  startup then every interval, a fresh DI scope per cycle, and **never lets an exception escape** (an unhandled
  exception in a `BackgroundService` stops the host). `TimeProvider` injected for testability.
- `PollingOptions.FirstPollNotificationCap` (default 5, ≥0) + appsettings.
- Deferred to C4: ETag / Last-Modified / 304, `og:image` scraping, Slack dispatch.

**Tests** — `FeedParserTests` (RSS/Atom/RDF fixtures, guid fallback, bad dates, HTML summary, images, malformed XML,
DTD), `HttpFeedFetcherTests` (stub handler: success, non-2xx, exception), `FeedPollingServiceTests` (only subscribed
feeds polled, de-dup across polls and within a document, first-poll cap, later polls uncapped, email-disabled
subscribers excluded, fetch/parse failure isolated, `LastPolledAt` stamped, field truncation),
`FeedPollingBackgroundServiceTests`, options tests.
**Docs** — `doc/features/feed-polling.md`; `configuration-options.md`; master-plan tick.

## S5 — Email notification (minimal)

**Code** (`Services/Notifications/`)
- `NotificationChannel` (**Ardalis.SmartEnum**: `Email`, `Slack`), `ArticleNotification(RecipientEmail, Articles)`.
- `INotificationService { Channel; Task<ErrorOr<Success>> NotifyAsync(...) }`.
- `EmailNotificationService` — composes a plain, table-based (via `EmailLayout`) new-article email: articles grouped
  by feed, HTML-encoded titles/links/summaries, plus a text alternative; sends through `IMailSender`; transport
  failures become an `Error` (logged) rather than an exception.
- `FeedPollingBackgroundService` dispatches each poll result entry to the registered `INotificationService`s for the
  `Email` channel; one failing recipient never blocks the rest.
- Deferred: Slack (C2), rich templates + Outlook buttons (C3), unsubscribe link/headers (C5), retry of failed sends (C4).

**Tests** — `EmailNotificationServiceTests` (recipient/subject singular+plural, grouping, encoding of hostile titles,
table-based body, text alternative, empty list is a no-op, transport failure → error + log),
background-service dispatch tests (per-recipient dispatch, failure isolation), host-boot DI resolution.
**Docs** — `doc/features/notifications.md`; `Services/README.md`; master-plan tick + Phase 1 definition of done.

---

## Verification (each step, and at the end)

1. `dotnet build` — 0 warnings, 0 errors.
2. `dotnet test` — all green including the new tests.
3. After S5: boot the real app from the working directory (Development) and confirm from the logs that migrations,
   seeding and the polling loop start cleanly, and that `/` serves the feed cards.
4. `git status` clean after each commit; commits: plan, S1, S2, S3, S4, S5.
