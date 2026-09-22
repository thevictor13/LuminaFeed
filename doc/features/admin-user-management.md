# Feature: Admin User Management (A3)

Admins can manage the people who have registered: **see every signed-up user and the feeds they subscribe to**,
**remove an individual subscription** from a user, and **delete a user registration** entirely. This is the third
admin track (after A1/A2 catalogue CRUD) and lives alongside them under `/admin`.

## Page (`LuminaFeed/Components/Admin/Users.razor`, `/admin/users`)

Requires the `Admin` policy (see [Admin Authorization](./admin-authorization.md)); anonymous requests are redirected
to login with a `ReturnUrl`. Reached from `AdminHome` (`/admin`).

The page uses `@rendermode InteractiveServer` and holds no business logic: it calls `IUserAdminService` and renders
returned error descriptions through the shared `Components/Shared/ErrorList` alert. A table lists every user, ordered
by email, with:

- **User** — the email address, an **Admin** badge when `IsAdmin`, and a **You** badge on the acting admin's own row.
- **Subscriptions** — each subscribed feed's name with small **Email** / **Slack** channel badges, followed by a
  **Remove** button (`aria-label="Remove <feed> subscription from <email>"`). A user with none shows a muted "—".
- **Actions** — a **Delete user** button (`aria-label="Delete user <email>"`).

Two destructive actions each open a confirmation in the shared **`Components/Shared/Modal`** (CSS-only Bootstrap
markup, static backdrop, no JS interop — so it stays inside the SignalR circuit and is bUnit-testable):

- **Remove subscription** — confirms removing that one feed from that one user.
- **Delete user** — warns that it permanently deletes the account **and** removes its N subscription(s) (the count is
  read fresh when the dialog opens, via `GetSubscriptionCountAsync`, falling back to the list snapshot if that read
  fails), then proceeds. Deleting a user cascades their subscriptions at the database.

### Self-guard

The acting admin **cannot delete their own account** (that would risk locking themselves out). The **Delete user**
button on their own row is `disabled` with an explanatory `title`, and the service refuses it defensively
(`UserErrors.CannotDeleteSelf`) even if the disabled button is bypassed. The acting admin's id always comes from the
**authenticated principal** (`ClaimTypes.NameIdentifier`, via the cascading `AuthenticationState`), never from client
input. Deleting or acting on *other* admins is allowed; there is no last-admin protection (out of scope).

## Service (`LuminaFeed/Services/Users/IUserAdminService` → `UserAdminService`)

Mirrors `SubscriptionService`: id-only inputs (no FluentValidation request records), a **short-lived context per
operation** from the injected `IDbContextFactory<ApplicationDbContext>`.

- `ListAsync()` — `UserSummary(Id, Email, IsAdmin, IReadOnlyList<UserSubscriptionSummary>)` per user, ordered by email;
  each `UserSubscriptionSummary(FeedId, FeedName, EmailEnabled, SlackEnabled)` is ordered by feed name.
- `RemoveSubscriptionAsync(userId, feedId)` — `ExecuteDelete` of that `(UserId, FeedId)` row; `NotFound` when nothing
  matched.
- `GetSubscriptionCountAsync(userId)` — the user's current subscription count for the delete-confirm warning;
  `NotFound` for an unknown id. (Mirrors `FeedService.GetDeletionImpactAsync` — read fresh, not from the list snapshot.)
- `DeleteUserAsync(userId, actingAdminId)` — refuses a blank `actingAdminId` **or** `userId == actingAdminId` with a
  `Conflict` (the acting id must come from the authenticated principal; a blank one would otherwise slip past the
  self-guard); otherwise loads the
  user (`NotFound` if unknown), removes it, and its subscriptions cascade at the DB. **No `.Include` is needed** —
  `Subscription → User` is `DeleteBehavior.Cascade` (the single DB cascade path chosen in G0.3/G0.8; contrast
  `FeedService.DeleteAsync`, which must `.Include` because `Subscription → Feed` is `ClientCascade`). Identity's own
  satellite rows (claims/logins/tokens) cascade the same way. **No `UserManager`** is involved because A3 defers the
  admin-toggle; the security-stamp refresh (`UserManager.UpdateSecurityStampAsync`) is only needed when toggling
  `IsAdmin`, which is out of scope here.

All fallible operations return **`ErrorOr<T>`**:

| Situation | Error |
|---|---|
| Removing a subscription that isn't there | `NotFound` — `User.SubscriptionNotFound` |
| Deleting an unknown user | `NotFound` — `User.NotFound` |
| Deleting your own account, or a blank acting-admin id | `Conflict` — `User.CannotDeleteSelf` |

Registered as `AddScoped<IUserAdminService, UserAdminService>()` in `Program.cs`.

## Tests

- `UserAdminServiceTests` — real in-memory SQLite (`SqliteTestDatabase`): list ordering + per-user subscriptions
  (feed names, channel flags) + isolation; remove touches only that `(user, feed)` row and is `NotFound` otherwise;
  delete cascades the user's subscriptions while another user's rows and the feed's articles survive, unknown id is
  `NotFound`, self-delete is `CannotDeleteSelf` with the user left intact, deleting **another admin** succeeds (no
  last-admin protection), a **blank acting id** is `CannotDeleteSelf`, and the delete also cascades an Identity
  **satellite row** (a seeded `AspNetUserLogins` entry). `GetSubscriptionCountAsync` returns the live count and
  `NotFound` for an unknown id. (The delete tests confirm the DB cascade fires under `EnsureCreated()` — EF's SQLite
  provider enables `PRAGMA foreign_keys`.)
- `AdminPagesTests` — `/admin/users` is routed and carries `[Authorize(Policy = "Admin")]`; an anonymous request is
  redirected to login with the `ReturnUrl`; a signed-in admin's GET lists the signed-in user.
- `UserAdminComponentTests` (bUnit, real service on in-memory SQLite; the acting admin signed in via a
  `ClaimTypes.NameIdentifier` claim) — the interactive paths: the table lists users and their feeds; **Remove** opens
  the modal and deletes the subscription; **Delete user** opens the modal showing the subscription count and deletes
  the user (no orphaned subscriptions); the **Delete user** button on the acting admin's own row is `disabled`.
- `HostBootTests` — `IUserAdminService` resolves from the real DI graph.

## Out of scope (deferred)

- **Grant/revoke `IsAdmin` from the UI** (and its `UserManager.UpdateSecurityStampAsync` refresh) — deferred this
  round; see the master plan.
- **Per-channel** subscription removal (email-only / Slack-only) — arrives with C1/C5; A3 removes the whole row.
- **Last-admin protection** and **user-list paging/search**.
