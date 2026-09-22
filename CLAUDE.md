# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Documentation

- **For broad context**, always read `doc/LuminaFeed Initial Specification.md`. That spec is the source of truth for detailed behaviour; the summaries below capture the durable decisions.
- **Doc map:** `doc/plans/00-master-plan.md` is the roadmap (phases, tracks, decisions, deferrals); every feature has a doc in `doc/features/` (kept up to date with the code); every implementation plan is a file in `doc/plans/`; `doc/research/rss-feeds.json` is the single source of the seeded feed catalogue (embedded into the app); `README.md` covers running the app locally.
- Any new features implemented must be documented in the `doc/features/` folder. These docs must constantly be kept up-to-date.
- **Any implementation plan you produce must be saved in the `doc/plans/` folder** (one Markdown file per plan).

## Product Overview

LuminaFeed is an online service where users register and subscribe to admin-curated, categorized RSS feeds. When feeds publish new items, subscribers receive alerts delivered as **email** and/or **Slack** notifications. Unauthenticated users are treated as having no subscriptions.

## Domain Model

- **Users** — standard vs. admin, distinguished by a simple `isAdmin` flag on the user table (no separate roles entity initially). The flag is baked into the auth cookie as an `IsAdmin` claim; toggling it later must refresh the user's security stamp.
- **Categories** — a dedicated entity. The real category set comes from the RSS feed research (`doc/research/`), **not** the spec's illustrative World News / Markets / Weather / Breaking News list. Assigned to a feed via dropdown.
- **RSS Feeds** — admin-curated, each belongs to a category. `Popularity` is a **fixed** figure per feed (from the research) that drives the default ordering; images are **URLs only** (RSS-provided, else a scraped `og:image`) — there is no admin upload.
- **Subscriptions** — per user, per feed; each carries the chosen notification channels (email and/or Slack, with the Slack webhook URL). The `SlackEnabled ⇒ SlackWebhookUrl` invariant is enforced in the subscribe flow (C1), not by the entity.
- **Articles** — items fetched from feeds, de-duplicated per feed by `ExternalId` (RSS `<guid>` else link; unique `(FeedId, ExternalId)`).
- **Conventions:** all new entities use **GUID v7** values as their IDs (`EntityBase`, `ValueGeneratedNever`). Each entity carries its column limits as `public const int *MaxLength` members — the single source for EF configuration, validators and forms. **Every persisted `DateTimeOffset` is UTC** (SQLite stores them as text and orders them as text, so offsets must be uniform).

## Notifications Architecture

- Define an `INotificationService` interface with two implementations, invoked individually or together based on the user's choice:
  - `EmailNotificationService` — uses **MailKit**. _(built)_
  - `SlackNotificationService` — uses the **Slack.Webhooks** NuGet package. _(C2)_
- Polling hands the channels plain **`NewArticle`** values (article id, feed id + name, title, link, summary, image, date), never tracked entities; digests group by feed id because feed names are not unique.
- **Email** works out of the box using the user's registered (verified) address. Alternate/extra email addresses are out of scope, since delivery requires a clicked verification link.
- **Slack** uses a single webhook URL per subscription. On enable, show an input prefilled with the user's previous webhook (`LastOrDefault`). Validate that the URL is a genuine Slack hook — it must begin with `https://hooks.slack.com/services` — otherwise reject it. The webhook targets its own default channel, so no channel is specified.
- Registration/account emails are sent via an `EmailSender` built on MailKit. **The sender owns HTML encoding**: Identity pages hand it the raw callback URL (pre-encoding it double-encodes the link and breaks confirmation).
- Background-generated emails have no request context: links back to the site need a configured public base URL (a Phase-2 prerequisite for the unsubscribe link).

## Email Templates

- Must render correctly in Word-based Outlook clients: use **table-based** layouts (not div-based), and **duplicate buttons** for Outlook, gated so duplicates appear **only** in Outlook.
- `appsettings.Development.json` is pre-populated with default **Papercut** SMTP settings for localhost.

## Feed Polling

- A **background task** polls on an interval (default: every minute) that is configurable via appsettings (`Polling:IntervalSeconds`, 1–86 400). A polling pass never lets an exception escape (that would stop the host); one broken feed never costs the others their poll.
- The RSS service tracks every feed that has **at least one** subscriber. Each poll iterates the active feeds and fetches them using **ETag** and **Last-Modified** request headers, honouring **304 Not Modified** to avoid re-fetching unchanged content _(conditional requests arrive with C4; today every poll is a full GET)_.
- A poll produces a dictionary keyed by the **user's email address** → the list of new articles, which the background service dispatches to the notification services per the user's channel choice.
- A feed's **first** poll — or a **catch-up** poll after it went unpolled for longer than `Polling:CatchUpAfterMinutes` — stores everything but reports only the newest `Polling:FirstPollNotificationCap` articles.
- Feed content is untrusted: the parser ignores DTDs (no XXE), accepts only absolute http(s) links, looks at a bounded prefix of each item's markup and strips it in a single linear pass; everything rendered from a feed is HTML-encoded.

## Unsubscribe

