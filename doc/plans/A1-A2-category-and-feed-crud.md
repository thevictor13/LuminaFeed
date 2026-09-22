# A1 & A2 — Category & Feed CRUD (edit/delete) — plan

> Session constraints this work was carried out under (recorded verbatim):
>
> > You must NEVER, EVER change anything beyond the contents of your working directory. Exceptions: the Claude memory, scratchpad and plans folders; %TEMP%; the main repo's .git for the phase-1 branch only (commit, no push, no stash pop); the NuGet global packages folder and NuGet caches (restore/add packages only); the ASP.NET Data Protection key ring and .NET SDK telemetry folders under the user profile; outbound HTTPS to nuget.org and to RSS publishers; and SMTP to localhost:25.
> > You may however read files that were gitignored in your working directory, which you will find in d:\Work\Sonrisa\LuminaFeed\. This is a rule you must not breach for the entirety of this session

## Context

The admin catalogue after the walking skeleton (**S1**) can only **create + list** categories and feeds
(`/admin/categories`, `/admin/feeds`). The [spec](../LuminaFeed%20Initial%20Specification.md) requires admins to
*edit the list of categories* and *edit the list of RSS feeds — delete or add a new one (basically a CRUD)*. The
[master plan](./00-master-plan.md) schedules this as two Phase-2 items — **A1 (Category CRUD)** and
**A2 (Feed CRUD)** — both building on S1. They are covered by this one plan but **committed separately**.

The schema (from [G0.3](./G0.3-domain-model.md)) already encodes the delete rules: `Feed → Category` is
**Restrict** (a category with feeds can't be deleted), `Article → Feed` is DB **Cascade**, and `Subscription → Feed`
is EF **ClientCascade** (User owns the single DB cascade path into `Subscriptions`, so EF issues the feed's
subscription deletes client-side). The `CategoryErrors.NotFound` / `FeedErrors.NotFound` factories already existed,
unused, for exactly this work.

## Decisions

- **Edit UX = the existing form in a modal dialog.** Clicking **Edit** on a row opens a dialog holding the same
  fields, pre-filled (**Save changes** / **Cancel**). The inline **Add** form at the top of each page is unchanged.
- **Delete = a confirmation dialog** (same component).
  - **Feed delete: allow + warn** — the dialog reports how many subscriptions and stored articles go with it
    (cascade), then proceeds.
  - **Category delete: blocked while it holds feeds** — the row's Delete button is disabled when `FeedCount > 0`,
    and `DeleteAsync` also refuses with a `Conflict` (`Category.HasFeeds`).
- **Modal = conditionally-rendered Bootstrap markup, CSS-only, no JS interop.** Bootstrap's CSS is loaded in
  `App.razor`; its JS bundle is not. Driving the dialog from Blazor state (`class="modal d-block show"` + a
  `modal-backdrop`) keeps it in the SignalR circuit and bUnit-testable, and sidesteps the JS-dialog pitfalls
  CLAUDE.md warns about. A reusable `Components/Shared/Modal.razor` (Title, Visible, OnClose, ChildContent; static
  backdrop) is introduced in A1 and reused by A2 (and later by C1's subscribe dialog). The edit dialog's inputs use
  `edit-*` ids so they don't collide with the add form's ids.
- **Service shape mirrors `CreateAsync`**: normalize → validate (FluentValidation) → not-found / conflict checks →
  `SaveChangesAsync` inside the same `DbUpdateException` race re-check → return the summary (`ErrorOr<T>`); delete
  returns `ErrorOr<Deleted>`. The duplicate-name / duplicate-URL scans gain a `Guid? excludingId` so an edited row
  doesn't clash with itself. Each service takes a second injected validator for its update request.

## A1 — Category CRUD (commit 1)

- **Services/Categories:** `UpdateCategoryRequest` + validator; `CategoryErrors.HasFeeds`;
  `ICategoryService`/`CategoryService` gain `UpdateAsync` and `DeleteAsync`; `NameExistsAsync` gains `excludingId`.
- **Components/Shared/Modal.razor** (new, shared).
- **Components/Admin/Categories.razor:** an Actions column (Edit / Delete, aria-labelled; Delete disabled when the
  category has feeds), an edit dialog, a delete-confirm dialog, and a success notice covering added/updated/deleted.
- **Tests:** `CategoryServiceTests` (update persists/trims, duplicate-name conflict ignoring the row itself, case
  variant of own name allowed, unknown id not-found, blank-name validation; delete removes an empty category,
  conflicts on one with feeds, not-found on an unknown id) and `AdminFormsComponentTests` (edit dialog updates the
  row and closes; editing onto an existing name shows the conflict inside the dialog; deleting an empty category
  removes the row; the Delete button is disabled for a category with feeds).

## A2 — Feed CRUD (commit 2)

- **Services/Feeds:** `UpdateFeedRequest` + validator (mirrors `CreateFeedRequest`, adds `Id`, reuses
  `BeAbsoluteHttpUrl`); `IFeedService`/`FeedService` gain `UpdateAsync`, `DeleteAsync`
  (loads `.Include(f => f.Subscriptions)` for the client cascade) and `GetDeletionImpactAsync` →
  `FeedDeletionImpact(Id, Name, SubscriptionCount, ArticleCount)` for the warning dialog; `FeedUrlExistsAsync` gains
  `excludingId`. `FeedSummary` is left unchanged so the public main view gains no joins.
- **Components/Admin/Feeds.razor:** Actions column, an edit dialog reusing every field (incl. the category
  dropdown), and a delete-confirm dialog that shows the subscription/article impact.
- **Tests:** `FeedServiceTests` (update persists incl. category change, duplicate-URL conflict vs. another feed,
  unknown feed/category not-found, non-http URL validation; delete cascades subscriptions + articles, unknown id
  not-found; deletion-impact counts and not-found) and `AdminFormsComponentTests` (edit dialog with the category
  pre-selected; duplicate-URL conflict in the dialog; delete dialog shows the counts and removes the row).

## Verification

- `dotnet build LuminaFeed.slnx` — 0 warnings; `dotnet test` — green (existing suite + the new tests).
- Real-browser check (per CLAUDE.md "interactivity is only proven in a browser") on an **isolated** app instance
  (temp SQLite DB via `ConnectionStrings__DefaultConnection`, a distinct port, overridden `AdminSeed`): sign in as
  admin, open `/admin/categories` and `/admin/feeds`, open an edit dialog and save, open a delete dialog and
  confirm, and confirm the browser console shows no circuit errors and the table reflects the change.

## Out of scope

Reassigning a category's feeds on delete (blocked instead); admin user management and removing individual
subscriptions (**A3**); the public card paging/ordering/filter (**B1–B3**) that also consume this catalogue.
