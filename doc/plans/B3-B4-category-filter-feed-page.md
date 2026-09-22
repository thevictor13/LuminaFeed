# B3 + B4 — Public category filter & feed detail page (plan)

## Permission constraint (VERBATIM — session rule, reproduced exactly)

> You must NEVER, EVER change anything beyond the contents of your working directory. Exceptions: the Claude memory, scratchpad and plans folders; %TEMP%; the current repo's .git for the current branch only (commit, no push, no stash pop); the NuGet global packages folder and NuGet caches (restore/add packages only); the ASP.NET Data Protection key ring and .NET SDK telemetry folders under the user profile; outbound HTTPS to nuget.org and to RSS publishers; and SMTP to localhost:25.
> You may however read files that were gitignored in your working directory, which you will find in d:\Work\Sonrisa\LuminaFeed\. This is a rule you must not breach for the entirety of this session

**Operational note:** work happens in the **`phase-2b`** worktree; the "current branch" above is `phase-2b`. **B3 and B4 are two separate local commits — no push, no stash pop.**

## Context

Phase-2 Track B broadens the public UX over data that already exists. B1 (cap 5 + "more") and B2 (per-category order control) are done. The two remaining Track-B items:

- **B3 — Category filter.** Spec (`doc/LuminaFeed Initial Specification.md` §"Public UI — Main View"): *"at the root (top) of the page it must be possible to filter by category… only that category's items are shown, capped at 30 rather than 5… a more button… adding another 30 to the cap."*
- **B4 — Feed detail page.** Spec §"Feed Page": a per-feed page listing the latest articles as a *different kind of card* (image, an initial few paragraphs, title — all subject to availability), with a subscribe button at the top unless already subscribed. P1.R note: each public card gets a button leading here, and ordering articles by `PublishedAt` in SQL relies on the **UTC invariant**.

Both build directly on the B1/B2 components. The intended outcome: a visitor can narrow the home page to one category (deep, cap-30 view) and click into any feed to read its latest articles and subscribe.

## Decisions (confirmed with the user)

- **B4 card buttons — keep both + add.** The public `FeedCard` keeps its external **Visit site** link, **adds** an internal **View articles** link (→ `/feed/{id}`), and pages still add **Subscribe** — three buttons.
- **B3 filter UI — dropdown.** A Blazor-driven `Category: All ▾` dropdown at the top of the page, mirroring the existing `OrderMenu` interaction (no Bootstrap JS: a `bool` + a click-away backdrop + Escape).
- **B3 filter state — ephemeral in-circuit.** Held in a plain field on `Home` like B1's visible-count and B2's order; resets on refresh; **no URL change and no new `[PersistentState]`** (protects the 32 KB circuit-start budget).

## Existing pieces to reuse (do not re-invent)

- `Components/Pages/Home.razor` — owns the auth principal, `ISubscriptionService`, the shared subscribed-ids/busy state and `ToggleSubscriptionAsync`; loads the whole catalogue once (deliberately unpersisted). B3 adds a field + the filter control here; B4's page copies this small subscribe pattern for one feed.
- `Components/Shared/CategorySection.razor` — already slices/re-sorts `Category.Feeds` in memory with a `visibleCount` and a "More" button. B3 turns its hard-coded page size into a parameter.
- `Components/Shared/OrderMenu.razor` (+ its scoped-CSS backdrop/Escape pattern) — the template for the new `CategoryFilter` dropdown.
- `Components/Shared/FeedCard.razor` / `SubscribeButton.razor` — the card and the subscribe/login-redirect button (already accessible, red-when-subscribed).
- `IFeedService` / `FeedService` (`Services/Feeds/`) — the read-model style (`FeedSummary`, `CategoryFeeds`); B4 adds one method + one record here.
- `ISubscriptionService` — `GetSubscribedFeedIdsAsync` / `SubscribeByEmailAsync` / `UnsubscribeAsync`, reused verbatim on the feed page.
- Article facts (confirmed): `Article.Summary` is **plain text** (markup stripped, entities decoded, capped 1000 chars by `FeedParser`), `Link`/`ImageUrl` are validated absolute http(s). So Blazor's `@expr` auto-encoding is sufficient — **never** `MarkupString`. Mirror `FeedCard`'s image (`referrerpolicy="no-referrer" loading="lazy"`) and external-link (`target="_blank" rel="noopener noreferrer"`) treatment.

---

## Commit 1 — B3: category filter

