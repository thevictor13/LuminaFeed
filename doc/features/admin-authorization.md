# Feature: Admin Authorization (G0.4)

Turns the `ApplicationUser.IsAdmin` flag into an ASP.NET Core authorization policy that guards the admin area.

## Components (`LuminaFeed/Authorization/`)

- **`AdminAuthorization`** — the single source of truth for the constants: `PolicyName = "Admin"`,
  `ClaimType = "IsAdmin"`, `ClaimValue = "true"`. `ClaimsFor(user)` returns the `IsAdmin` claim when `user.IsAdmin`,
  otherwise nothing.
- **`AdminClaimsPrincipalFactory`** — a `UserClaimsPrincipalFactory<ApplicationUser>` that appends
  `AdminAuthorization.ClaimsFor(user)` to the generated principal, so the flag is baked into the auth cookie and no
  per-request DB lookup is needed for authorization checks.

## Wiring (`Program.cs`)

- The factory is registered via `AddIdentityCore<ApplicationUser>()....AddClaimsPrincipalFactory<AdminClaimsPrincipalFactory>()`.
- The policy is registered via `AddAuthorizationBuilder().AddPolicy("Admin", p => p.RequireClaim("IsAdmin", "true"))`.
- Razor pages/components under the admin area (and the admin nav section) require the `Admin` policy. A bootstrap admin
  account is seeded so the area is reachable on a fresh DB — see [Seed Data](./seed-data.md).

## Denial behaviour

Two distinct outcomes, both driven by the Identity cookie handler:

- **Anonymous** request to an admin page → *challenged*: redirected to **`/Account/Login`** (with a `ReturnUrl`).
- **Authenticated non-admin** (signed in but lacking the `IsAdmin` claim) → *forbidden*: redirected to
  **`/Account/AccessDenied`**. They are **not** bounced back to login (they are already signed in).

## Known trade-off

Because the `IsAdmin` claim is **baked into the issued cookie**, changing a user's `IsAdmin` flag does **not** take
effect until their principal is regenerated (next sign-in / cookie refresh). There is no security-stamp revalidation of
this custom claim. Acceptable for the current admin model; revisit if near-real-time revocation is ever required.

## Tests

- `AdminAuthorizationTests` — `ClaimsFor` for admin vs. standard users, and end-to-end policy evaluation through a real
  `IAuthorizationService` (an admin-claimed principal succeeds; a name-only principal is denied).
- `AdminPagesTests` — through the real host: every admin route carries `[Authorize(Policy = "Admin")]`; an **anonymous**
  request is redirected to `/Account/Login`; an **authenticated non-admin** (seeded + signed in via the real login form)
  is redirected to `/Account/AccessDenied`; a signed-in **admin** is served the page.
