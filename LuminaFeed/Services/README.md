# Services

Application and infrastructure services, registered in DI from `Program.cs`. Interfaces live here
alongside their implementations.

**Implemented (Phase 0):**
- `Email/` — MailKit email transport (`IMailSender` / `MailKitMailSender`), the Identity email sender
  (`IdentityEmailSender`), and the Outlook-safe HTML layout (`EmailLayout`).

**Planned (Phase 1–2, not yet built):** RSS polling/fetching and the polling background service, and
the notification abstraction (`INotificationService`) with its `EmailNotificationService` /
`SlackNotificationService` implementations.
