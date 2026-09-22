# Feature: Admin Catalogue Management (S1 · A1 · A2)

Admins can manage the feed catalogue: both **categories** (**A1**) and **feeds** (**A2**) are full CRUD — create,
list, **edit** and **delete**. Editing and deleting happen in a **modal dialog** opened from the row, so the list
stays in place.

Managing the registered **users** (listing them, removing their subscriptions, deleting registrations) is a separate
page — see [Admin User Management](./admin-user-management.md) (**A3**).

## Pages (`LuminaFeed/Components/Admin/`)

All three require the `Admin` policy (see [Admin Authorization](./admin-authorization.md)); anonymous requests are
redirected to the login page with a `ReturnUrl`.

- **`/admin`** (`AdminHome`) — links to the catalogue and user-management pages.
- **`/admin/categories`** (`Categories`) — add form (name, optional description) above a table of all categories with
  their feed counts. Each row has **Edit** and **Delete** actions (**A1**). Delete is **disabled while the category
  still has feeds** (`FeedCount > 0`) — `Feed → Category` is `Restrict`, so those feeds must be moved or deleted first.
- **`/admin/feeds`** (`Feeds`) — add form (name, **category dropdown**, feed URL, site URL, optional image URL,
  popularity, optional description) above a table of all feeds. With no categories it points the admin at the
  categories page instead. After a successful add the category stays selected so several feeds can be added in a row.
  Each row has **Edit** and **Delete** actions (**A2**); the edit dialog reuses every field including the category
  dropdown. Deleting a feed **also removes its subscriptions and stored articles** (cascade), so the delete dialog
  first fetches and shows those counts (`IFeedService.GetDeletionImpactAsync`) as a warning before confirming.

The pages use `@rendermode InteractiveServer` and hold no business logic: they call the services below and render the
returned error descriptions through the shared `Components/Shared/ErrorList` alert. A `busy` flag ignores a second
submit while one is in flight; separate flags gate the edit and delete dialogs. One success notice covers
added / updated / deleted.

### Edit & delete dialog (A1)

