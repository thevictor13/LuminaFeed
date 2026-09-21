# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Documentation

- **For broad context**, always read `doc/LuminaFeed Initial Specification.md`. That spec is the source of truth for detailed behaviour; the summaries below capture the durable decisions.
- Any new features implemented must be documented in the `doc/features/` folder. These docs must constantly be kept up-to-date

## Product Overview

LuminaFeed is an online service where users register and subscribe to admin-curated, categorized RSS feeds. When feeds publish new items, subscribers receive alerts delivered as **email** and/or **Slack** notifications. Unauthenticated users are treated as having no subscriptions.

## Domain Model

- **Users** — standard vs. admin, distinguished by a simple `isAdmin` flag on the user table (no separate roles entity initially).
- **Categories** — a dedicated entity, seeded with values like World News, Markets, Weather, Breaking News. Assigned to a feed via dropdown.
- **RSS Feeds** — admin-curated, each belongs to a category and may have an image.
- **Subscriptions** — per user, per feed; each carries the chosen notification channels (email and/or Slack, with the Slack webhook URL).
- **Articles** — items fetched from feeds.
- **Convention:** all new entities use **GUID v7** values as their IDs.

## Notifications Architecture

- Define an `INotificationService` interface with two implementations, invoked individually or together based on the user's choice:
  - `EmailNotificationService` — uses **MailKit**.
  - `SlackNotificationService` — uses the **Slack.Webhooks** NuGet package.
- **Email** works out of the box using the user's registered (verified) address. Alternate/extra email addresses are out of scope, since delivery requires a clicked verification link.
- **Slack** uses a single webhook URL per subscription. On enable, show an input prefilled with the user's previous webhook (`LastOrDefault`). Validate that the URL is a genuine Slack hook — it must begin with `https://hooks.slack.com/services` — otherwise reject it. The webhook targets its own default channel, so no channel is specified.
- Registration/account emails are sent via an `EmailSender` built on MailKit.

## Email Templates

- Must render correctly in Word-based Outlook clients: use **table-based** layouts (not div-based), and **duplicate buttons** for Outlook, gated so duplicates appear **only** in Outlook.
- `appsettings.Development.json` is pre-populated with default **Papercut** SMTP settings for localhost.

## Feed Polling

- A **background task** polls on an interval (default: every minute) that is configurable via appsettings.
- The RSS service tracks every feed that has **at least one** subscriber. Each poll iterates the active feeds and fetches them using **ETag** and **Last-Modified** request headers, honouring **304 Not Modified** to avoid re-fetching unchanged content.
- A poll produces a dictionary keyed by the **user's email address** → the list of new articles, which the background service dispatches to the notification services per the user's channel choice.

## Unsubscribe

- The `EmailNotificationService` implements **HMAC-signed, token-based** one-click unsubscribe conforming to **RFC 8058**, reachable via an Unsubscribe link to a simple page with a single unsubscribe button.
- On the site, users can unsubscribe from just one notification channel (email or Slack) or both.

## UI Behaviour (see spec for exact limits)

- **Public main view** — feeds shown by category as **cards** (with the feed's image when available): max **5** per category (single row on desktop, wrap on mobile), a **more** button adds 5. A category header **order** control sorts by popularity (default) or name, ascending/descending. A top-level **category filter** shows only that category, capped at **30** with a more button adding 30.
- **Feed page** — article cards (image, first paragraphs, title, subject to availability) plus a subscribe/unsubscribe button.
- **Subscribe flow** — an unauthenticated subscribe attempt redirects to login (offering registration), preserving the origin location (a query parameter is fine) so the user returns after auth. An already-subscribed feed shows **unsubscribe** in red. Clicking subscribe opens a dialog with email and Slack switches.
- **Admin views** — list signed-up users and their feeds; CRUD categories; CRUD feeds; remove individual user subscriptions or delete user registrations entirely.

## Testing

- All implemented features and code changes must come with a comprehensive set of unit tests, using the **XUnit** framework

## Build Configuration

- The project targets **.NET 10**
- **C# 14** language version with implicit usings and nullable reference types

## Key Technologies & Patterns

### Backend Stack
- **.NET 10** with C# 14 and nullable reference types enabled
- **Blazor** web app on ASP.NET Core, with **ASP.NET Core Identity** for authentication
- **Entity Framework Core** with **SQLite** for persistence
- **ErrorOr** for functional error handling
- **FluentValidation** for request validation
- **Ardalis.SmartEnum** for type-safe enumerations
