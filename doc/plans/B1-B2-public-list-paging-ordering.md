# B1 + B2 — Public feed list: per-category paging & ordering (plan)

## Permission constraint (VERBATIM — session rule, reproduced exactly)

> You must NEVER, EVER change anything beyond the contents of your working directory. Exceptions: the Claude memory, scratchpad and plans folders; %TEMP%; the main repo's .git for the phase-1 branch only (commit, no push, no stash pop); the NuGet global packages folder and NuGet caches (restore/add packages only); the ASP.NET Data Protection key ring and .NET SDK telemetry folders under the user profile; outbound HTTPS to nuget.org and to RSS publishers; and SMTP to localhost:25.
> You may however read files that were gitignored in your working directory, which you will find in d:\Work\Sonrisa\LuminaFeed\. This is a rule you must not breach for the entirety of this session

**Operational note (confirmed with the user for this task):** B1 and B2 are committed on the **`phase-2b`** branch/worktree — **local commit only, no push, no stash pop**. The "phase-1 branch only" wording above is stale from a prior session; the user authorised committing this work on `phase-2b`. No other deviation from the constraint is permitted.

## Context

The public home page (`/`, [`Home.razor`](../../LuminaFeed/Components/Pages/Home.razor)) listed **every** admin-curated feed as a card grouped by category (the S2/S3 walking-skeleton slice). The spec (`doc/LuminaFeed Initial Specification.md` §"Public UI — Main View") and the [master plan](./00-master-plan.md) call for two Phase-2 Track-B enhancements:

- **B1 — Feed list by category:** at most **5** feeds per category (single row on desktop, wrapping on mobile), with a **"more"** button that reveals 5 more each click.
- **B2 — Order control:** a per-category-header control to order by **popularity (default)** or **name**, **ascending/descending**.

Both are pure public-UX changes over data that already exists. `Feed.Popularity` and names are already seeded (G0.7) and already reach the page via `FeedSummary`, so **no service query, entity, migration or seed change is needed** — the cap, the "more" step and the ordering are all done **client-side in the InteractiveServer circuit** over the catalogue the page already loads once (deliberately unpersisted; the R8 dead-circuit fix keeps only the small subscribed-id set in `[PersistentState]`, since the circuit-start message is capped at 32 KB).

**Dependency note:** the master plan lists B1 as "needs A2" (admin Feed CRUD, not yet built). A2 does not gate B1/B2 — the public list renders already-seeded feeds and image URLs. Doing B1/B2 ahead of A2 is intentional.

## Design

Extract a **`CategorySection`** component owning one category's presentation and its per-category interactive state (visible count in B1; selected order in B2). This keeps `Home.razor` thin (CLAUDE.md) and makes the paging/ordering unit-testable in isolation with bUnit. `Home.razor` keeps the auth principal, the subscription services and the shared subscribed-ids/busy state, passing the subscribe wiring down as parameters + an `EventCallback<Guid>`.

`IFeedService.ListByCategoryAsync()` is unchanged: it returns all feeds (already popularity-desc, name) grouped by category; `CategorySection` slices/re-sorts its `Category.Feeds` in memory.

**Order control (B2):** the app loads only Bootstrap **CSS** (no Bootstrap JS — `App.razor` has only `blazor.web.js`), so a `data-bs-toggle="dropdown"` menu will not toggle. Per the user's choice, it is a **Blazor-driven dropdown button** whose open/close is handled in C# (a `bool` + a click-away backdrop + Escape), styled with Bootstrap's `.dropdown` / `.dropdown-menu` CSS.

## B1 — cap 5 + "more" + layout (commit 1)

