# Feature: Public Feed List (S2 · B1)

The public main view (`/`): every admin-curated feed shown as a **card**, grouped under its category. S2 built the
grouped list; **B1** added the per-category cap and the "more" button (below). Still to come: the ordering control
(**B2**), the category filter (**B3**) and the per-feed article page (**B4**).

## Page (`LuminaFeed/Components/Pages/Home.razor`)

- Anonymous-accessible. Renders one **`CategorySection`** per category (see below).
- States: _loading_, _"No feeds have been added yet."_ for an empty catalogue, otherwise the grouped sections.
- Since S3 the page runs with `@rendermode InteractiveServer` (for the per-card subscribe button and the B1 "more"
  button). It persists only the user's subscribed ids across the prerender → circuit hand-off and re-queries the
  catalogue on the interactive render: persisted state is sent back in the circuit-start message, which SignalR caps
  at 32 KB, and persisting the whole catalogue (~87 KB) made every circuit fail at start — nothing on the page was
  interactive. A page test keeps the persisted payload under 8 KB. See [Subscriptions](./subscriptions.md).
- The page owns the auth principal, the subscription services and the shared subscribed-ids/busy state, and passes the
  subscribe wiring down to each section as parameters + an `EventCallback<Guid>`.
- Replaces the template's "Hello, world" page. The template sample pages (`Counter`, `Weather`, `Auth`), their nav
  links, the layout's "About" link and the dead `System.Net.Http` imports were removed; the nav now has a single
  **Feeds** entry (plus Admin / account links).

## Category section (B1) (`LuminaFeed/Components/Shared/CategorySection.razor`)

One category's header and its feed cards, owning the per-category interactive state.

- **Cap + "more" (B1).** Shows at most **5** cards up front (`Take(visibleCount)`, `visibleCount` starts at 5); a
  **More** button — shown only while more remain — reveals **5** more per click. The whole catalogue is already in the
  circuit's memory (the page loads it once, deliberately unpersisted), so the cap and the "more" step are pure
  client-side slicing — no re-query, and no new `[PersistentState]` (which would grow the 32 KB circuit-start payload).
  The More button carries an `aria-label` naming its category ("Show more World News feeds").
- **Layout.** A responsive Bootstrap grid `row-cols-1 row-cols-sm-2 row-cols-md-3 row-cols-lg-5` — a single row of up
  to five on desktop (`lg`+), wrapping to 3 / 2 / 1 on smaller screens.

## Card (`LuminaFeed/Components/Shared/FeedCard.razor`)

Shows the feed's **image when it has one** (`ImageUrl`, lazy-loaded, no referrer, letterboxed via scoped CSS), its
name, its description when present, and a **Visit site** button (`SiteUrl`, new tab, `rel="noopener noreferrer"`).
An `Actions` render fragment lets pages add buttons next to it (used by the subscribe button).

## Service (`IFeedService.ListByCategoryAsync`)

Returns `CategoryFeeds(CategoryId, CategoryName, Feeds)` — **categories by name**, each with its feeds by
**`Popularity` descending, then name** (the fixed default order). Categories without feeds are omitted. One SQL
query ordered by category name (a join, so the `(CategoryId, Popularity)` index doesn't serve it). B1's cap and "more"
slice this already-loaded list in the component — no per-category query — so the `(CategoryId, Popularity)` index is
still reserved for a future per-category query (the B3 filter's cap-30 view).

A feed created through the admin page ([Admin Catalogue Management](./admin-catalog-management.md)) appears here on
the next load — the first link of the skeleton's definition of done.

## Tests

- `FeedServiceTests` — grouping and ordering (popularity, name tie-break), empty categories omitted, empty catalogue,
  a feed added through `CreateAsync` shows up in the grouped list.
- `CategorySectionComponentTests` (bUnit) — the B1 cap/"more": at most five cards with a More button when more remain;
  clicking More reveals the rest and hides the button; no More button at five-or-fewer; each visible card carries an
  accessible subscribe control.
- `PublicPagesTests` — the real host serves `/` anonymously with the **capped** card count (≤ 5 per category, derived
  from the embedded catalogue) and one "more" button per category that has more than five feeds; a feed added via
  `IFeedService` (top popularity, so within the visible five) is rendered with its image and site link; the persisted
  circuit-start payload stays under 8 KB; the removed sample routes return 404.
- Interactivity (circuit boot + the More click growing a category) is verified in a real browser against an isolated
  app instance, per the "prove it in a browser" convention — bUnit does not run `blazor.web.js`.
