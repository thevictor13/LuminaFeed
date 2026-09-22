# Feature: Subscriptions (S3 + C1)

A signed-in user manages, per feed, how they are alerted when it publishes something new: **email** and/or **Slack**,
chosen in a dialog. **C1** turns the walking-skeleton one-click email subscribe (S3) into the spec's full flow —
a dialog with two switches, a validated/prefilled Slack webhook, and **per-channel unsubscribe**.

> **Slack delivery is C2.** C1 **captures and validates** the Slack channel preference (`SlackEnabled` +
> `SlackWebhookUrl`) and persists it; the `SlackNotificationService` that actually posts to the webhook arrives with
> C2. Until then a Slack-only subscription (email off) simply receives nothing yet — email keeps working, and polling
> is unchanged (it still keys its result to email-enabled subscribers — see [Feed Polling](./feed-polling.md)).

## Behaviour (public list, `/`)

Each feed card carries a `SubscribeButton` (`Components/Shared/`):

| Visitor | Button |
|---|---|
| Anonymous (treated as having no subscriptions) | **Subscribe** — a link to `Account/Login?ReturnUrl=<current page>`; the login page's *Register* link forwards the same `ReturnUrl`, so the visitor lands back here after signing in or registering (on the register path the `ReturnUrl` also rides the confirmation email and the confirm page's **Continue** button; only a local path is honoured). |
| Signed in, not subscribed | **Subscribe** (primary) — opens the channel dialog. |
| Signed in, subscribed | **Unsubscribe** in **red** — opens the *same* dialog, reflecting the current channels. |

Both signed-in buttons open one shared dialog (a "unified dialog": the red state manages channels rather than
deleting silently). The user id always comes from the **authenticated principal** (`ClaimTypes.NameIdentifier`),
never from client input; the account's verified email address (from the principal) is shown in the dialog as the
fixed email target. The card button's red/primary state flips from the dialog's result via an `OnChanged` callback.

## The dialog (`Components/Shared/SubscribeDialog.razor`)

A **Bootstrap-styled modal driven entirely by Blazor** — rendered with `@if (Visible)` (`.modal.show` +
`.modal-backdrop`), **no Bootstrap JS bundle and no JS interop**, so it works in the circuit and under bUnit alike
(bUnit does not run `blazor.web.js`). On open it loads the current state via `GetSubscriptionForEditAsync`.

- **Email switch** — a Bootstrap `form-switch`; defaults **on** for a new subscription. Email always targets the
  account's registered, verified address (`RequireConfirmedAccount`); there is nowhere to enter another one.
- **Slack switch** — when on, a **webhook URL input** appears, **pre-populated** with the user's previous webhook
  (`LastOrDefault` — this subscription's own value, else the user's most recently created webhook anywhere).
- **Save** — validated server-side (see below); on success the dialog closes and the card reflects the new state.
- **Per-channel unsubscribe** — turn one switch **off** and Save (at least one channel must stay on).
- **Unsubscribe** (red, only for an existing subscription) — removes the whole subscription (both channels).
- Errors surface in the dialog's `ErrorList` (`.alert-danger`); Save/Unsubscribe are disabled while a call is in flight.

## Validation (`Services/Subscriptions/SaveSubscriptionRequest.cs`)

`SaveSubscriptionRequest(EmailEnabled, SlackEnabled, SlackWebhookUrl?)` + a FluentValidation validator
(auto-registered by `AddValidatorsFromAssemblyContaining<Program>()`), run **inside the service** and surfaced through
the same `ErrorList` pattern as the admin forms:

- **At least one channel** must be enabled (else *"Enable at least one notification channel, or unsubscribe."*).
- When **Slack is on**, the webhook is **required**. This enforces the `SlackEnabled ⇒ SlackWebhookUrl` invariant the
  `Subscription` entity documents but does not enforce.