- **New** [`Components/Shared/CategorySection.razor`](../../LuminaFeed/Components/Shared/CategorySection.razor): header + a responsive grid `row-cols-1 row-cols-sm-2 row-cols-md-3 row-cols-lg-5` over `Category.Feeds.Take(visibleCount)` (`visibleCount` starts at 5, plain field — **not** persisted); a **More** button (shown only while `visibleCount < Category.Feeds.Count`, `aria-label="Show more {Category} feeds"`, `@onclick` → `+= 5`). Parameters: `Category`, `IsAuthenticated`, `LoginUrl`, `SubscribedFeedIds`, `BusyFeedId`, `OnToggleSubscription`.
- **Modify** `Home.razor`: replace the inline per-category `<section>` loop with `<CategorySection … @key="category.CategoryId"/>`. Everything else (load, toggle, persisted `SubscribedIds`, `ErrorList`) unchanged.
- **Docs:** this plan; [`public-feed-list.md`](../features/public-feed-list.md) (cap, more, `lg` layout, `CategorySection`); tick B1 in the master plan.

## B2 — order control (commit 2)

- **New** `Components/Shared/FeedOrdering.cs`: `enum FeedOrder { PopularityDesc, PopularityAsc, NameAsc, NameDesc }` (default `PopularityDesc`) + a pure `Sort(IReadOnlyList<FeedSummary>, FeedOrder)` (name comparisons `OrdinalIgnoreCase`; popularity ties broken by name, matching the service default).
- **New** `Components/Shared/OrderMenu.razor` (+ scoped CSS): a Blazor-driven dropdown — an `Order ▾` trigger (`aria-haspopup`, `aria-expanded`, `aria-label="Order {Label} by"`) toggling a `.dropdown-menu` with four `.dropdown-item` buttons (**Most popular**, **Least popular**, **Name (A–Z)**, **Name (Z–A)**), the current one marked `active`/`aria-current`; a transparent full-viewport backdrop closes it on outside click (`z-index` layering in scoped CSS), Escape closes it. Params: `Value`, `OnChange`, `Label`.
- **Modify** `CategorySection.razor`: hold `FeedOrder Order` (default `PopularityDesc`, not persisted); place `<OrderMenu>` in the header; iterate `FeedOrdering.Sort(Category.Feeds, Order).Take(visibleCount)`.
- **Docs:** update [`public-feed-list.md`](../features/public-feed-list.md); tick B2 in the master plan.

## Tests (xUnit / bUnit — plain `Assert.*`)

- **B1:** `CategorySectionComponentTests` (bUnit, no DB — the component takes a plain `CategoryFeeds`): five cards + More when more remain; clicking More reveals the rest and hides the button; no More at ≤ 5; each visible card has an accessible subscribe control. `PublicPagesTests` (host) updated: the capped card count and one More button per category with > 5 feeds are derived from `SeedCatalogLoader.LoadEmbedded()`; the admin-added feed uses top popularity so it stays within the visible five; the ≤ 8 KB persisted-payload guard stays green.
- **B2:** `FeedOrderingTests` (pure unit) for the four modes incl. tie-breaks; `CategorySectionComponentTests` extended — default DOM order is popularity-desc, opening the menu shows `.dropdown-menu.show`, selecting "Name (A–Z)" reorders the titles and marks the active item, the backdrop closes the menu.

## Verification

1. `DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build` — 0 warnings.
2. `DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet test` — green.
3. **Browser check (interactivity is only real in a browser):** an isolated instance (private port + temp DB + distinct admin email) driven by a scratchpad PuppeteerSharp probe against the **installed** Chrome (no Chromium download — network stays within nuget.org / RSS publishers): confirm the cap (five per category), that "More" reveals more without a full reload, that the order menu opens / reorders live / closes, and a clean console (no circuit errors).

## Decisions

- **Cap / order live in the component, over the already-loaded list** — not in the service. The page already loads the whole catalogue once by design; per-category re-queries would add round-trips for data already held. `FeedSummary` carries `Popularity` and `Name`, so all four orderings are in-memory.
- **No new `[PersistentState]`** — visible count and order are ephemeral; persisting them would grow the 32 KB circuit-start payload the R8 fix protects.
- **`lg` breakpoint for five-across** (was `xl`) so "single row on desktop" holds at common desktop widths.
