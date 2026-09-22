# Feature: Public Feed List (S2 · B1 · B2 · B3)

The public main view (`/`): every admin-curated feed shown as a **card**, grouped under its category. S2 built the
grouped list; **B1** added the per-category cap and the "more" button, **B2** the per-category order control, and
**B3** the top-level category filter (all below). The per-feed article page is [**B4**](./feed-page.md).

## Page (`LuminaFeed/Components/Pages/Home.razor`)

- Anonymous-accessible. Renders a top-level **`CategoryFilter`** (B3) then one **`CategorySection`** per visible
  category (see below).
- States: _loading_, _"No feeds have been added yet."_ for an empty catalogue, otherwise the grouped sections.
- **Category filter (B3).** The page holds an ephemeral `selectedCategoryId` (null = "All feeds"; not persisted,
  like B1's visible-count and B2's order). With no selection every category renders at the default cap of **5**; when
  a category is selected only that section renders, at the deeper cap of **30**. The two caps are passed down as the
  `CategorySection.PageSize` parameter, so switching the filter re-caps the section without any re-query — the whole
  catalogue is already in the circuit's memory. Filter options are projected from that loaded catalogue.
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

- **Cap + "more" (B1).** Shows at most `PageSize` cards up front (`Take(visibleCount)`, `visibleCount` starts at
  `PageSize`); a **More** button — shown only while more remain — reveals `PageSize` more per click. The whole
  catalogue is already in the circuit's memory (the page loads it once, deliberately unpersisted), so the cap and the
  "more" step are pure client-side slicing — no re-query, and no new `[PersistentState]` (which would grow the 32 KB
  circuit-start payload). The More button carries an `aria-label` naming its category ("Show more World News feeds").
- **Page size (B1/B3).** `PageSize` is a parameter, **5** by default (the main view) and **30** when the B3 filter
  narrows the page to this one category. When `PageSize` changes, the visible window restarts at the new size
  (tracked in `OnParametersSet`), so entering/leaving the filtered view re-caps cleanly; a subscribe re-render, which
  leaves `PageSize` unchanged, does not disturb it. The chosen order is preserved across the change.
- **Layout.** A responsive Bootstrap grid `row-cols-1 row-cols-sm-2 row-cols-md-3 row-cols-lg-5` — a single row of up
  to five on desktop (`lg`+), wrapping to 3 / 2 / 1 on smaller screens.
- **Order control (B2).** The header carries an **`OrderMenu`** (see below) whose choice re-sorts this category's
  feeds. The section holds the selected `FeedOrder` (default popularity-descending, matching the service) and iterates
  `FeedOrdering.Sort(Category.Feeds, order).Take(visibleCount)` — a pure in-memory re-sort, like the cap. Re-ordering
  keeps the current "more" count (the same number of cards, reordered). The order is ephemeral (not persisted).

## Category filter (B3) (`LuminaFeed/Components/Shared/CategoryFilter.razor`)

A top-of-page dropdown that narrows the list to one category: **All feeds** (default) plus one item per category.
Selecting a category shows only its section, at the deeper cap of 30 (see the page above); "All feeds" restores the
full grouped view. The choice is ephemeral (not persisted) — it resets on refresh, consistent with B1/B2.

- **No Bootstrap JS**, exactly like `OrderMenu`: it drives open/close in the circuit (a `bool`, a transparent
  full-viewport backdrop that closes it on an outside click, and Escape), using Bootstrap's `.dropdown` classes for
  styling only.
- **Accessible.** The trigger reads `Category: {selection}` with `aria-label="Filter feeds by category"`,
  `aria-haspopup`, `aria-expanded`; each option is a `role="menuitem"` button, the current one `active` + `aria-current`.
- Owns no data: the parent (`Home`) supplies the `Options` (id + name, from the already-loaded catalogue) and the
  `Selected` id, and receives the choice through an `EventCallback<Guid?>` (null = "All feeds").

## Order menu (B2) (`LuminaFeed/Components/Shared/OrderMenu.razor`)

A per-category dropdown: **Most popular** (default) · **Least popular** · **Name (A–Z)** · **Name (Z–A)**, backed by
the `FeedOrder` enum and the pure `FeedOrdering.Sort` / `FeedOrdering.Label` helpers (`FeedOrdering.cs`).

