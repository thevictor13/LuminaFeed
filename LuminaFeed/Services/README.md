# Services

Application and infrastructure services, registered in DI from `Program.cs`. Interfaces live here
alongside their implementations. Application services return `ErrorOr<T>`, validate requests with
FluentValidation, and open a short-lived `ApplicationDbContext` per operation via `IDbContextFactory`.

**Implemented:**
- `Email/` — MailKit email transport (`IMailSender` / `MailKitMailSender`), the Identity email sender
  (`IdentityEmailSender`), and the Outlook-safe HTML layout (`EmailLayout`). _(Phase 0)_
- `Categories/`, `Feeds/` — the curated catalogue: admin create + list, and the public grouped list
  (`ICategoryService`, `IFeedService`). _(S1, S2)_
- `Subscriptions/` — email-only subscribe / unsubscribe per user and feed (`ISubscriptionService`). _(S3)_

**Planned (not yet built):** RSS polling/fetching and the polling background service, and
the notification abstraction (`INotificationService`) with its `EmailNotificationService` /
`SlackNotificationService` implementations.
