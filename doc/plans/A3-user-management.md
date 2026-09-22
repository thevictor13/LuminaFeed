# A3 — Admin User Management — plan

> **INVIOLABLE SESSION RULE (verbatim):**
>
> You must NEVER, EVER change anything beyond the contents of your working directory. Exceptions: the Claude memory, scratchpad and plans folders; %TEMP%; the current repo's .git for the current branch only (commit, no push, no stash pop); the NuGet global packages folder and NuGet caches (restore/add packages only); the ASP.NET Data Protection key ring and .NET SDK telemetry folders under the user profile; outbound HTTPS to nuget.org and to RSS publishers; and SMTP to localhost:25.
> You may however read files that were gitignored in your working directory, which you will find in d:\Work\Sonrisa\LuminaFeed\. This is a rule you must not breach for the entirety of this session

*(All build/test/EF commands write only under the working directory — `bin/obj` and the gitignored `app.db` — honoring the rule.)*

## Context

After the walking skeleton and the A1/A2 catalogue CRUD, the admin area (`/admin/categories`, `/admin/feeds`) can
fully manage the feed catalogue, but **user management does not exist** — `AdminHome.razor` still carries the
placeholder *"User management will live here too."* The [spec](../LuminaFeed%20Initial%20Specification.md) requires
admins to *see the list of signed-up users and their RSS feeds* and to *remove certain RSS subscriptions from users,
or delete user registrations completely*. The [master plan](./00-master-plan.md) schedules this as **A3** (it needs
subscriptions, which S3 delivered).

This plan adds a `/admin/users` page over a new `IUserAdminService`, mirroring the established admin CRUD pattern
(thin InteractiveServer page → `ErrorOr` service over `IDbContextFactory` → shared `Modal` / `ErrorList`).

### Scope decisions (locked with the user before implementation)

- **Grant/revoke-admin toggle: DEFERRED.** A3 builds only the three spec-enumerated capabilities (list users + their
  feeds, remove a subscription, delete a user). No `IsAdmin` toggle and **no `UserManager.UpdateSecurityStampAsync`**
  this round. The G0.9 security-stamp guard stays a forward note for whenever a toggle is built (see *Out of scope*).
- **Self-action guard: ON.** The acting admin cannot delete their own account — enforced in the UI (disabled button
  on their own row) **and** defensively in the service (`UserErrors.CannotDeleteSelf`).
- **Remove subscription = the whole row** (both channels). Per-channel unsubscribe stays with C1/C5.
- **No `UserManager`, no migration.** Deleting the `ApplicationUser` row cascades its `Subscription` rows (and
  Identity's satellite rows) at the DB — `Subscription → User` is `DeleteBehavior.Cascade` (the single DB cascade path
  chosen in G0.3/G0.8; `SubscriptionConfiguration.cs`). No schema change → no EF migration.

## Design

### 1. Service — `LuminaFeed/Services/Users/` (new folder)

Mirrors `SubscriptionService` (id-only inputs, no FluentValidation request records). Constructor takes just the
factory: `sealed class UserAdminService(IDbContextFactory<ApplicationDbContext> dbFactory) : IUserAdminService`,
opening a short-lived `await using var db = await dbFactory.CreateDbContextAsync(ct)` per operation.

- **`IUserAdminService.cs`** — records + interface:
  - `sealed record UserSummary(string Id, string Email, bool IsAdmin, IReadOnlyList<UserSubscriptionSummary> Subscriptions)`
  - `sealed record UserSubscriptionSummary(Guid FeedId, string FeedName, bool EmailEnabled, bool SlackEnabled)`
  - `Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken = default)`
  - `Task<ErrorOr<Deleted>> RemoveSubscriptionAsync(string userId, Guid feedId, CancellationToken = default)`
  - `Task<ErrorOr<Deleted>> DeleteUserAsync(string userId, string actingAdminId, CancellationToken = default)`
- **`UserAdminService.cs`**:
  - `ListAsync` — `db.Users.AsNoTracking().OrderBy(u => u.Email).Select(...)`, projecting each user's subscriptions
    (`u.Subscriptions.OrderBy(s => s.Feed.Name).Select(...)`). Same projection shape as `CategoryService.ListAsync`
    carrying a dependent collection.
  - `RemoveSubscriptionAsync` — `db.Subscriptions.Where(s => s.UserId == userId && s.FeedId == feedId)
    .ExecuteDeleteAsync(ct)`; `0` rows → `UserErrors.SubscriptionNotFound`, else `Result.Deleted` (idiom from
    `SubscriptionService.UnsubscribeAsync`).
  - `DeleteUserAsync` — first `if (userId == actingAdminId) return UserErrors.CannotDeleteSelf;` then load the user;
    `null` → `UserErrors.UserNotFound`; otherwise `db.Users.Remove(user); await db.SaveChangesAsync(ct);
    return Result.Deleted;`. Subscriptions cascade at the DB (no `.Include` needed — unlike `FeedService.DeleteAsync`,
    where `Subscription → Feed` is `ClientCascade`).