- **Whenever a webhook is supplied — Slack on *or* off** — it must be `≤ Subscription.SlackWebhookUrlMaxLength` (2048)
  and a genuine Slack incoming webhook: an absolute **https** URL beginning with `Subscription.SlackWebhookUrlPrefix`
  (`https://hooks.slack.com/services`), the single source for that check. Validating the format independently of the
  switch means a stale or malformed value can never be persisted — so it can never pollute the `LastOrDefault` prefill.
  (The dialog also drops the webhook from the request when Slack is off, so the stored value is retained rather than
  overwritten; the service normalizes blank input to null before validating, so an empty field is simply "no webhook".)

## Service (`Services/Subscriptions/ISubscriptionService` → `SubscriptionService`)

- `GetSubscribedFeedIdsAsync(userId)` — the user's feed ids (for the card red/primary state); empty for a blank/unknown user.
- `GetSubscriptionForEditAsync(userId, feedId)` — the dialog's opening state: whether a subscription exists, the
  current switches (defaults email-on/Slack-off when new), and the webhook to prefill. The `LastOrDefault` webhook is
  chosen **client-side** (a user has few subscriptions, and SQLite cannot `ORDER BY` a `DateTimeOffset`).
- `SaveSubscriptionAsync(userId, feedId, request)` — a **validated upsert**: creates the `(UserId, FeedId)` row or
  updates its channels. When Slack is turned off and no webhook is supplied, the stored webhook is **preserved** (the
  invariant is one-directional), so the `LastOrDefault` prefill survives. A concurrent duplicate insert that trips the
  unique index is recovered by re-loading the winning row and applying the chosen channels.
- `SubscribeByEmailAsync(userId, feedId)` — idempotent email-only convenience (still used to arrange email
  subscriptions in tests and by the walking-skeleton path).
- `UnsubscribeAsync(userId, feedId)` — removes the row entirely (the dialog's red Unsubscribe).

| Situation | Error |
|---|---|
| Blank user id | `Validation` — `Subscription.UserRequired` |
| No channel / bad or missing Slack webhook | `Validation` — coded by the request property (`EmailEnabled` / `SlackWebhookUrl`) |
| Unknown feed | `NotFound` — `Feed.NotFound` |
| Unknown user | `NotFound` — `Subscription.UserNotFound` |
| Unsubscribing when not subscribed | `NotFound` — `Subscription.NotSubscribed` (the dialog treats it as already done) |

`Home.razor` runs with `@rendermode InteractiveServer`; the user's subscribed ids ride the prerender→circuit hand-off
in a small `[PersistentState]` `HashSet<Guid>` (the catalogue is re-queried, never persisted — SignalR caps the
circuit-start message at 32 KB; `PublicPagesTests` keeps the payload small). Every card button carries an accessible
name (`aria-label="Subscribe to <feed>"` / `"Unsubscribe from <feed>"`); the dialog's own Unsubscribe uses its visible
text within the feed-titled dialog, so the two never collide.

## Tests

- `SubscriptionServiceTests` — the email-only path (S3) plus C1: `SaveSubscriptionAsync` create/update, per-channel
  off keeps the row, webhook retention when Slack is switched off, and every validation branch (no channel, missing/
  non-Slack/over-length webhook); `GetSubscriptionForEditAsync` defaults, stored state, `LastOrDefault` prefill, and
  not-found/anonymous. Validators are exercised **through** the service's `ErrorOr` result.
- `SubscribeDialogComponentTests` (bUnit, real services on in-memory SQLite) — the interactive dialog: switches, the
  revealed/prefilled webhook, a bad webhook rejected in the dialog, a valid one saved, per-channel unsubscribe, the
  red Unsubscribe removal, the in-flight disable (a gated double) and a failing service's error.
- `HomePageComponentTests` (bUnit) — the card→dialog integration: the anonymous login link; Subscribe opens the
  dialog; saving flips the card to red Unsubscribe and persists; the dialog's Unsubscribe reverts it; Cancel changes nothing.
- `PublicPagesTests` — anonymous: all cards offer *Subscribe* as a login link with `ReturnUrl=%2F` and no red button;
  signed in with one subscription (arranged via `SubscribeByEmailAsync`): exactly one red *Unsubscribe*.
- `HostBootTests` — `ISubscriptionService` resolves from the real DI graph.