`Components/Shared/Modal.razor` is a small reusable dialog: **conditionally-rendered Bootstrap markup, CSS-only, no
JS interop** (only Bootstrap's CSS is loaded, not its JS bundle), so it stays inside the SignalR circuit and is
bUnit-testable. Its backdrop is static (a stray click won't discard an in-progress edit); it closes via the header
`×`, the Cancel button, or the **Escape** key (an intentional keypress, unlike a stray click). Focus moves into the
dialog when it opens (`FocusAsync` on the container, `tabindex="-1"`), so keyboard users land inside it and Escape works
immediately. On close it does **not** programmatically return focus to the invoking row control, and it does not trap
Tab: both would need either a per-row trigger `ElementReference` (which Blazor can't capture inside a `@foreach`, and
which is gone once a delete removes the row) or reading `document.activeElement` via custom JS interop — which the
component deliberately avoids to stay circuit-only and bUnit-testable. Focus-*in* (the WCAG-critical half) is handled;
focus-*return* is a documented limitation. Clicking **Edit** on a row opens it holding the same form, pre-filled (the dialog's inputs
use `edit-*` ids so they don't collide with the add form); clicking **Delete** opens a confirmation. On success the
dialog closes, the table refreshes and the notice updates. (C1's subscribe dialog will reuse this component.)

## Services (`LuminaFeed/Services/`)

- **`Categories/ICategoryService` → `CategoryService`**
  - `ListAsync()` — `CategorySummary(Id, Name, Description, FeedCount)`, ordered by name.
  - `CreateAsync(CreateCategoryRequest)` — trims input, stores a blank description as `null`.
  - `UpdateAsync(UpdateCategoryRequest)` **(A1)** — trims input; not-found for an unknown id; duplicate-name check
    ignores the row being edited.
  - `DeleteAsync(Guid id)` **(A1)** — `ErrorOr<Deleted>`; not-found for an unknown id; refuses with a conflict while
    the category still holds feeds — checked up front, and **re-checked** if a feed is assigned concurrently, so the
    `Restrict` FK surfaces as the same `Category.HasFeeds` conflict rather than an unhandled error.
- **`Feeds/IFeedService` → `FeedService`**
  - `ListAsync()` — `FeedSummary` rows ordered by category name, then feed name.
  - `CreateAsync(CreateFeedRequest)` — trims input, stores blank optionals as `null`.
  - `UpdateAsync(UpdateFeedRequest)` **(A2)** — trims input; not-found for an unknown feed **or** category;
    duplicate-URL check ignores the row being edited. Changing the **feed URL** clears the feed's cached
    `ETag` / `LastModified` (the conditional-request state a future C4 poll would send), so the new endpoint starts
    clean; a non-URL edit leaves them untouched.
  - `GetDeletionImpactAsync(Guid id)` **(A2)** — `FeedDeletionImpact(Id, Name, SubscriptionCount, ArticleCount)` for
    the delete warning; not-found for an unknown id. (`FeedSummary` is deliberately left unchanged, so the public
    main view gains no extra joins.)
  - `DeleteAsync(Guid id)` **(A2)** — `ErrorOr<Deleted>`; not-found for an unknown id. Loads the feed with its
    `Subscriptions` included, because `Subscription → Feed` is **`ClientCascade`** (the User owns the single DB
    cascade path into `Subscriptions`, so EF must delete the feed's subscriptions itself); articles cascade at the DB.

The services take a second injected validator for the update request (both are auto-discovered by
`AddValidatorsFromAssemblyContaining<Program>()`). All fallible operations return **`ErrorOr<T>`**:

| Situation | Error |
|---|---|
| FluentValidation failure | `ErrorType.Validation`, `Code` = property name (one per failure) |
| Category name already used (case-insensitive) | `Conflict` — `Category.DuplicateName` |
| Deleting a category that still has feeds | `Conflict` — `Category.HasFeeds` |
| Feed URL already used (case-insensitive) | `Conflict` — `Feed.DuplicateFeedUrl` |
| Editing/creating against a missing row | `NotFound` — `Category.NotFound` / `Feed.NotFound` |

**Validation** (`CreateCategoryRequestValidator`, `CreateFeedRequestValidator`, discovered by
`AddValidatorsFromAssemblyContaining<Program>()`): required name; length limits taken straight from the entities'
`*MaxLength` constants (`Category.NameMaxLength`, `Feed.UrlMaxLength`, …), which are also what the EF configuration and
the forms' `maxlength` attributes use, so they cannot drift (`LimitsTests` guards constant ↔ column); `FeedUrl` /
`SiteUrl` / optional `ImageUrl` must be **absolute http(s) URLs** (so `javascript:`/`ftp:`/relative values are
rejected); popularity ≥ 0.

The duplicate checks run before the insert; if a concurrent create still trips the unique index, the resulting
`DbUpdateException` is re-checked and reported as the same `Conflict`, and anything else is rethrown. The category
delete guards the `Feed → Category` `Restrict` FK the same way: if a feed is assigned between the up-front count and
the save, the `DbUpdateException` is re-checked and reported as `Category.HasFeeds`.

## Persistence access: `IDbContextFactory`

`Program.cs` registers `AddDbContextFactory<ApplicationDbContext>` (instead of `AddDbContext`). Interactive Server
circuits and the polling background service outlive a request, so application services create a **short-lived
context per operation** rather than sharing a long-lived scoped one (which invites "a second operation was started on
this context" errors and stale tracked entities). `AddDbContextFactory` also registers a scoped `ApplicationDbContext`,
so Identity's stores and the seeders are unchanged.

## Tests

- `CategoryServiceTests`, `FeedServiceTests` — real in-memory SQLite (`SqliteTestDatabase`, an
  `IDbContextFactory` over one kept-open connection): persistence + GUID v7 ids, trimming, blank → `null`,
  validation errors, URL scheme rejection, conflicts ignoring case, unknown category, list ordering, feed counts.
  `CategoryServiceTests` also covers **A1**: update persists/trims and keeps its feed count, a rename onto another
  category is a conflict while a case-variant of the row's own name is allowed, unknown-id updates/deletes are
  not-found, and deleting a category with feeds is a `Category.HasFeeds` conflict — including when the feed is added
  concurrently, during the delete's own save (an EF interceptor drives the race), so the `Restrict` FK violation is
  reported as the conflict instead of escaping.
  `FeedServiceTests` also covers **A2**: update persists including a category change, a duplicate-URL conflict vs.
  another feed (a case-variant of the row's own URL is allowed), unknown-feed / unknown-category not-found, non-http
  URL validation; delete cascades the feed's subscriptions and articles while leaving other feeds' rows intact;
  the deletion-impact counts (and not-found); and a feed-URL change clears the cached `ETag` / `LastModified` while a
  non-URL edit keeps them.
- `AdminPagesTests` — each admin page is routed and carries `[Authorize(Policy = "Admin")]`; anonymous requests are
  redirected to login with the `ReturnUrl`; a **signed-in admin** (seeded, signed in through the real login form)
  gets the pages rendered with the seeded catalogue (the category dropdown lists a seeded category by id).
- `AdminFormsComponentTests` (bUnit, real services on in-memory SQLite) — the forms **submitted**: adding a category
  / feed shows the success alert, refreshes the table and resets the form (the feeds form keeps its category
  selected); a duplicate name shows the conflict; a blank submit lists every validation message; a `javascript:`
  feed URL is rejected; with no categories the feeds page points at the categories page instead of a form.
  **A1 dialogs:** the Edit dialog updates the row and closes, editing onto an existing name shows the conflict
  inside the dialog, deleting an empty category removes the row, and the Delete button is disabled for a category
  that has feeds. **A2 dialogs:** the feed Edit dialog opens with the category pre-selected and updates the row,
  editing onto another feed's URL shows the conflict inside the dialog, and the Delete dialog shows the
  subscription / article impact and then removes the row (and its subscriptions and articles). **Modal behaviour:**
  pressing **Escape** in the edit dialog closes it without saving; a blank name in the edit dialog surfaces the
  validation message *inside* the dialog (leaving the row unchanged); cancelling a delete removes nothing; and a feed
  with no subscriptions or articles shows the plain delete confirmation (no impact line).
- `HostBootTests` — the services resolve from the real DI graph, and the factory-created context sees the same seeded
  database as the scoped one. The shared `TestAppFactory` keeps its temp database next to the test binaries and
  clears the SQLite connection pools so the file is actually deleted.
