# Feature: Notifications (S5 — email, minimal)

When a polling pass finds new articles, each affected subscriber is emailed. This introduces the
**`INotificationService`** abstraction and its first implementation; **Slack** joins with C2, richer article templates
and Outlook buttons with C3, per-user channel dispatch with C4, and the RFC 8058 unsubscribe link + headers with C5.

## Abstraction (`LuminaFeed/Services/Notifications/`)

- **`NotificationChannel`** — an `Ardalis.SmartEnum`: `Email`, `Slack`.
- **`ArticleNotification(RecipientEmail, Articles)`** — what one subscriber is told about (articles carry their `Feed`).
- **`INotificationService`** — `Channel` + `NotifyAsync(notification) : ErrorOr<Success>`. Delivery problems are
  **returned as errors, not thrown**, so one failing recipient or channel can never take the others down.

## `EmailNotificationService`

Builds **one digest email per subscriber per polling pass** and sends it through `IMailSender` (MailKit — see
[Email Infrastructure](./email-infrastructure.md)) to the subscriber's registered, verified address.

- **Subject** — `New from <feed>: <title>` for a single article; `<n> new articles from <feed>` for several from one
  feed; `<n> new articles from <k> feeds` otherwise.
- **Body** — rendered inside the table-based, Outlook-safe `EmailLayout` (no `<div>`): articles grouped under their
  feed name (feeds alphabetically, articles in the order given — newest first), each with a linked title, the
  publication time in UTC when known, and the summary shortened to 300 characters. A **plain-text alternative** with
  the same content is attached.
- **Untrusted content** — every feed-supplied value (feed name, title, summary, link) is **HTML-encoded**; links were
  already restricted to absolute http(s) URLs by the parser.
- An empty article list sends nothing. A transport failure becomes `Notification.EmailFailed` carrying the cause
  (the transport logs the details itself); caller cancellation still propagates.

## Dispatch (`FeedPollingBackgroundService`)

After each pass, the poll's dictionary (**subscriber email → new articles**, email-enabled subscriptions only — see
[Feed Polling](./feed-polling.md)) is handed to the registered notification services for the **`Email` channel**: one
`NotifyAsync` per subscriber. A returned error is logged as a warning and an unexpected exception as an error; either
way the remaining subscribers are still notified and the polling loop carries on. Registered in `Program.cs` as
`AddSingleton<INotificationService, EmailNotificationService>()`.

## End to end

`WalkingSkeletonTests` runs the Phase 1 definition of done through the **real host and DI graph** (only the HTTP
fetcher and the SMTP transport are doubles): admin adds a category + feed → it shows on `/` → a confirmed user
subscribes → a polling pass stores the feed's 8 items → the user is emailed the newest 5 → an unchanged feed sends
nothing → one newly published item sends exactly that one → after unsubscribing the feed is no longer fetched.

The same path was exercised once against live publishers from a Development boot (BBC — RSS, Deutsche Welle — RDF,
The Register — Atom): 95 articles stored, 15 notifications (3 × the cap of 5) composed into one digest, and nothing
re-announced on the next tick. With Papercut running on `localhost:25` that digest lands in its inbox.

## Known limitations (deliberate, until Phase 2)

- Articles are stored **before** notifying, so a digest that fails to send (SMTP down) is **not retried** — those
  articles are no longer "new" on the next pass. (C4)
- No unsubscribe link or `List-Unsubscribe` headers yet. (C5)
- Emails are sent one after another inside the polling pass. (C4)

## Tests

- `EmailNotificationServiceTests` — channel, recipient/subject variants, table-based body, grouping and order, HTML
  encoding of hostile feed content, text alternative, summary shortening, article without feed/summary/date, empty
  list, transport failure → error with cause, cancellation.
- `FeedPollingBackgroundServiceTests` — one notification per subscriber with their own articles, email channel only,
  a failing or throwing recipient doesn't block the others, nothing new → nobody notified, no email service → warning.
- `WalkingSkeletonTests` — the end-to-end flow above, plus the DI registration of `EmailNotificationService`.
- Host-booting tests replace `IMailSender` with a `RecordingMailSender`, so no test ever opens an SMTP connection.
