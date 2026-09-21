# LuminaFeed — Master Plan (High-Level Roadmap)

> This is a **roadmap / ordered TODO**, not an implementation plan. Each item marked 🗂 gets
> its own detailed plan in `doc/plans/` before it is built.

## Context

LuminaFeed is greenfield on top of the stock **.NET 10 Blazor Web App (Individual Accounts)**
template (Interactive Server render mode). Identity is fully scaffolded (login/register/manage/
2FA/passkeys), with one Identity migration, a live `app.db`, and Bootstrap. **Everything domain-
specific is unbuilt.** This plan orders the groundwork, then builds a thin end-to-end **walking
skeleton**, then broadens in parallel tracks. Source of truth for behaviour:
`doc/LuminaFeed Initial Specification.md`.

## Current state (already done by the template)

- ASP.NET Core Identity + EF Core (SQLite) + Blazor Server scaffold, working auth.
- Email is a **no-op sender**; `RequireConfirmedAccount = true` (so confirmation can't truly complete yet).
- No domain entities, services, background tasks, config sections, or test project.

## Conventions carried throughout (from CLAUDE.md)

- GUID **v7** IDs on all new entities · **ErrorOr** for errors · **FluentValidation** for validation.
- Each feature ships with **xUnit** tests, is documented in `doc/features/`, and gets a detailed
  plan in `doc/plans/` first.
- Structure: **single project, feature/layer folders** (`Domain/`, `Services/`, `Data/`, `Components/{Pages,Admin,Layout,Account}`).

---

## Phase 0 — Groundwork (prerequisites)

**The RSS feed research (G0.R) is the very first step and gates the rest** — the real categories,
the seeded feeds, and the fixed popularity figures all come out of it, and much of the schema and
seed data hinges on its results. `G0.3` is the long pole among the code tasks. Parallelism noted per item.

- [ ] 🗂 **G0.R — RSS feed research (FIRST; spike, non-code).** Research available public RSS feeds and produce: a **curated seed list of feeds**, the **category set those feeds imply** (this replaces the spec's illustrative World News / Markets / Weather / Breaking News list), and the **fixed initial popularity figures** per feed. Deliverable is a data list (checked into `doc/`), consumed by G0.7. **Handed to a separate agent; the rest of Phase 0 continues once it's done.**
- [x] **G0.1 — Packages & test project.** Add ErrorOr, FluentValidation, Ardalis.SmartEnum (and MailKit, Slack.Webhooks). Add an **xUnit** test project to the solution. _(foundational; blocks all code)_
- [x] **G0.2 — Folder skeleton.** Create `Domain/`, `Services/`, `Components/Admin/`. _(trivial; with G0.1)_
- [ ] 🗂 **G0.3 — Domain model + persistence.** Extend `ApplicationUser` (`IsAdmin`); add `Category`, `Feed` (incl. a **fixed `Popularity`** figure + an **image URL**), `Subscription` (per-channel prefs + Slack webhook), `Article` (incl. image URL); GUID-v7 ID convention; DbContext `DbSet`s + EF configs; **initial domain migration**. Schema only — seed data lands in G0.7. _(informed by G0.R; core prerequisite for nearly everything)_
- [ ] **G0.4 — Admin authorization.** Turn `IsAdmin` into an `"Admin"` authorization policy; guard an admin area + nav section. _(needs G0.3)_
- [x] **G0.5 — Config & options.** appsettings sections + typed options: Development **Papercut** SMTP (localhost), polling interval, HMAC unsubscribe secret. _(with G0.3; parallel)_
- [ ] **G0.6 — Real email infra.** Replace `IdentityNoOpEmailSender` with a **MailKit** `IEmailSender<ApplicationUser>`; establish the **table-based, Outlook-safe** email layout foundation. Unblocks the real confirmation flow. _(needs G0.5; parallel with G0.3/G0.4)_
- [ ] **G0.7 — Seed data.** Load the researched **categories + feeds + fixed popularity figures** (from G0.R) into the DB seeder. _(needs G0.3 + G0.R)_

**Groundwork parallelization:** `G0.R` first and gating. Then `G0.1 → {G0.2, G0.3, G0.5}`; `G0.4` after G0.3, `G0.6` after G0.5, and `G0.7` once G0.3 is done (G0.R already complete).

---

## Phase 1 — Walking skeleton (Slice 1)

One deliberately thin path end-to-end to de-risk integration. Everything here is minimal
(happy-path only); polish comes in Phase 2. Introduces the `INotificationService` abstraction.

- [ ] **S1 — Admin: create category + feed.** Minimal create/list only. _(needs G0.3, G0.4)_
- [ ] **S2 — Public: minimal feed list.** Show the feed as a card. No ordering/filter/pagination. _(needs S1)_
- [ ] **S3 — Subscribe (email only).** Signed-in user persists an email subscription; skip dialog/Slack/redirect polish. _(needs G0.3, S2)_
- [ ] **S4 — Polling (minimal).** Background service polls the subscribed feed on interval, fetches items, persists `Article`s. _(needs G0.3, G0.5, S3)_
- [ ] **S5 — Email notification (minimal).** `EmailNotificationService` (behind `INotificationService`) sends a plain new-article email to the subscriber. _(needs G0.6, S4)_

**Definition of done (skeleton):** admin adds a feed → it appears publicly → a signed-in user
subscribes → polling fetches new articles → the user receives an email. Running product, end-to-end.

---

## Phase 2 — Broaden (parallel tracks)

After the skeleton, three tracks run largely in parallel. Ordering is intra-track; cross-track
dependencies are called out.

### Track A — Admin
- [ ] 🗂 **A1 — Category CRUD** (edit/delete + validation). _(needs S1)_
- [ ] 🗂 **A2 — Feed CRUD** (edit/delete, image, category dropdown). _(needs S1)_
- [ ] 🗂 **A3 — User management** (list users + their feeds; remove individual subscriptions; delete user registrations). _(needs subscriptions — S3 / C1)_

### Track B — Public UX
- [ ] 🗂 **B1 — Feed list by category** (cards + image, 5/category, "more" +5, single-row desktop / wrap mobile). _(needs S2, A2)_
- [ ] 🗂 **B2 — Ordering control** (popularity default, name; asc/desc) in category header. Popularity = the fixed `Feed.Popularity` figure seeded in G0.7. _(needs B1)_
- [ ] 🗂 **B3 — Category filter** (single category, cap 30, "more" +30). _(needs B1)_
- [ ] 🗂 **B4 — Feed detail page** (article cards: image, first paragraphs, title, subject to availability; subscribe/unsubscribe button). _(needs G0.3; content from S4)_

### Track C — Subscriptions & notifications
- [ ] 🗂 **C1 — Full subscribe flow** (dialog with email + Slack switches; unauthenticated → login/register preserving return location via query param; unsubscribe-in-red state). _(needs S3, auth)_
- [ ] 🗂 **C2 — Slack notifications** (`SlackNotificationService` via Slack.Webhooks; validate URL starts `https://hooks.slack.com/services`; LastOrDefault prefill; default channel). _(needs abstraction; parallel with email)_
- [ ] 🗂 **C3 — Email notifications hardening** (article templates; Outlook table-based layout + Outlook-only duplicated buttons). _(needs S5, G0.6)_
- [ ] 🗂 **C4 — Polling hardening** (track feeds with ≥1 subscriber; ETag/Last-Modified + 304; **resolve images** — use the RSS-provided image, else scrape `og:image` from the article target; produce email→articles dictionary; dispatch to email/Slack per user choice). _(needs S4, C2, C3)_
- [ ] 🗂 **C5 — Unsubscribe** (RFC 8058 HMAC-signed one-click; unsubscribe page, single button; per-channel or both; Unsubscribe link + List-Unsubscribe headers). _(needs C3)_

**Cross-track dependencies:** `B1` needs `A2` · `A3` needs subscriptions (`C1`) · `C5` needs `C3` · `C4` needs `C2`+`C3`.

---

## Decided

- **Popularity** — a **fixed** figure per feed (not computed), stored on `Feed`, set from the G0.R research. Default ordering sorts by it.
- **Categories** — derived from the G0.R feed research, **not** the spec's illustrative examples.
- **Images** — use the **RSS-provided image** if present; otherwise the **`og:image`** scraped from the article target. **No admin upload.** Store the image URL (not a blob).

## Open decisions (resolve during detailed planning)

- **"More"/pagination mechanics** — investigate the options; **constraint: avoid a full page reload** (lean on InteractiveServer partial loading / enhanced navigation). Resolved in the B1 detailed plan.
- **Article identity/de-dup** for polling (guid/link/hash).
- **HMAC key management/rotation** for unsubscribe tokens.
- **Confirmation UX** now that a real email sender exists.

## Deferred / out of scope

- Alternate or multiple email addresses (verification-bound). _(spec)_
- Multiple Slack channels (single webhook for v1). _(spec)_
- Categories as free-text (superseded by the category entity). _(spec)_
- **Admin image upload** — not supported at the moment. _(this session)_

## Next steps

1. Save this roadmap to `doc/plans/00-master-plan.md` and commit it.
2. **Kick off G0.R (RSS feed research) first** — handed to a separate agent; it gates the rest of Phase 0.
3. Once the research lands, continue Phase 0 (`G0.1` onward) and produce per-feature detailed plans (🗂 items) in `doc/plans/` as each is reached.
