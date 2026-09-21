# Components/Admin

Admin-only Blazor components, guarded by the `"Admin"` authorization policy (backed by `ApplicationUser.IsAdmin`).

**Implemented (S1):** `AdminHome` (`/admin`), `Categories` (`/admin/categories`) and `Feeds` (`/admin/feeds`) —
minimal add + list. See `doc/features/admin-catalog-management.md`.

**Planned (Phase 2):** category/feed edit + delete (A1/A2) and user management (A3).
