# Services

Application and infrastructure services, registered in DI from `Program.cs`. Interfaces live here
alongside their implementations. Application services return `ErrorOr<T>`, validate requests with
FluentValidation, and open a short-lived `ApplicationDbContext` per operation via `IDbContextFactory`.

**Implemented:**
- `Email/` — MailKit email transport (`IMailSender` / `MailKitMailSender`), the Identity email sender
  (`IdentityEmailSender`), and the Outlook-safe HTML layout (`EmailLayout`). _(Phase 0)_
- `Categories/`, `Feeds/` — admin catalogue management: create + list (`ICategoryService`, `IFeedService`). _(S1)_

**Planned (not yet built):** subscriptions, RSS polling/fetching and the polling background service, and
the notification abstraction (`INotificationService`) with its `EmailNotificationService` /
`SlackNotificationService` implementations.
