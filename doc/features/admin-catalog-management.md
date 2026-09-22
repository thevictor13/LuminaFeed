# Feature: Admin Catalogue Management (S1)

Admins can **add and list** categories and feeds. This is the walking-skeleton slice: edit/delete, richer validation
UX and image handling arrive with **A1** (Category CRUD) and **A2** (Feed CRUD).

## Pages (`LuminaFeed/Components/Admin/`)

All three require the `Admin` policy (see [Admin Authorization](./admin-authorization.md)); anonymous requests are
redirected to the login page with a `ReturnUrl`.

- **`/admin`** (`AdminHome`) — links to the two management pages.
- **`/admin/categories`** (`Categories`) — add form (name, optional description) above a table of all categories with
  their feed counts.
- **`/admin/feeds`** (`Feeds`) — add form (name, **category dropdown**, feed URL, site URL, optional image URL,
  popularity, optional description) above a table of all feeds. With no categories it points the admin at the
  categories page instead. After a successful add the category stays selected so several feeds can be added in a row.

The pages use `@rendermode InteractiveServer` and hold no business logic: they call the services below and render the
returned error descriptions through the shared `Components/Shared/ErrorList` alert. A `busy` flag ignores a second
submit while one is in flight.

## Services (`LuminaFeed/Services/`)

- **`Categories/ICategoryService` → `CategoryService`**
  - `ListAsync()` — `CategorySummary(Id, Name, Description, FeedCount)`, ordered by name.
  - `CreateAsync(CreateCategoryRequest)` — trims input, stores a blank description as `null`.
- **`Feeds/IFeedService` → `FeedService`**
  - `ListAsync()` — `FeedSummary` rows ordered by category name, then feed name.
  - `CreateAsync(CreateFeedRequest)` — trims input, stores blank optionals as `null`.

Both return **`ErrorOr<T>`**:

| Situation | Error |
|---|---|
| FluentValidation failure | `ErrorType.Validation`, `Code` = property name (one per failure) |
| Category name already used (case-insensitive) | `Conflict` — `Category.DuplicateName` |
| Feed URL already used (case-insensitive) | `Conflict` — `Feed.DuplicateFeedUrl` |
| Feed's category does not exist | `NotFound` — `Category.NotFound` |

**Validation** (`CreateCategoryRequestValidator`, `CreateFeedRequestValidator`, discovered by
`AddValidatorsFromAssemblyContaining<Program>()`): required name; length limits taken straight from the entities'
`*MaxLength` constants (`Category.NameMaxLength`, `Feed.UrlMaxLength`, …), which are also what the EF configuration and
the forms' `maxlength` attributes use, so they cannot drift (`LimitsTests` guards constant ↔ column); `FeedUrl` /
`SiteUrl` / optional `ImageUrl` must be **absolute http(s) URLs** (so `javascript:`/`ftp:`/relative values are
rejected); popularity ≥ 0.

The duplicate checks run before the insert; if a concurrent create still trips the unique index, the resulting
`DbUpdateException` is re-checked and reported as the same `Conflict`, and anything else is rethrown.

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
- `AdminPagesTests` — each admin page is routed and carries `[Authorize(Policy = "Admin")]`; anonymous requests are
  redirected to login with the `ReturnUrl`; a **signed-in admin** (seeded, signed in through the real login form)
  gets the pages rendered with the seeded catalogue.
- `HostBootTests` — the services resolve from the real DI graph, and the factory-created context sees the same seeded
  database as the scoped one. The shared `TestAppFactory` keeps its temp database next to the test binaries and
  clears the SQLite connection pools so the file is actually deleted.