- **No Bootstrap JS.** `App.razor` loads Bootstrap's CSS but not its JS bundle, so a `data-bs-toggle="dropdown"` menu
  would never open. `OrderMenu` drives open/close itself in the circuit: a `bool`, a transparent full-viewport
  **backdrop** that closes it on an outside click (z-index layering in scoped CSS), and **Escape** to close — using
  Bootstrap's `.dropdown` / `.dropdown-menu` classes purely for styling.
- **Accessible.** The trigger names its category (`aria-label="Order {category} by"`, `aria-haspopup`,
  `aria-expanded`); each option is a `role="menuitem"` button, the current one marked `active` + `aria-current`.
- Emits the chosen `FeedOrder` through an `EventCallback<FeedOrder>`; the parent (`CategorySection`) owns the state.

## Card (`LuminaFeed/Components/Shared/FeedCard.razor`)

Shows the feed's **image when it has one** (`ImageUrl`, lazy-loaded, no referrer, letterboxed via scoped CSS), its
name, its description when present, a **View articles** button (internal, `feed/{id}`, `aria-label="View {name}
articles"`, → the [feed page](./feed-page.md)) and a **Visit site** button (`SiteUrl`, new tab,
`rel="noopener noreferrer"`). An `Actions` render fragment lets pages add buttons next to them (used by the subscribe
button), so a card carries three buttons: View articles, Visit site and Subscribe.

## Service (`IFeedService.ListByCategoryAsync`)

Returns `CategoryFeeds(CategoryId, CategoryName, Feeds)` — **categories by name**, each with its feeds by
**`Popularity` descending, then name** (the fixed default order). Categories without feeds are omitted. One SQL
query ordered by category name (a join, so the `(CategoryId, Popularity)` index doesn't serve it). B1's cap, B2's
order and B3's filter all slice/re-sort this already-loaded list in the component — no per-category query — so the
`(CategoryId, Popularity)` index is still reserved for a future per-category query.

A feed created through the admin page ([Admin Catalogue Management](./admin-catalog-management.md)) appears here on
the next load — the first link of the skeleton's definition of done.

## Tests

- `FeedServiceTests` — grouping and ordering (popularity, name tie-break), empty categories omitted, empty catalogue,
  a feed added through `CreateAsync` shows up in the grouped list.
- `FeedOrderingTests` — the four B2 orderings, including the popularity tie-break by name and case-insensitive name
  order.
- `CategorySectionComponentTests` (bUnit) — the B1 cap/"more": at most five cards with a More button when more remain;
  clicking More reveals the rest and hides the button; no More button at five-or-fewer; each visible card carries an
  accessible subscribe control. B2: the default order is most-popular-first; opening the order menu shows the four
  options; selecting "Name (A–Z)" reorders the cards, closes the menu and marks the active option; the backdrop closes
  the menu. B3: a `PageSize` of 30 shows up to 30 with "More" adding 30, and changing `PageSize` restarts the window.
- `CategoryFilterComponentTests` (bUnit) — the B3 filter: default reads "All feeds" and starts closed; opening lists
  "All feeds" plus every category; selecting one raises `OnChange` with its id and closes the menu; a preselected
  category is shown on the trigger and marked active; the backdrop closes the menu.
- `PublicPagesTests` — the real host serves `/` anonymously with the **capped** card count (≤ 5 per category, derived
  from the embedded catalogue) and one "more" button per category that has more than five feeds; the B3 filter control
  renders exactly once; a feed added via `IFeedService` (top popularity, so within the visible five) is rendered with
  its image and site link; the persisted circuit-start payload stays under 8 KB; the removed sample routes return 404.
- `HomePageComponentTests` (bUnit, interactive) — selecting a category shows only that section and, for a category with
  more than five feeds, its deeper (30) cap with no More button.
- Interactivity (circuit boot, the More click growing a category, the order menu opening / reordering live / closing,
  and the category filter narrowing the page live and restoring it) is verified in a real browser against an isolated
  app instance, per the "prove it in a browser" convention — bUnit does not run `blazor.web.js`.
