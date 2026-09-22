# Feature: Seed Data (G0.7)

Loads the admin-curated feed catalogue produced by the G0.R research into the database so the public feed list,
ordering, and category filter have real content.

## Source of truth

[`doc/research/rss-feeds.json`](../research/rss-feeds.json) — 10 categories, 115 validated feeds, each with a fixed
`popularity`. It is **embedded** into the app (`LuminaFeed.csproj` links it as an `EmbeddedResource` at
`Data/Seed/rss-feeds.json`), so there is one source of truth and no copy that can drift. To refresh the catalogue,
edit that JSON and rebuild.

The seeder bypasses the admin validator, so `SeedDataTests` holds the catalogue to the same URL rule (absolute
http(s)) and, for the values a browser renders — `siteUrl` and `imageUrl` — requires **https**: a plain-http image on
the https site is mixed content that some clients block. (Four image URLs and one whitespace-padded one were fixed
in P1.R; a few `feedUrl`s stay `http://` because that is what the publisher advertises, and the fetcher follows
their redirects over TLS.) Because seeding is insert-missing-only, an **existing** development database keeps the
old values until it is regenerated.

## Components (`LuminaFeed/Data/Seed/`)

- **`SeedCatalog` / `SeedCategory` / `SeedFeed`** — DTOs matching the JSON (field names map 1:1 to the domain model).
- **`SeedCatalogLoader`** — pure JSON parsing (`Parse`) plus embedded-resource loading (`LoadEmbedded`, resolved by
  file-name suffix). Throws if the catalogue is empty/invalid.
- **`DatabaseSeeder`** — applies the catalogue to the DB.

## Behaviour

- **Upsert by natural key**: categories by `Name`, feeds by `FeedUrl` (both unique indexes).
- **Insert-missing-only**: existing rows are never modified. This makes the seeder **idempotent** and ensures a
  re-seed does not clobber later admin edits (Feed/Category CRUD, A1/A2). New research entries added to the JSON are
  picked up on the next run.
- Feeds resolve their `CategoryId` from the seeded categories; a feed whose category is missing is logged and skipped.
- GUID v7 ids are generated client-side via `EntityBase`.

## Admin user seed (`AdminUserSeeder`)

So the admin area (`/admin`, guarded by the `Admin` policy) is reachable on a fresh database, a bootstrap admin
account is seeded from the optional **`AdminSeed`** config section (`AdminSeedOptions`):

- **Config-driven & safe by default**: only runs when both `AdminSeed:Email` and `AdminSeed:Password` are set, so
  **no admin is created unless explicitly configured**. `appsettings.Development.json` supplies a local dev admin
  (`admin@luminafeed.local`); the base `appsettings.json` leaves it empty. Production must supply values out of
  source control (env/user-secrets) or no admin is seeded.
- **Idempotent**: creates the user (with `EmailConfirmed = true`, so it can sign in under `RequireConfirmedAccount`)
  if missing; promotes an existing user to admin if needed; otherwise does nothing.
- **Fail-fast**: if `CreateAsync` fails (e.g. the configured password violates the Identity policy) the seeder logs
  the errors and throws, so a configured-but-unseeded admin surfaces loudly at startup rather than silently leaving
  the admin area unreachable.

## Startup wiring (`Program.cs`)

After the app is built, in a DI scope: `Database.MigrateAsync()`, then `DatabaseSeeder.SeedAsync(...)`, then
`AdminUserSeeder.SeedAsync()`, in **all environments**. A run logs
`Seed complete: N categories added, M feeds added, K feeds already present.` and, when configured,
`Seeded admin user '<email>'.`

> ⚠️ **Not production-ready.** Auto-migrating on startup is a deliberate temporary shortcut for this stage. It has no
> review gate, races across multiple instances, and no rollback path. Replace with a controlled migration step
> (CI/CD or an admin-triggered command) before any production deployment.

## Tests (`LuminaFeed.Tests/SeedDataTests.cs`)

Catalogue integrity (counts, referential integrity, distinct popularity/URLs, required fields); DB population with
resolved categories; idempotency; preservation of existing rows on re-seed; and pickup of newcomers after a partial
seed. `AdminUserSeederTests.cs` covers admin creation when configured, skip when unconfigured, promotion of an
existing user, idempotency, and fail-fast when creation fails.
