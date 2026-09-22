# Feature: Public Feed List (S2)

The public main view (`/`): every admin-curated feed shown as a **card**, grouped under its category. This is the
walking-skeleton slice — the 5-per-category cap, the "more" button, the ordering control and the category filter arrive
with **B1–B3**, and the per-feed article page with **B4**.

## Page (`LuminaFeed/Components/Pages/Home.razor`)

- Anonymous-accessible. Renders one `<section>` per category (heading + a responsive Bootstrap card grid:
  1 column on phones → 5 per row on wide screens).
- States: _loading_, _"No feeds have been added yet."_ for an empty catalogue, otherwise the grouped cards.
- Since S3 the page runs with `@rendermode InteractiveServer` (for the per-card subscribe button). It persists only
  the user's subscribed ids across the prerender → circuit hand-off and re-queries the catalogue on the interactive
  render: persisted state is sent back in the circuit-start message, which SignalR caps at 32 KB, and persisting the
  whole catalogue (~87 KB) made every circuit fail at start — nothing on the page was interactive. A page test keeps
  the persisted payload under 8 KB. See [Subscriptions](./subscriptions.md).
- Replaces the template's "Hello, world" page. The template sample pages (`Counter`, `Weather`, `Auth`), their nav
  links, the layout's "About" link and the dead `System.Net.Http` imports were removed; the nav now has a single
  **Feeds** entry (plus Admin / account links).

## Card (`LuminaFeed/Components/Shared/FeedCard.razor`)

Shows the feed's **image when it has one** (`ImageUrl`, lazy-loaded, no referrer, letterboxed via scoped CSS), its
name, its description when present, and a **Visit site** button (`SiteUrl`, new tab, `rel="noopener noreferrer"`).
An `Actions` render fragment lets pages add buttons next to it (used by the subscribe button).

## Service (`IFeedService.ListByCategoryAsync`)

Returns `CategoryFeeds(CategoryId, CategoryName, Feeds)` — **categories by name**, each with its feeds by
**`Popularity` descending, then name** (the fixed default order). Categories without feeds are omitted. One SQL
query ordered by category name (a join, so the `(CategoryId, Popularity)` index doesn't serve it — that index is for
the per-category views of B1–B3), grouped in memory; a short-lived context per call.

A feed created through the admin page ([Admin Catalogue Management](./admin-catalog-management.md)) appears here on
the next load — the first link of the skeleton's definition of done.

## Tests

- `FeedServiceTests` — grouping and ordering (popularity, name tie-break), empty categories omitted, empty catalogue,
  a feed added through `CreateAsync` shows up in the grouped list.
- `PublicPagesTests` — the real host serves `/` anonymously with all 115 seeded feeds as cards under their
  categories; a feed added via `IFeedService` is rendered with its image and site link; the removed sample routes
  return 404.