- **`UserErrors.cs`** — static-readonly `Error` fields (matching `SubscriptionErrors` style): `UserNotFound`
  (`NotFound` `"User.NotFound"`), `SubscriptionNotFound` (`NotFound` `"User.SubscriptionNotFound"`),
  `CannotDeleteSelf` (`Conflict` `"User.CannotDeleteSelf"`).

### 2. Page — `LuminaFeed/Components/Admin/Users.razor` (new, `/admin/users`)

Same shape as `Categories.razor`/`Feeds.razor`: `@attribute [Authorize(Policy = AdminAuthorization.PolicyName)]`,
`@rendermode InteractiveServer`, `@inject IUserAdminService UserAdminService`, back-link to `admin`,
`<ErrorList Messages="errors"/>` + a success `notice`, `users` list (null = loading) loaded in `OnInitializedAsync`,
busy flags, try/finally handlers that reload the list and set the notice.

- **Current admin id** from the authenticated principal (never client input): `[CascadingParameter]
  Task<AuthenticationState>? AuthenticationState`, read `ClaimTypes.NameIdentifier` in `OnInitializedAsync` into
  `currentUserId` — the same approach `Home.razor` uses.
- **Table** — one row per user: **Email**, an **Admin** badge when `IsAdmin`, a **Subscriptions** cell listing each
  feed name (with small email/Slack channel badges) each followed by a **Remove** button
  (`aria-label="Remove <feed> subscription from <email>"`), and an **Actions** cell with a **Delete user** button
  (`aria-label="Delete user <email>"`), rendered `disabled` with a `title` when `user.Id == currentUserId`
  (mirrors the disabled-delete idiom in `Categories.razor`). A user with no subscriptions shows a muted "—".
- **Two confirm dialogs** via the shared `Components/Shared/Modal.razor` (CSS-only, static backdrop), driven by two
  nullable state fields (as `Categories` drives its edit/delete modals):
  - `subscriptionToRemove` → *Remove subscription* modal, `btn-danger` confirm → `RemoveSubscriptionAsync`.
  - `userToDelete` → *Delete user* modal, warning it permanently deletes the account and removes its **N**
    subscription(s) (count is already in the loaded `UserSummary`), `btn-danger` confirm →
    `DeleteUserAsync(userToDelete.Id, currentUserId!)`.
- Errors from a failed call surface in the per-dialog `ErrorList`, matching the A1/A2 in-dialog conflict pattern.

### 3. Wiring

- **`Program.cs`** — add `builder.Services.AddScoped<IUserAdminService, UserAdminService>();` beside the other
  services.
- **`AdminHome.razor`** — add a **Users** list item and remove the placeholder paragraph.
- **`NavMenu.razor`** — no change (a single `Admin` link gates the whole area; individual pages are reached from
  `AdminHome`).

## Tests

- **`UserAdminServiceTests`** (new; `SqliteTestDatabase` + `AddUser`/`AddFeed` + direct `Subscription` seeding,
  mirroring `SubscriptionServiceTests`): list ordering + per-user subscriptions + isolation; remove touches only that
  `(user, feed)` row and returns not-found otherwise; delete cascades the user's subscriptions (other users' rows and
  all `Article`s survive), unknown id → not-found, self-delete → `CannotDeleteSelf` (user still exists).
- **`AdminPagesTests`** — extend the routing + anonymous-redirect theories with `/admin/users`; a signed-in admin GET
  renders the page.
- **`UserAdminComponentTests`** (new bUnit `BunitContext`, real `UserAdminService(_db)`, `AddAuthorization()...
  SetClaims(NameIdentifier = adminUser.Id)`, `SetRendererInfo("Server", isInteractive:true)` before `Render<Users>()`):
  the table lists users + feeds; Remove opens a modal and deletes the row; Delete opens a modal (with the count) and
  deletes the user; the Delete button on the acting admin's own row is disabled.
- **`HostBootTests`** — assert `IUserAdminService` resolves from the real DI graph.

## Docs

- **New** `doc/features/admin-user-management.md`.
- **`doc/features/admin-catalog-management.md`** — a pointer to the new user-management doc.
- **`doc/plans/00-master-plan.md`** — tick **A3**; record the deferred `IsAdmin` toggle under *Deferred / out of scope*.
- **`LuminaFeed/Components/Admin/README.md`** — mark user management (A3) as built.

## Verification

1. `DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build LuminaFeed.slnx` — 0 warnings; `dotnet test` — green.
2. **Real-browser check** on an isolated app instance (temp SQLite DB via `ConnectionStrings__DefaultConnection`, a
   distinct port, overridden `AdminSeed`, SMTP to localhost:25 only): sign in as the seeded admin, open `/admin/users`,
   confirm the users + subscriptions render, remove a subscription, delete a *non-admin* test user, confirm the own-row
   Delete button is disabled, and check the browser console shows **no circuit errors**.

## Out of scope

- **Grant/revoke `IsAdmin` from the UI** and its `UserManager.UpdateSecurityStampAsync` refresh — deferred (would also
  be the place to fix the seeder's promote-path, which omits the stamp bump but is harmless at startup).
- **Per-channel** subscription removal — C1/C5; A3 removes the whole row.
- **Last-admin protection** (blocking deletion of the final admin) — only self-deletion is guarded.
- **User-list paging/search** — the list is small; not required by the spec.
