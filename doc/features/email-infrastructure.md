# Feature: Email Infrastructure (G0.6)

Real email delivery via **MailKit**, replacing the template's no-op sender, with an Outlook-safe HTML layout. This
covers Identity account emails (confirmation, password reset). New-article digests reuse the same transport and
layout through `EmailNotificationService` — see [Notifications](./notifications.md); richer article templates come
with C3.

## Components (`LuminaFeed/Services/Email/`)

- **`IMailSender` / `MailKitMailSender`** — the low-level SMTP transport. `SendAsync(EmailMessage, CancellationToken)`
  builds a `MimeMessage` from `SmtpOptions` and sends it over a **fresh `SmtpClient` per send** (SmtpClient is not safe
  to reuse). Chooses STARTTLS vs. plain from `SmtpOptions.UseStartTls`; authenticates only when a `UserName` is set.
  On any transport failure it **logs an error with recipient/subject context and rethrows**, so callers (e.g. the
  Identity account flows) still see the failure.
- **`EmailMessage`** — immutable record: `ToEmail`, `ToName?`, `Subject`, `HtmlBody`, `TextBody?`.
- **`EmailLayout`** — static, table-based HTML layout. Word-based Outlook can't render div layouts reliably, so the
  shell is **tables + inline styles** (no `<div>`). `Button(text, url)` emits a **"bulletproof" duplicated button**: an
  MSO-conditional VML `v:roundrect` for Outlook alongside a normal `<a>` for every other client — the two are mutually
  exclusive via `[if mso]` / `[if !mso]` comments, so the Outlook duplicate never shows elsewhere. Text nodes and URLs
  are **HTML-encoded** (`HtmlEncoder`) so untrusted values (future article titles/links) can't inject markup;
  `bodyHtml` is treated as trusted HTML that callers must pass already-safe.
- **`IdentityEmailSender`** — implements `IEmailSender<ApplicationUser>`; renders confirmation / password-reset links
  (and reset codes) through `EmailLayout` and delegates to `IMailSender`. Every email is sent **multipart**: the HTML
  body plus a **plain-text alternative** (better deliverability + text-only clients). Any value it interpolates directly
  into the HTML (the fallback link anchor, the reset code) is **HTML-encoded** with `HtmlEncoder`, mirroring
  `EmailLayout`, so those inputs can't inject markup. **The sender owns that encoding**: the Identity pages
  (`Register`, `ResendEmailConfirmation`, `ForgotPassword`, `ExternalLogin`, `Manage/Email`) hand it the **raw**
  callback URL. The stock template pre-encoded the URL (its no-op sender interpolated it unencoded), which combined
  with this sender produced double-encoded links (`&amp;amp;code=`) that `ConfirmEmail` could never read — so no
  self-registered account could be confirmed. Fixed in P1.R; `RegistrationFlowTests` guards the whole path.

## Wiring (`Program.cs`)

`IMailSender → MailKitMailSender` and `IEmailSender<ApplicationUser> → IdentityEmailSender` are registered as
**singletons** (both stateless; the SmtpClient is created per send). SMTP settings come from `SmtpOptions` — see
[Configuration & Options](./configuration-options.md). In Development this targets **Papercut** on `localhost:25`.

## Tests (`LuminaFeed.Tests/EmailTests.cs`)

`EmailLayout` produces a table-based (not div-based) shell with the VML namespace, emits both button variants, and
HTML-encodes heading/text/URL; `MailKitMailSender.BuildMessage` sets From/To/Subject/HtmlBody, and `SendAsync` logs +
rethrows on transport failure; `IdentityEmailSender` builds a table-based confirmation email containing the link,
attaches a non-empty plain-text alternative (link/code), HTML-encodes a hostile interpolated link, and encodes a
multi-parameter link **exactly once** (raw in the text part). `RegistrationFlowTests` opens the emailed link against
the real host.