### New — `Components/Shared/CategoryFilter.razor` (+ scoped CSS)
A Blazor-driven dropdown mirroring `OrderMenu`: a trigger `Category: {selected name | All} ▾` (`aria-haspopup="menu"`, `aria-expanded`, `aria-label="Filter feeds by category"`) toggling a `.dropdown-menu` with **All feeds** + one `role="menuitem"` per category, the current one `active`/`aria-current`. Transparent full-viewport backdrop closes on outside click; Escape closes. Params: `Options` (`IReadOnlyList<(Guid Id, string Name)>`), `Selected` (`Guid?`, null = All), `OnChange` (`EventCallback<Guid?>`).

### Modify — `Components/Shared/CategorySection.razor`
Replace the `const int PageSize = 5` with a `[Parameter] int PageSize { get; set; } = 5;`; drive `visibleCount` and the "More" increment from it. Reset `visibleCount` to `PageSize` only when `PageSize` actually changes (track an `_appliedPageSize` field, checked in `OnParametersSet`) so entering/leaving the filtered view restarts at the new cap without disturbing subscribe re-renders. Order selection is preserved across the change.

### Modify — `Components/Pages/Home.razor`
- Add ephemeral `Guid? selectedCategoryId` (not persisted) and constants `DefaultPageSize = 5`, `FilteredPageSize = 30`.
- Render `<CategoryFilter>` between the lead paragraph and the sections, its `Options` projected from the already-loaded `Categories`.
- When `selectedCategoryId is null`: render every section at `PageSize=DefaultPageSize` (unchanged view). When set: render only the matching `CategorySection` at `PageSize=FilteredPageSize`.
- Keep `@key="category.CategoryId"`; everything else (load, toggle, `SubscribedIds`, `ErrorList`) unchanged.

### Docs (commit 1)
- Save this plan to `doc/plans/B3-B4-category-filter-feed-page.md`.
- Update `doc/features/public-feed-list.md` (B3 filter dropdown, cap-30/"more"-30, `PageSize` parameter).
- Tick **B3** in `doc/plans/00-master-plan.md` with a done-note.

### Tests (commit 1)
- **`CategoryFilterComponentTests`** (pure bUnit, no DB): trigger reads "All feeds" by default; opening lists All + every category; selecting one raises `OnChange` with its id and marks it active; backdrop click and Escape close it; accessible label present.
- **`CategorySectionComponentTests`** (extend): with `PageSize=30` it shows up to 30 and "More" adds 30; changing `PageSize` resets `visibleCount`.
- **`HomePageComponentTests`** (extend, interactive over real services): selecting a category renders only that section and up to 30 cards; clearing returns to all-sections/cap-5.
- **`PublicPagesTests`** (extend): served `/` contains the filter control exactly once; the persisted circuit-start payload stays under the existing 8 KB budget (`PersistedStateBytes` helper).

---

## Commit 2 — B4: feed detail page

### Service — `Services/Feeds/IFeedService.cs` + `FeedService.cs`
- New record `ArticleSummary(Guid Id, string Title, string Link, string? Summary, string? ImageUrl, DateTimeOffset? PublishedAt)` and `FeedDetail(FeedSummary Feed, IReadOnlyList<ArticleSummary> Articles)`.
- New `Task<FeedDetail?> GetFeedDetailAsync(Guid feedId, int maxArticles, CancellationToken)`: project the feed to `FeedSummary` (return `null` when absent), then read its articles (filtered by `FeedId`) and order them **in memory** — newest first (`PublishedAt` desc), dateless last, `FetchedAt` breaking ties — and cap at `maxArticles`.

> **Correction discovered during implementation:** EF Core's **SQLite** provider throws `NotSupportedException` for a `DateTimeOffset` in an `ORDER BY` clause, so the planned SQL-side ordering (and the `(FeedId, PublishedAt)` index below) is not possible through EF. Ordering is therefore done client-side (LINQ-to-Objects orders a null key last under `OrderByDescending`, giving dateless-last), matching the notification path which already sorts in memory. **No article index or migration is added** — the existing `(FeedId, ExternalId)` unique index already serves the `FeedId` filter on its prefix. Bringing a feed's rows into memory is acceptable at current volumes (unbounded article growth is a separately deferred retention concern). A memory note records the SQLite/DateTimeOffset gotcha.

### New — `Components/Pages/Feed.razor` (`@page "/feed/{FeedId:guid}"`, `@rendermode InteractiveServer`, anonymous)
- Loads `GetFeedDetailAsync(FeedId, ArticleLimit=50)` on the interactive render; `null` → a "Feed not found" message + link back to `/` (route constraint `:guid` already rejects non-guids as 404).
- Header: back-to-Feeds link, feed name, category name, description; a top action bar with **Visit site ↗** (external) and a **`SubscribeButton`** (login-redirect for anonymous with `ReturnUrl` = this page; red Unsubscribe when subscribed). Reuse the `Home` subscribe pattern for this one feed (userId from the principal, busy flag, `SubscribeByEmailAsync`/`UnsubscribeAsync`). Persist only a tiny `[PersistentState] bool? Subscribed` so the button doesn't flip; re-query articles on the interactive render (never persisted).
- Body: `ArticleCard` per article, or "No articles have been fetched for this feed yet." when empty. No per-page paging (spec caps only the main view); capped at the latest 50.

