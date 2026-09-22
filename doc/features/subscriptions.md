# Feature: Subscriptions (S3 — email only)

A signed-in user can subscribe to a feed and receive its alerts **by email**. This is the walking-skeleton slice; the
full flow — a dialog with email + Slack switches, Slack webhook validation/prefill, and per-channel unsubscribe —
arrives with **C1 / C2 / C5**.

## Behaviour (public list, `/`)

Each feed card carries a `SubscribeButton` (`Components/Shared/`):

| Visitor | Button |
|---|---|
| Anonymous (treated as having no subscriptions) | **Subscribe** — a link to `Account/Login?ReturnUrl=<current page>`; the login page's *Register* link forwards the same `ReturnUrl`, so the visitor lands back here after signing in or registering. On the register path the `ReturnUrl` rides along in the confirmation email's link, and the `ConfirmEmail` page's **Continue** button leads to login with the same `ReturnUrl` (only a local path is honoured; anything off-site falls back to a plain login link). |
| Signed in, not subscribed | **Subscribe** (primary) — creates the subscription with `EmailEnabled = true`. |
| Signed in, subscribed | **Unsubscribe** in **red** — removes the subscription. |

The email address is always the account's registered, verified address (`RequireConfirmedAccount`); there is nowhere
to enter another one.

`Home.razor` runs with `@rendermode InteractiveServer`, so a click updates just that button (it is disabled while its
call is in flight, and further clicks are ignored meanwhile). The user id is read from the **authenticated principal**
(`ClaimTypes.NameIdentifier`) — never from client input. The user's subscribed ids are carried from the prerender into
the circuit with `[PersistentState]` (so the buttons don't flip while the circuit starts); the catalogue itself is
**re-queried** on the interactive render — persisted state travels back to the server inside the circuit-start
message, which SignalR caps at 32 KB, and persisting 115 feed summaries (~87 KB) killed every circuit at start, leaving
the page static (found in manual testing after P1.R; `PublicPagesTests` now keeps the payload under 8 KB). Service
errors surface in the shared `ErrorList` alert at the top of the page (a card far down the list may
have to scroll up to see it — revisited with the card paging of B1). Every button carries an accessible name
(`aria-label="Subscribe to <feed>"` / `"Unsubscribe from <feed>"`), since a page full of otherwise identical buttons
is unusable with a screen reader.

> The minimal **Unsubscribe** is included in the skeleton on purpose: once polling and notifications are on, a test
> subscriber would otherwise be emailed forever. It deletes the whole row; C5 refines this into per-channel control.

## Service (`Services/Subscriptions/ISubscriptionService` → `SubscriptionService`)

- `GetSubscribedFeedIdsAsync(userId)` — the user's feed ids; empty for a blank/unknown user.
- `SubscribeByEmailAsync(userId, feedId)` — **idempotent**. Creates the `(UserId, FeedId)` row with
  `EmailEnabled = true`; if the row exists it only (re-)enables email and leaves any Slack settings untouched. A
  concurrent duplicate insert that trips the unique index is treated as success (the desired state already holds).
- `UnsubscribeAsync(userId, feedId)` — deletes that user's row for that feed (`ExecuteDelete`).

| Situation | Error |
|---|---|
| Blank user id | `Validation` — `Subscription.UserRequired` |
| Unknown feed | `NotFound` — `Feed.NotFound` |
| Unknown user | `NotFound` — `Subscription.UserNotFound` |
| Unsubscribing when not subscribed | `NotFound` — `Subscription.NotSubscribed` (the page treats it as already done) |

## Tests

- `SubscriptionServiceTests` — persisted email-only row (GUID v7), idempotent double subscribe, re-enabling email
  keeps Slack settings, unknown feed/user, blank user, unsubscribe touches only that user + feed, not-subscribed,
  re-subscribe after unsubscribe, per-user isolation of the id list.
- `PublicPagesTests` — anonymous: all cards offer *Subscribe* as a login link with `ReturnUrl=%2F` and no red
  button; signed in (through the real login form) with one subscription: exactly one red *Unsubscribe*, the rest
  *Subscribe* buttons, no login links, and the persisted-state payload is present.
- `HomePageComponentTests` (bUnit, real services on in-memory SQLite) — the **interactive path** the page tests can't
  reach: the anonymous login link; a click on *Subscribe* turns the button into a red *Unsubscribe* and persists the
  row; a click on *Unsubscribe* reverts it and deletes the row; the button is disabled while the call is in flight
  (a gated service double); a failing service shows its message in the alert and leaves the button unchanged.
- `RegistrationFlowTests` — the register path end to end through the real host: the real Register form posts, the
  recorded confirmation email carries a working link (encoded once in the HTML part, raw in the text part), opening
  it confirms the account and offers *Continue* → `Account/Login?ReturnUrl=%2F`, and the confirmed account can sign
  in. An off-site `ReturnUrl` is ignored; a mangled `code` yields the error message, not a 500.
- `HostBootTests` — `ISubscriptionService` resolves from the real DI graph.
