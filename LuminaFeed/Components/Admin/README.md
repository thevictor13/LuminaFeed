# Components/Admin

Admin-only Blazor components, guarded by the `"Admin"` authorization policy (backed by `ApplicationUser.IsAdmin`).

**Implemented:** `AdminHome` (`/admin`), `Categories` (`/admin/categories`) and `Feeds` (`/admin/feeds`) — full CRUD
(add / list / edit / delete, S1 + A1/A2; see `doc/features/admin-catalog-management.md`); `Users` (`/admin/users`) —
list users + their subscriptions, remove a subscription, delete a registration (A3; see
`doc/features/admin-user-management.md`).

**Planned (Phase 2):** grant/revoke `IsAdmin` from the user page (deferred; needs the `UpdateSecurityStampAsync`
guard).
