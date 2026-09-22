# LuminaFeed — Master Plan (High-Level Roadmap)

> This is a **roadmap / ordered TODO**, not an implementation plan. Each item marked 🗂 gets
> its own detailed plan in `doc/plans/` before it is built.

## Context

LuminaFeed is greenfield on top of the stock **.NET 10 Blazor Web App (Individual Accounts)**
template (Interactive Server render mode). Identity is fully scaffolded (login/register/manage/
2FA/passkeys), with one Identity migration, a live `app.db`, and Bootstrap. **Everything domain-
specific is unbuilt.** This plan orders the groundwork, then builds a thin end-to-end **walking
skeleton**, then broadens in parallel tracks. Source of truth for behaviour:
`doc/LuminaFeed Initial Specification.md`.

## Current state (already done by the template)

- ASP.NET Core Identity + EF Core (SQLite) + Blazor Server scaffold, working auth.
- Email is a **no-op sender**; `RequireConfirmedAccount = true` (so confirmation can't truly complete yet).
- No domain entities, services, background tasks, config sections, or test project.

## Conventions carried throughout (from CLAUDE.md)

- GUID **v7** IDs on all new entities · **ErrorOr** for errors · **FluentValidation** for validation.
- Each feature ships with **xUnit** tests, is documented in `doc/features/`, and gets a detailed
  plan in `doc/plans/` first.
- Structure: **single project, feature/layer folders** (`Domain/`, `Services/`, `Data/`, `Components/{Pages,Admin,Layout,Account}`).

---

## Phase 0 — Groundwork (prerequisites)

**The RSS feed research (G0.R) is the very first step and gates the rest** — the real categories,
the seeded feeds, and the fixed popularity figures all come out of it, and much of the schema and
seed data hinges on its results. `G0.3` is the long pole among the code tasks. Parallelism noted per item.

- [x] 🗂 **G0.R — RSS feed research (FIRST; spike, non-code).** Research available public RSS feeds and produce: a **curated seed list of feeds**, the **category set those feeds imply** (this replaces the spec's illustrative World News / Markets / Weather / Breaking News list), and the **fixed initial popularity figures** per feed. Deliverable is a data list (checked into `doc/`), consumed by G0.7. **Handed to a separate agent; the rest of Phase 0 continues once it's done.** ✅ **Done** — 115 validated feeds across 10 categories in [`doc/research/rss-feeds.json`](../research/rss-feeds.json) (+ [`.md`](../research/rss-feeds.md) companion); plan/outcome in [`G0.R-rss-feed-research.md`](./G0.R-rss-feed-research.md).
- [x] **G0.1 — Packages & test project.** Add ErrorOr, FluentValidation, Ardalis.SmartEnum (and MailKit, Slack.Webhooks). Add an **xUnit** test project to the solution. _(foundational; blocks all code)_
- [x] **G0.2 — Folder skeleton.** Create `Domain/`, `Services/`, `Components/Admin/`. _(trivial; with G0.1)_
- [x] 🗂 **G0.3 — Domain model + persistence.** Extend `ApplicationUser` (`IsAdmin`); add `Category`, `Feed` (incl. a **fixed `Popularity`** figure + an **image URL**), `Subscription` (per-channel prefs + Slack webhook), `Article` (incl. image URL); GUID-v7 ID convention; DbContext `DbSet`s + EF configs; **initial domain migration**. Schema only — seed data lands in G0.7. _(informed by G0.R; core prerequisite for nearly everything)_ — see [`G0.3-domain-model.md`](./G0.3-domain-model.md).
- [x] **G0.4 — Admin authorization.** Turn `IsAdmin` into an `"Admin"` authorization policy; guard an admin area + nav section. _(needs G0.3)_
- [x] **G0.5 — Config & options.** appsettings sections + typed options: Development **Papercut** SMTP (localhost), polling interval, HMAC unsubscribe secret. _(with G0.3; parallel)_
- [x] **G0.6 — Real email infra.** Replace `IdentityNoOpEmailSender` with a **MailKit** `IEmailSender<ApplicationUser>`; establish the **table-based, Outlook-safe** email layout foundation. Unblocks the real confirmation flow. _(needs G0.5; parallel with G0.3/G0.4)_
- [x] **G0.7 — Seed data.** Load the researched **categories + feeds + fixed popularity figures** (from G0.R) into the DB seeder. Also adds a **bootstrap admin seeder** (`AdminUserSeeder` + optional `AdminSeed` config) so the admin area is reachable on a fresh DB. _(needs G0.3 + G0.R)_ ✅ **Done** — embedded `rss-feeds.json` seeded via `DatabaseSeeder` (insert-missing-only, idempotent); startup runs `MigrateAsync` + seed (⚠️ auto-migrate is a temporary, not-production-ready shortcut). Plan: [`G0.7-seed-data.md`](./G0.7-seed-data.md); feature: [`../features/seed-data.md`](../features/seed-data.md).
- [x] 🗂 **G0.8 — Phase 0 review & remediation.** Senior architecture review of the Phase-0 commits and the fixes it surfaced: correct startup cancellation tokens, MailKit error handling, admin-seed fail-fast, a `(CategoryId, Popularity)` index for the default sort, `UserId` max length + single DB cascade path into `Subscriptions` (SQL-Server portability), targeted entity encapsulation, HTML-encoded email layout, `global.json` SDK pin, a real host-boot integration test, and the missing Phase-0 feature docs. _(after G0.1–G0.7)_ — see [`G0.8-phase-0-remediation.md`](./G0.8-phase-0-remediation.md).
- [x] 🗂 **G0.9 — Phase 0 review follow-up & polish.** Second independent review (verdict: Phase 0 is solid). In-scope polish: plain-text alternative on Identity emails; HTML-encode the values `IdentityEmailSender` interpolates into HTML; a direct-context `HasPendingModelChanges` drift guard test (the host-boot test suppresses the WAF false positive); and doc nits. Also **records the deferred stock-template-UI cleanup as an S2 checklist** and the **A3 security-stamp guard** (below). _(after G0.8)_ — see [`G0.9-phase-0-review-followup.md`](./G0.9-phase-0-review-followup.md).

**Groundwork parallelization:** `G0.R` first and gating. Then `G0.1 → {G0.2, G0.3, G0.5}`; `G0.4` after G0.3, `G0.6` after G0.5, and `G0.7` once G0.3 is done (G0.R already complete). `G0.8` reviews the finished phase.

---

## Phase 1 — Walking skeleton (Slice 1)

One deliberately thin path end-to-end to de-risk integration. Everything here is minimal
(happy-path only); polish comes in Phase 2. Introduces the `INotificationService` abstraction.

> Detailed plan for the whole phase: [`P1-walking-skeleton.md`](./P1-walking-skeleton.md).

- [x] **S1 — Admin: create category + feed.** Minimal create/list only. _(needs G0.3, G0.4)_ ✅ **Done** — `/admin/categories` + `/admin/feeds` over `ICategoryService` / `IFeedService` (ErrorOr + FluentValidation); persistence access switched to `IDbContextFactory`. Feature: [`../features/admin-catalog-management.md`](../features/admin-catalog-management.md).
- [x] **S2 — Public: minimal feed list.** Show the feed as a card. No ordering/filter/pagination. _(needs S1)_ ✅ **Done** — `/` lists every feed as a card grouped by category (fixed popularity order) via `IFeedService.ListByCategoryAsync`; template sample pages removed. Feature: [`../features/public-feed-list.md`](../features/public-feed-list.md).
- [x] **S3 — Subscribe (email only).** Signed-in user persists an email subscription; skip dialog/Slack/redirect polish. _(needs G0.3, S2)_ ✅ **Done** — `ISubscriptionService` (idempotent email subscribe + a minimal whole-row unsubscribe); Subscribe / red Unsubscribe button on each card, anonymous → login with `ReturnUrl`. Feature: [`../features/subscriptions.md`](../features/subscriptions.md).
- [x] **S4 — Polling (minimal).** Background service polls the subscribed feed on interval, fetches items, persists `Article`s. _(needs G0.3, G0.5, S3)_ ✅ **Done** — `FeedPollingBackgroundService` → `IFeedPollingService` (active feeds only, per-feed isolation, `(FeedId, ExternalId)` de-dup, email-keyed result, **first-poll cap** of the newest 5) over `HttpFeedFetcher` + a hand-rolled RSS/Atom/RDF `FeedParser`; live-validated on 114/115 seeded feeds. Feature: [`../features/feed-polling.md`](../features/feed-polling.md).
- [x] **S5 — Email notification (minimal).** `EmailNotificationService` (behind `INotificationService`) sends a plain new-article email to the subscriber. _(needs G0.6, S4)_ ✅ **Done** — `INotificationService` + `NotificationChannel` (SmartEnum) + `EmailNotificationService` (one table-based digest per subscriber per pass, HTML-encoded, text alternative); the polling loop dispatches the email-keyed poll result. Feature: [`../features/notifications.md`](../features/notifications.md).

**Definition of done (skeleton):** admin adds a feed → it appears publicly → a signed-in user
subscribes → polling fetches new articles → the user receives an email. Running product, end-to-end.

✅ **Met** — covered end to end through the real host by `WalkingSkeletonTests`, and exercised once against live publishers (RSS, Atom and RDF). Deliberate skeleton gaps handed to Phase 2: no retry of a failed digest and no conditional GETs (C4), no unsubscribe link/headers (C5), whole-row unsubscribe only (C1/C5), New Scientist answers 406 to .NET's HTTP stack (C4).

- [x] 🗂 **P1.R — Phase 1 review & remediation.** Senior architecture review of S1–S5 (verdict: the skeleton is sound) and the fixes it surfaced. ✅ **Done** — the double-encoded Identity confirmation links (self-registration could never be confirmed) and the register-path `ReturnUrl`; the idle-feed **catch-up cap** (`Polling:CatchUpAfterMinutes`) and an interval upper bound; bounded, linear markup stripping in the parser; a `NewArticle` DTO at the notification boundary; entity-owned `*MaxLength` constants; test hygiene (no hosted polling loop in host tests, derived seed counts, `FakeTimeProvider`); **bUnit** component tests for the interactive subscribe and admin-form paths; accessible button names; the doc re-baseline below. **Found in manual testing afterwards (R8):** no page was ever interactive — the home page persisted the whole catalogue (~87 KB) into the circuit-start message, which SignalR caps at 32 KB, so every circuit died at start; fixed by persisting only the subscribed ids and guarded by a page test, verified in a headless browser. 255 tests. Plan + findings: [`P1.R-phase-1-review-remediation.md`](./P1.R-phase-1-review-remediation.md).

---

## Phase 2 — Broaden (parallel tracks)

After the skeleton, three tracks run largely in parallel. Ordering is intra-track; cross-track
dependencies are called out.

### Track A — Admin
- [x] 🗂 **A1 — Category CRUD** (edit/delete + validation). _(needs S1)_ ✅ **Done** — `CategoryService` gains
  `UpdateAsync` / `DeleteAsync` (duplicate-name check ignores the edited row; delete refuses with `Category.HasFeeds`
  while feeds remain — `Feed → Category` is `Restrict`); `/admin/categories` edits and deletes in a reusable,
  CSS-only Bootstrap modal (`Components/Shared/Modal.razor`, no JS interop). Plan (with A2):
  [`A1-A2-category-and-feed-crud.md`](./A1-A2-category-and-feed-crud.md); feature:
  [`../features/admin-catalog-management.md`](../features/admin-catalog-management.md).
- [x] 🗂 **A2 — Feed CRUD** (edit/delete, image, category dropdown). _(needs S1)_ ✅ **Done** — `FeedService` gains
  `UpdateAsync` (duplicate-URL check ignores the edited row; not-found for an unknown feed or category),
  `GetDeletionImpactAsync` (subscription/article counts for the warning) and `DeleteAsync` (loads
  `.Include(Subscriptions)` because `Subscription → Feed` is `ClientCascade`; articles cascade at the DB).
  `/admin/feeds` edits every field (incl. the category dropdown) in the shared modal, and deletes after warning of
  the subscriptions/articles that go with it. Plan:
  [`A1-A2-category-and-feed-crud.md`](./A1-A2-category-and-feed-crud.md); feature:
  [`../features/admin-catalog-management.md`](../features/admin-catalog-management.md).
- [x] 🗂 **A3 — User management** (list users + their feeds; remove individual subscriptions; delete user registrations). _(needs subscriptions — S3 / C1)_ ✅ **Done** — `/admin/users` over `IUserAdminService` (`ListAsync` + `RemoveSubscriptionAsync` + `DeleteUserAsync`), mirroring the `SubscriptionService` shape (ErrorOr + `IDbContextFactory`, no `UserManager`). Deleting a user cascades their subscriptions at the DB (`Subscription → User` is `Cascade`); the acting admin can't delete their own account (disabled button **and** a `User.CannotDeleteSelf` service guard). Remove/delete confirm in the shared `Modal`. Plan: [`A3-user-management.md`](./A3-user-management.md); feature: [`../features/admin-user-management.md`](../features/admin-user-management.md). **Deferred:** the grant/revoke-`IsAdmin` toggle (see *Deferred / out of scope*). **Security-stamp guard (from G0.9), still pending for that toggle:** the admin claim is baked into the auth cookie by `AdminClaimsPrincipalFactory`, so whenever the `IsAdmin` toggle is built it must call `UserManager.UpdateSecurityStampAsync` so `IdentityRevalidatingAuthenticationStateProvider` regenerates the principal — otherwise the change won't take effect until the user's next sign-in.

### Track B — Public UX
- [ ] 🗂 **B1 — Feed list by category** (cards + image, 5/category, "more" +5, single-row desktop / wrap mobile). _(needs S2, A2)_
- [ ] 🗂 **B2 — Ordering control** (popularity default, name; asc/desc) in category header. Popularity = the fixed `Feed.Popularity` figure seeded in G0.7. _(needs B1)_
- [ ] 🗂 **B3 — Category filter** (single category, cap 30, "more" +30). _(needs B1)_
- [ ] 🗂 **B4 — Feed detail page** (article cards: image, first paragraphs, title, subject to availability; subscribe/unsubscribe button). _(needs G0.3; content from S4)_ **Notes from P1.R:** the spec gives each card two buttons, one leading here — today's *Visit site* (external) button becomes / joins the feed-page button. Ordering articles by `PublishedAt` in SQL relies on the **UTC invariant** (every persisted `DateTimeOffset` is UTC; SQLite compares them as text).

### Track C — Subscriptions & notifications
- [ ] 🗂 **C1 — Full subscribe flow** (dialog with email + Slack switches; unsubscribe-in-red state; per-channel unsubscribe). **Enforce the `SlackEnabled ⇒ SlackWebhookUrl` invariant** (and the `https://hooks.slack.com/services` prefix rule) here via FluentValidation — the `Subscription` entity documents but does not enforce it. _Already done by S3 + P1.R:_ unauthenticated → login/register preserving the return location (`ReturnUrl` through login, registration, the confirmation email and the confirm page's Continue button), the red Unsubscribe state, accessible button names. _(needs S3, auth)_
- [ ] 🗂 **C2 — Slack notifications** (`SlackNotificationService` via Slack.Webhooks; validate URL starts `https://hooks.slack.com/services`; LastOrDefault prefill; default channel). The poll result stays keyed by email; its value grows from `IReadOnlyList<NewArticle>` into a per-subscription digest (channels, webhook, subscription id) so the dispatcher can fan out per channel. _(needs abstraction; parallel with email)_
- [ ] 🗂 **C3 — Email notifications hardening** (article templates; Outlook table-based layout + Outlook-only duplicated buttons). **Prerequisite:** a configured public base URL (`SiteOptions.PublicBaseUrl`, validated at start) — background-generated mail has no request context to build site links from. _(needs S5, G0.6)_
- [ ] 🗂 **C4 — Polling hardening** (ETag/Last-Modified + 304; **resolve images** — use the RSS-provided image, else scrape `og:image` from the article target; dispatch to email/Slack per user choice; retry a digest that failed to send). _Already done by S4/S5/P1.R:_ active feeds only, the email→articles dictionary, per-feed isolation, the first-poll/catch-up cap, bounded parsing. **Prerequisites / first commits:** (1) an **outbound URL filter** before any server-side fetch of a publisher-controlled URL — `og:image` scraping is an SSRF surface: reject loopback/private/link-local targets and re-check on every redirect hop; (2) extract a `FeedPoller` (fetch → parse → de-dup → insert → stamp, returning the previous `LastPolledAt` as data) from `FeedPollingService`, keeping the orchestrator for selection/isolation/cap/recipients and channel fan-out in the loop's `DispatchAsync`; `og:image` enrichment runs as a separate bounded step after the feed's save; (3) bounded concurrency across feeds (`Parallel.ForEachAsync`, a `MaxConcurrentFeeds` option, a thread-safe test fetcher) — sequential polling of 115 feeds already exceeds the minute; (4) drop `Cache=Shared` from the SQLite connection string and enable WAL journal mode for a background writer plus interactive readers. Consider notifying only about articles published after `Subscription.CreatedAt` once the value is per-subscription. _(needs S4, C2, C3)_
- [ ] 🗂 **C5 — Unsubscribe** (RFC 8058 HMAC-signed one-click; unsubscribe page, single button; per-channel or both; Unsubscribe link + List-Unsubscribe headers). Needs `SiteOptions.PublicBaseUrl` (C3) and the subscription id in the digest value (C2). _(needs C3)_

**Cross-track dependencies:** `B1` needs `A2` · `A3` needs subscriptions (`C1`) · `C5` needs `C3` · `C4` needs `C2`+`C3`.

---

## Decided

- **Popularity** — a **fixed** figure per feed (not computed), stored on `Feed`, set from the G0.R research. Default ordering sorts by it.
- **Categories** — derived from the G0.R feed research, **not** the spec's illustrative examples.
- **Images** — use the **RSS-provided image** if present; otherwise the **`og:image`** scraped from the article target. **No admin upload.** Store the image URL (not a blob).
- **Article identity/de-dup** — resolved at the schema level in G0.3: `Article.ExternalId` (RSS `<guid>` else link) with a unique `(FeedId, ExternalId)` index. Polling inserts only unseen ids against it (S4).
- **UTC everywhere** — every persisted `DateTimeOffset` is UTC (the parser normalises dates, the poller stamps `TimeProvider.GetUtcNow()`); SQLite stores and compares them as text, so mixed offsets would order wrongly. _(P1.R)_
- **Column limits on the entities** — `public const int *MaxLength` members are the single source for EF configuration, validators and forms; `LimitsTests` guards constant ↔ column. _(P1.R)_
- **Poll result shape** — keyed by subscriber email (spec); the value is a list of `NewArticle` values, never entities, and grows into a per-subscription digest in C2/C4. _(P1.R)_

## Open decisions (resolve during detailed planning)

- **"More"/pagination mechanics** — investigate the options; **constraint: avoid a full page reload** (lean on InteractiveServer partial loading / enhanced navigation). Resolved in the B1 detailed plan.
- **HMAC key management/rotation** for unsubscribe tokens.
- **Confirmation UX** now that a real email sender exists.

## Deferred / out of scope

- Alternate or multiple email addresses (verification-bound). _(spec)_
- Multiple Slack channels (single webhook for v1). _(spec)_
- Categories as free-text (superseded by the category entity). _(spec)_
- **Admin image upload** — not supported at the moment. _(this session)_
- **Admin grant/revoke of `IsAdmin` from the user-management UI** — deferred from **A3** (this session's scoping decision); A3 ships list / remove-subscription / delete-registration only. When built, it must call `UserManager.UpdateSecurityStampAsync` after flipping the flag (the G0.9 security-stamp guard on the A3 item), and it would also be the place to fix the seeder's promote-path (`AdminUserSeeder`), which omits the same stamp bump but is harmless at startup.
- **Article retention / cleanup** — the `Articles` table grows without bound (every item of every active feed, forever). A retention job (or a per-feed cap) is needed before the product runs for long; not scheduled in Phase 2. _(P1.R)_

## Next steps

1. Save this roadmap to `doc/plans/00-master-plan.md` and commit it.
2. **Kick off G0.R (RSS feed research) first** — handed to a separate agent; it gates the rest of Phase 0.
3. Once the research lands, continue Phase 0 (`G0.1` onward) and produce per-feature detailed plans (🗂 items) in `doc/plans/` as each is reached.
