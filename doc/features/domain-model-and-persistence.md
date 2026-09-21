# Feature: Domain Model & Persistence (G0.3)

The core domain entities and their EF Core (SQLite) persistence. Schema only — seeding is [Seed Data](./seed-data.md).

## Entities (`LuminaFeed/Domain/`)

All custom entities derive from **`EntityBase`**, whose `Id` is a **GUID v7** (`Guid.CreateVersion7()`,
time-ordered) generated on construction. `Id` has a **private setter** so the client-generated value can't be
overwritten; EF is configured with `ValueGeneratedNever()` to store it verbatim.

- **`Category`** — `Name` (required, unique, ≤100), `Description?` (≤1000), `Feeds` nav.
- **`Feed`** — `Name` (≤200), `CategoryId` + `Category` nav, `FeedUrl` (required, unique, ≤2048), `SiteUrl` (≤2048),
  `ImageUrl?` / `Description?`, `Popularity` (fixed sort figure), plus the polling-cache columns populated later by
  C4: `ETag?` (≤512), `LastModified?` (≤256, raw HTTP value), `LastPolledAt?`. Navs: `Subscriptions`, `Articles`.
- **`Subscription`** — `UserId` (string FK to Identity, ≤450) + `User` nav, `FeedId` + `Feed` nav, `EmailEnabled`,
  `SlackEnabled`, `SlackWebhookUrl?` (≤2048), `CreatedAt` (private-set, UtcNow). The `SlackEnabled ⇒ SlackWebhookUrl`
  invariant is **documented but not enforced at this layer** — it is validated in the Phase-2 subscribe flow (C1).
- **`Article`** — `FeedId` + `Feed` nav, `ExternalId` (RSS `<guid>` else link, the de-dup key), `Title` (≤500),
  `Link` (≤2048), `Summary?` (unbounded), `ImageUrl?`, `PublishedAt?`, `FetchedAt` (private-set, UtcNow).

`ApplicationUser` (`Data/`) extends `IdentityUser` with a `bool IsAdmin` flag and a `Subscriptions` nav. It keeps
Identity's string key, so it is the one persisted entity not on a GUID v7 id.

## Persistence (`LuminaFeed/Data/`)

- **`ApplicationDbContext`** — `IdentityDbContext<ApplicationUser>` with `DbSet`s for the four entities;
  `ApplyConfigurationsFromAssembly` auto-discovers one `IEntityTypeConfiguration` per entity in `Data/Configurations/`.
- **Unique indexes** — `Category.Name`, `Feed.FeedUrl`, `Subscription (UserId, FeedId)`, `Article (FeedId, ExternalId)`.
- **Sort index** — `Feed (CategoryId, Popularity)` backs the public list's default ordering (feeds by popularity within a category).
- **Delete behaviour** — `Feed → Category` **Restrict** (can't delete a category with feeds); `Article → Feed` **Cascade**;
  `Subscription → User` **Cascade** (DB); `Subscription → Feed` **ClientCascade**. Subscription is reachable from both
  User and Feed, and two DB cascade paths into one table are rejected by SQL Server, so Feed cascades subscriptions at
  the EF level (the app deletes feeds by loading them with their dependents) while User is the single DB-level owner.

## Migrations

`Data/Migrations/*_AddDomainModel` creates the four tables, the `IsAdmin` column, and the indexes above. GUID PKs and
`DateTimeOffset` values are stored as `TEXT` (SQLite). Because the app is pre-release with a gitignored, regenerated
`app.db`, this migration is **regenerated in place** (not stacked) when the domain schema changes; keep the EF tools
version aligned with the runtime EF Core version to avoid a snapshot/runtime mismatch.

> Startup applies migrations automatically (`MigrateAsync`) — a temporary, not-production-ready shortcut (see
> [Seed Data](./seed-data.md) and [Configuration & Options](./configuration-options.md)).

## Tests (`LuminaFeed.Tests/DomainModelTests.cs`)

Run against real in-memory SQLite (kept-open connection, `EnsureCreated`): GUID v7 id; full-graph round-trip with
includes; both unique-index violations throw; deleting a (tracked) Feed cascades to its Articles + Subscriptions;
deleting a Category with feeds is restricted; and the `(CategoryId, Popularity)` index exists on the model.