### New — `Components/Shared/ArticleCard.razor`
The "different kind of card": image when present (letterboxed, `loading="lazy"`, `referrerpolicy="no-referrer"`), title as an external link (`target="_blank" rel="noopener noreferrer"`), the summary paragraph and the published date — each guarded for null. Plain `@expr` interpolation (Blazor auto-encodes; summary is already plain text). Param: `Article` (`ArticleSummary`).

### Modify — `Components/Shared/FeedCard.razor`
Add an internal **View articles** anchor (`href="feed/{Feed.Id}"`, `aria-label="View {Feed.Name} articles"`) next to **Visit site**, before the `Actions` fragment. `CategorySection` is unchanged (the card owns the link; `FeedSummary` already carries `Id`).

### Docs (commit 2)
- New `doc/features/feed-page.md` (route, header, article cards, ordering + UTC dependence, the index, subscribe reuse).
- Update `doc/features/public-feed-list.md` (FeedCard's new "View articles" button).
- Update this plan's `doc/plans/` copy if anything shifted; tick **B4** in `doc/plans/00-master-plan.md` with a done-note.

### Tests (commit 2)
- **`FeedServiceTests`** (extend): `GetFeedDetailAsync` returns the feed + its articles newest-first (incl. dateless-last, cap honoured), excludes other feeds' articles, and returns `null` for an unknown id. Seed articles directly via `ctx.Articles.Add(new Article { FeedId, ExternalId, Title, Link, … })` (the `DomainModelTests` idiom; `SqliteTestDatabase` has no article helper).
- **`ArticleCardComponentTests`** (pure bUnit): renders title/link/summary/image/date when present; hides image/summary/date when null; the title link carries `rel="noopener noreferrer"`.
- **`FeedPageComponentTests`** (bUnit interactive, like `HomePageComponentTests`: `SqliteTestDatabase`, real `IFeedService`/`ISubscriptionService`, `SetRendererInfo`, `AddAuthorization`): renders header + Visit site + article cards; anonymous → Subscribe is a login link; signed-in Subscribe toggles to red Unsubscribe and persists a row; an unknown feed id shows the not-found message.
- **`PublicPagesTests`** (extend): `/feed/{id}` for a seeded feed (+ an article row inserted via a service scope) returns 200 with the feed name and article title; an unknown-but-valid guid returns 200 with the not-found message; `/feed/not-a-guid` returns 404; `/` now shows one "View articles" link per visible card; the feed page's persisted payload stays under 8 KB.

---

## Verification (both commits)

1. `DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build` — **0 warnings**.
2. `DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet test` — **green** (incl. the `HasPendingModelChanges` drift guard after the migration).
3. **Browser check (interactivity is only real in a browser — per convention & memory):** launch an isolated instance (private port + temp DB + distinct admin email, so no leaked digests / no touching the main app.db) driven by the scratchpad PuppeteerSharp probe against the installed Chrome (no Chromium download; network stays within nuget.org / RSS publishers). Seed a couple of `Article` rows directly into the temp DB for a feed so the feed page shows cards. Confirm:
   - **B3:** the filter dropdown opens/closes (backdrop + Escape); picking a category shows only that section at cap 30 with a "More" (+30) button; "All feeds" restores the cap-5 view — all without a full page reload.
   - **B4:** a card's **View articles** navigates to `/feed/{id}`; the page shows the header, **Visit site**, and article cards; **Subscribe** toggles to red **Unsubscribe** and back; an unknown guid shows the not-found message.
   - Console is clean (no circuit errors) on every page.

## Commit plan

- **Commit 1 (B3):** `CategoryFilter.razor` (+CSS), `CategorySection.razor` (PageSize param), `Home.razor`, the B3 tests, the plan doc, `public-feed-list.md` + master-plan tick. Build 0-warnings, tests green, browser-verified before committing.
- **Commit 2 (B4):** the `IFeedService`/`FeedService` additions, `ArticleConfiguration` index + migration, `Feed.razor`, `ArticleCard.razor`, `FeedCard.razor` change, the B4 tests, `feed-page.md`, `public-feed-list.md` + master-plan tick. Build 0-warnings, tests green, browser-verified before committing.

Both commits are **local only on `phase-2b` — no push, no stash pop.** Commit-message trailer: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
