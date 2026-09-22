# LuminaFeed

An online service where users register and subscribe to admin-curated, categorised RSS feeds, and get alerted by
email (and, later, Slack) when a feed publishes something new.

The behaviour is specified in [`doc/LuminaFeed Initial Specification.md`](doc/LuminaFeed%20Initial%20Specification.md);
the roadmap and its decisions live in [`doc/plans/00-master-plan.md`](doc/plans/00-master-plan.md); every feature
that exists has a page under [`doc/features/`](doc/features/).

## Status

Phase 0 (groundwork) and Phase 1 (the walking skeleton: admin adds a feed → it appears publicly → a signed-in user
subscribes → polling fetches new articles → the user is emailed) are complete and reviewed (`P1.R`). Phase 2 broadens
it: category/feed CRUD and user management, card paging/ordering/filter and the feed page, the subscribe dialog with
Slack, conditional feed requests, richer emails and RFC 8058 unsubscribe.

## Prerequisites

- .NET SDK **10.0.102** or later 10.0.x (`global.json`, `rollForward: latestFeature`).
- [Papercut SMTP](https://github.com/ChangemakerStudios/Papercut-SMTP) listening on `localhost:25` if you want to
  see the emails (confirmation links, new-article digests). Without it, sends fail and are logged; nothing else breaks.

## Run it

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project LuminaFeed --launch-profile http
```

Then open <http://localhost:5097>. On first start the app applies the EF migration, creates `LuminaFeed/Data/app.db`,
seeds the researched catalogue (10 categories, 115 feeds) and a development admin account:

| | |
|---|---|
| Admin login | `admin@luminafeed.local` / `ChangeMe!123` (from `appsettings.Development.json`) |
| Admin area | <http://localhost:5097/admin> |
| Polling | every minute, feeds with at least one subscriber only |

Register a user of your own from the **Register** link; the confirmation email lands in Papercut, and its
**Continue** button brings you back to the feed you were subscribing to.

## Configuration

Base values live in `LuminaFeed/appsettings.json`; the Development overlay adds the local SMTP, a throwaway unsubscribe
secret and the dev admin. Production must supply these itself (environment variables or user-secrets) — the base
file is deliberately not bootable on its own.

| Section | Keys |
|---|---|
| `Smtp` | `Host` (required), `Port`, `FromAddress` (required), `FromName`, `UseStartTls`, `UserName`, `Password` |
| `Polling` | `IntervalSeconds` (1–86 400, default 60), `FirstPollNotificationCap` (default 5), `CatchUpAfterMinutes` (default 360) |
| `Unsubscribe` | `HmacSecret` (required, ≥16 chars) |
| `AdminSeed` | `Email`, `Password` (optional; both set ⇒ a confirmed admin is seeded) |

Details: [`doc/features/configuration-options.md`](doc/features/configuration-options.md).

## Tests

```bash
dotnet test
```

xUnit, with real in-memory SQLite for the services, the real host (`WebApplicationFactory`) for pages and flows, and
bUnit for the interactive components. No test reaches the network or an SMTP server; host tests keep their temporary
databases under `bin/`.

## Layout

```
LuminaFeed/            the Blazor Server app
  Components/          Pages (public), Admin, Account (Identity), Shared, Layout
  Domain/              Category, Feed, Subscription, Article (GUID v7 ids, *MaxLength constants)
  Data/                DbContext, EF configurations, migrations, seeders
  Services/            Categories, Feeds, Subscriptions, Polling, Notifications, Email
  Options/             typed, startup-validated configuration
LuminaFeed.Tests/      xUnit + bUnit test project
doc/                   specification, feature docs, plans, research (seed catalogue)
```
