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
(`ClaimTypes.NameIdentifier`) — never from client input. The prerendered catalogue and the user's subscribed ids are
carried into the circuit with `[PersistentState]`, so the cards don't flash back to "Loading…" and nothing is queried
twice. Service errors surface in the shared `ErrorList` alert.

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
- `PublicPagesTests` — anonymous: all cards offer *Subscribe* as a login link with `ReturnUrl=%2F` and no
  *Unsubscribe*; signed in (through the real login form) with one subscription: exactly one red *Unsubscribe*, the
  rest *Subscribe* buttons, no login links, and the persisted-state payload is present.
- `RegistrationFlowTests` — the register path end to end through the real host: the real Register form posts, the
  recorded confirmation email carries a working link (encoded once in the HTML part, raw in the text part), opening
  it confirms the account and offers *Continue* → `Account/Login?ReturnUrl=%2F`, and the confirmed account can sign
  in. An off-site `ReturnUrl` is ignored; a mangled `code` yields the error message, not a 500.
- `HostBootTests` — `ISubscriptionService` resolves from the real DI graph.