- The `EmailNotificationService` implements **HMAC-signed, token-based** one-click unsubscribe conforming to **RFC 8058**, reachable via an Unsubscribe link to a simple page with a single unsubscribe button. _(C5)_
- On the site, users can unsubscribe from just one notification channel (email or Slack) or both. _(C1/C5; the skeleton removes the whole subscription)_

## UI Behaviour (see spec for exact limits)

- **Public main view** — feeds shown by category as **cards** (with the feed's image when available): max **5** per category (single row on desktop, wrap on mobile), a **more** button adds 5. A category header **order** control sorts by popularity (default) or name, ascending/descending. A top-level **category filter** shows only that category, capped at **30** with a more button adding 30. _(paging/order/filter shipped in B1–B3)_
- **Feed page** — article cards (image, first paragraphs, title, subject to availability) plus a subscribe/unsubscribe button. _(shipped in B4)_
- **Subscribe flow** — an unauthenticated subscribe attempt redirects to login (offering registration), preserving the origin location (a query parameter is fine) so the user returns after auth — on the register path the `ReturnUrl` rides along in the confirmation email and the confirm page's **Continue** button. An already-subscribed feed shows **unsubscribe** in red. Clicking subscribe opens a dialog with email and Slack switches. _(dialog arrives with C1)_
- **Admin views** — list signed-up users and their feeds; CRUD categories; CRUD feeds; remove individual user subscriptions or delete user registrations entirely. _(full category/feed CRUD and user management shipped in A1–A3)_

## Conventions established in code

- **Persistence access:** `AddDbContextFactory<ApplicationDbContext>`; application services create a **short-lived context per operation** (Interactive Server circuits and the polling loop outlive requests). Identity and the seeders use the scoped context the same registration provides.
- **Services** live in `Services/<Area>/` as `I<Name>Service.cs` + `<Name>Service.cs`, with `<Area>Errors.cs` (static `Error` factories) and `Create<X>Request.cs` (record + FluentValidation validator, discovered by `AddValidatorsFromAssemblyContaining<Program>()`). They return **`ErrorOr<T>`**: validation failures → `Error.Validation(property, message)`, uniqueness → `Error.Conflict`, missing rows → `Error.NotFound`; concurrent unique-index races are re-checked and reported as the same conflict.
- **Time** comes from an injected `TimeProvider` wherever behaviour depends on it (polling, the loop), so tests use `FakeTimeProvider`.
- **UI** pages are thin (`@rendermode InteractiveServer`, no business logic), render `ErrorOr` errors through `Components/Shared/ErrorList`, and read the user id from the **authenticated principal**, never from client input. Repeated buttons carry `aria-label`s naming their target. **Keep `[PersistentState]` payloads small** (ids, flags): persisted state is sent back to the server inside the circuit-start message, which SignalR caps at 32 KB — exceeding it kills the circuit and silently leaves the page static. Re-query bulky data on the interactive render instead; `PublicPagesTests` guards the home page's payload.
- **Interactivity is only proven in a real browser.** Prerendered HTML looks identical whether or not the circuit comes up, and neither the host tests nor bUnit run `blazor.web.js`; after UI changes, load the page in a browser and check the console for circuit errors.
- **Untrusted input** (feed content, Identity callback values) is HTML-encoded at the point of rendering; the parser and validators let only absolute http(s) URLs through.

## Testing

- All implemented features and code changes must come with a comprehensive set of unit tests, using the **XUnit** framework (project `LuminaFeed.Tests`).
- Idioms: real **in-memory SQLite** over a kept-open connection (`SqliteTestDatabase`, an `IDbContextFactory`); the **real host** through `TestAppFactory` (`[Collection("Host")]`, isolated temp database under `bin/`, no network, recorded mail, the polling loop **not** hosted unless a test opts in) with the **real login/register forms** (`TestSignIn`); **bUnit** component tests (`BunitContext`, real services on the test database; register services first, then `SetRendererInfo` for pages that declare `@rendermode InteractiveServer`); `FakeTimeProvider` for anything timed; hand-rolled doubles in `TestDoubles.cs` / `PollingTestDoubles.cs`. No test waits on wall-clock time or reaches a publisher or an SMTP server.
- Every step: `dotnet build` with 0 warnings, `dotnet test` green, the feature doc updated, one commit per step.

## Build Configuration

- The project targets **.NET 10** (`global.json` pins the SDK; `rollForward: latestFeature`).
- **C# 14** language version with implicit usings and nullable reference types.
- Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` for `dotnet` invocations.

## Key Technologies & Patterns

### Backend Stack
- **.NET 10** with C# 14 and nullable reference types enabled
- **Blazor** web app on ASP.NET Core (Interactive Server), with **ASP.NET Core Identity** for authentication
- **Entity Framework Core** with **SQLite** for persistence
- **ErrorOr** for functional error handling
- **FluentValidation** for request validation
- **Ardalis.SmartEnum** for type-safe enumerations
- **MailKit** (email) and **Slack.Webhooks** (Slack) for delivery
- Tests: **xUnit**, **bUnit**, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`
