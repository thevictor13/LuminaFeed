# C1 — Full Subscribe Flow — LuminaFeed

> **INVIOLABLE SESSION RULE (verbatim):**
>
> You must NEVER, EVER change anything beyond the contents of your working directory. Exceptions: the Claude memory, scratchpad and plans folders; %TEMP%; the current repo's .git for the current branch only (commit, no push, no stash pop); the NuGet global packages folder and NuGet caches (restore/add packages only); the ASP.NET Data Protection key ring and .NET SDK telemetry folders under the user profile; outbound HTTPS to nuget.org and to RSS publishers; and SMTP to localhost:25.
> You may however read files that were gitignored in your working directory, which you will find in d:\Work\Sonrisa\LuminaFeed\. This is a rule you must not breach for the entirety of this session

Consequences for this plan: builds/tests write only `bin/`/`obj/` here (plus the temp SQLite file `TestAppFactory` already creates under `bin/`); commits stay on the current `phase-2c` branch (no push); **no new NuGet package is required** (`Slack.Webhooks` is already referenced from G0.1 and is not used until C2); **no DB migration** (the `SlackEnabled` / `SlackWebhookUrl` columns already exist from G0.3). A browser verification uses the scratchpad PuppeteerSharp probe pattern (no browser packages added to the project).

---

## Context

S3 (walking skeleton) shipped a deliberately minimal, **email-only, one-click** subscribe: a single `SubscribeButton` toggles a whole-row subscription with `EmailEnabled = true`, and the red "Unsubscribe" deletes the row. C1 replaces that stub with the **full flow the spec describes** (`doc/LuminaFeed Initial Specification.md` §"Subscribe / Unsubscribe Flow"):

- A signed-in user clicks Subscribe → a **dialog** appears with **two switches** (Email, Slack).
- **Email** is fixed to the account's registered, verified address (no alternate address).
- **Slack** reveals a **webhook URL input** when switched on, **pre-populated** with the user's previous webhook (`LastOrDefault`); the URL is **validated** — it must begin with `https://hooks.slack.com/services`, else rejected.
- **Per-channel unsubscribe** — the user can turn off just email, just Slack, or both.

**Already delivered by S3 + P1.R (not re-done here):** the anonymous → login/register flow that preserves the return location (`ReturnUrl` through login, registration, the confirmation email and the confirm page's Continue button), the red Unsubscribe state, accessible per-feed button names, and the small `[PersistentState]` payload (32 KB circuit-start cap).

**Explicitly out of scope (later tracks):** actual Slack **sending** (`SlackNotificationService`) is **C2**; richer article/Outlook email templates are C3; RFC 8058 one-click email unsubscribe + `List-Unsubscribe` headers are C5. C1 only **captures and validates** the channel preferences and provides the on-site per-channel management.

The intended outcome: a signed-in user can choose email and/or Slack (with a validated, prefilled webhook) for any feed through a dialog, change those channels later, unsubscribe from one channel or the whole feed — all persisted correctly and covered by tests.

## Decisions confirmed with the user

1. **Unified dialog.** Both **Subscribe** and the red **Unsubscribe** open the *same* channel dialog. A new subscription opens with Email on / Slack off; an existing one reflects its current channels. **Save requires at least one channel**; turning one switch off and saving is a **per-channel unsubscribe**; an explicit **red "Unsubscribe" button inside the dialog** removes the subscription entirely. One reusable component (also serves the B4 feed page later).
2. **Slack switch is live in C1.** The dialog renders the Slack switch + validated webhook input and **persists** `SlackEnabled` / `SlackWebhookUrl` now. Until C2 wires `SlackNotificationService`, a Slack-only subscription simply receives nothing yet (email keeps working) — documented as a known interim gap.

## Design decisions (made during planning)

3. **Dialog technology = Blazor-controlled, Bootstrap-styled modal rendered with `@if`.** No Bootstrap JS bundle is loaded and there is no reusable dialog component (only the framework `ReconnectModal`). A `@if (Visible)` `.modal.show` (with `style="display:block"`) + `.modal-backdrop` needs **no JS interop** and is **fully testable in bUnit** (which does not run `blazor.web.js`). This matches how the app already renders state (`ErrorList`, alerts).
4. **Validation = server-side FluentValidation → `ErrorList`** (the established convention; the app has no `Blazored.FluentValidation`/`FluentValidationValidator` and none is added). Failure messages surface in the dialog's `.alert-danger` exactly as the admin forms do.
5. **Webhook retention & prefill.** The invariant is one-directional (`SlackEnabled ⇒ SlackWebhookUrl`), so disabling Slack does **not** null the stored webhook. Prefill = the row's own webhook, else the **most recently created** subscription of that user that has a non-empty webhook (`LastOrDefault`).
6. **Polling/notifications unchanged.** The poll already selects feeds with ≥1 subscription and keys its result to **email-enabled** subscribers only; C1 touches neither. A subscription with email off gets no email; Slack dispatch is C2.

## Codebase facts this plan relies on (verified)

- `Domain/Subscription.cs` — `EmailEnabled`, `SlackEnabled`, `SlackWebhookUrl?`, `const int SlackWebhookUrlMaxLength = 2048`, `const int UserIdMaxLength = 450`, `CreatedAt` (private-set, `UtcNow`). Comment already earmarks the length const for "(C1) the subscribe-dialog validator". EF config + migration already map these columns.
- Request+validator idiom: `Services/Feeds/CreateFeedRequest.cs` (record + `AbstractValidator<T>` in one file; `Cascade(CascadeMode.Stop)`, `.Must(...)`, `.When(...)`), bridged by `Services/ValidationResultExtensions.cs` (`ToErrors()` → `Error.Validation(propertyName, message)`), auto-registered by `AddValidatorsFromAssemblyContaining<Program>()` in `Program.cs`. Services call `validator.ValidateAsync(...)` after trimming/null-if-blank normalization.
- `Services/Subscriptions/SubscriptionService.cs` — `IDbContextFactory` per-op contexts; idempotent insert with `DbUpdateException` re-check against the unique `(UserId, FeedId)` index; `SubscriptionErrors` are `UserRequired` (Validation), `UserNotFound`/`NotSubscribed` (NotFound). `FeedErrors.NotFound(feedId)` reused.
- UI: `Components/Shared/SubscribeButton.razor` is presentational; `Components/Pages/Home.razor` owns the logic, reads the user id from `Task<AuthenticationState>` → `ClaimTypes.NameIdentifier`, guards in-flight with `busyFeedId`, renders errors via `Components/Shared/ErrorList.razor`, persists only `SubscribedIds`. `FeedCard.razor` exposes an `Actions` render fragment. `_Imports.razor` already imports `Microsoft.AspNetCore.Components.Forms` and `Components.Shared`.
- Tests: `HomePageComponentTests` / `AdminFormsComponentTests` (bUnit `BunitContext`, `SetRendererInfo(new RendererInfo("Server", isInteractive:true))` last, `AddAuthorization().SetClaims(new Claim(ClaimTypes.NameIdentifier, id))`, `.Change(...)`, `SubmitAsync()`, `WaitForElement`/`WaitForAssertion`, `.alert-danger`/`.alert-success`, nested `GatedSubscriptionService`/`FailingSubscriptionService` doubles). `SqliteTestDatabase` (is the `IDbContextFactory`; `AddUser/AddFeed/AddCategory`). `TestAppFactory` + `TestSignIn` + `[Collection(HostCollection.Name)]` for real-host HTML. Validators are exercised through the service's `ErrorOr` (no `FluentValidation.TestHelper` in the repo).

---

## Steps

Each step builds with **0 warnings** and leaves **all tests green**. Committed on `phase-2c`. C1 is a single master-plan item; commits: **C1.0 plan**, then **C1.1 service layer**, **C1.2 UI**, **C1.3 tests + docs** (each intermediate commit still builds/tests clean).

### C1.0 — Save the plan
This file, committed under `doc/plans/`.

### C1.1 — Service layer (request + validator + upsert + edit-state)

**New — `Services/Subscriptions/SaveSubscriptionRequest.cs`** (record + validator in one file, mirroring `CreateFeedRequest.cs`):
```csharp
public sealed record SaveSubscriptionRequest(bool EmailEnabled, bool SlackEnabled, string? SlackWebhookUrl = null);

public sealed class SaveSubscriptionRequestValidator : AbstractValidator<SaveSubscriptionRequest>
{
    public SaveSubscriptionRequestValidator()
    {
        RuleFor(r => r.EmailEnabled)
            .Must((r, _) => r.EmailEnabled || r.SlackEnabled)
            .WithMessage("Enable at least one notification channel, or unsubscribe.");

        RuleFor(r => r.SlackWebhookUrl).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A Slack webhook URL is required when Slack notifications are on.")
            .MaximumLength(Subscription.SlackWebhookUrlMaxLength)
            .Must(BeSlackWebhook)
            .WithMessage($"The webhook must be a Slack incoming webhook (it must start with {Subscription.SlackWebhookUrlPrefix}).")
            .When(r => r.SlackEnabled);
    }

    internal static bool BeSlackWebhook(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && url!.StartsWith(Subscription.SlackWebhookUrlPrefix, StringComparison.Ordinal);
}
```

**Edit — `Domain/Subscription.cs`:** add the single-source prefix constant next to the length const:
```csharp
public const string SlackWebhookUrlPrefix = "https://hooks.slack.com/services";
```

**New — a small DTO** (in `ISubscriptionService.cs` or its own file):
```csharp
public sealed record SubscriptionState(bool Exists, bool EmailEnabled, bool SlackEnabled, string? WebhookPrefill);
```

**Edit — `Services/Subscriptions/ISubscriptionService.cs`:** drop the "email only" wording; add:
```csharp
Task<ErrorOr<SubscriptionState>> GetSubscriptionForEditAsync(string? userId, Guid feedId, CancellationToken ct = default);
Task<ErrorOr<Success>> SaveSubscriptionAsync(string? userId, Guid feedId, SaveSubscriptionRequest request, CancellationToken ct = default);
```
Keep `GetSubscribedFeedIdsAsync`, `SubscribeByEmailAsync` (still used to arrange email subs in host/notification tests) and `UnsubscribeAsync` (full removal — reused by the dialog's red button).

**Edit — `Services/Subscriptions/SubscriptionService.cs`:** add `IValidator<SaveSubscriptionRequest> validator` to the primary constructor (auto-resolved from DI — no `Program.cs` change).
- `GetSubscriptionForEditAsync` — blank user → `UserRequired`; unknown feed → `FeedErrors.NotFound`; unknown user → `UserNotFound`. Load the `(userId, feedId)` row; compute `WebhookPrefill = row?.SlackWebhookUrl ?? user's subs with a non-empty webhook ordered by CreatedAt desc, first`. Return `new SubscriptionState(Exists: row is not null, EmailEnabled: row?.EmailEnabled ?? true, SlackEnabled: row?.SlackEnabled ?? false, WebhookPrefill)`. (Defaults for a new subscription: Email on, Slack off.)
- `SaveSubscriptionAsync` — blank user → `UserRequired`; normalize (`SlackWebhookUrl = trimmed, null-if-blank`); `validator.ValidateAsync` → `ToErrors()` on failure; check feed + user exist; **upsert**: existing row → set `EmailEnabled`/`SlackEnabled`/`SlackWebhookUrl` (when Slack off and no webhook supplied, **preserve** the stored webhook so the prefill survives); new row → insert. Reuse the existing `DbUpdateException` unique-index recovery (on a concurrent insert, re-load the winning row via a fresh context and apply the update). Return `Result.Success`.

`SubscriptionErrors` needs no new members — channel/webhook failures flow through `ToErrors()`.

### C1.2 — UI (dialog + button + page)

**New — `Components/Shared/SubscribeDialog.razor`** (self-contained; injects `ISubscriptionService`):
- Parameters: `Guid FeedId`, `string FeedName`, `string? UserId` (sourced from the principal by the page), `string? AccountEmail` (shown as the read-only email target), `bool Visible`, `EventCallback OnClose`, `EventCallback<bool> OnChanged` (fires with the new subscribed state after Save/Unsubscribe so the page flips the button).
- On becoming visible for a feed (`OnParametersSetAsync` guarded by a "loaded feed" field), call `GetSubscriptionForEditAsync` → seed a nested `FormModel { EmailEnabled, SlackEnabled, SlackWebhookUrl }` (webhook from `WebhookPrefill`), remember `Exists`, clear `errors`.
- Markup: `@if (Visible)` → `<div class="modal fade show" style="display:block" role="dialog" aria-modal="true" aria-labelledby="sub-dialog-title">` with `<div class="modal-dialog"><div class="modal-content">`:
  - Header: title `Subscribe to @FeedName` (id `sub-dialog-title`) + a Cancel/close `×` (`OnClose`).
  - Body `<EditForm Model="form" OnSubmit="SaveAsync">`:
    - Email switch — `<div class="form-check form-switch"><InputCheckbox id="sub-email" role="switch" class="form-check-input" @bind-Value="form.EmailEnabled"/><label for="sub-email">Email — @AccountEmail</label></div>`.
    - Slack switch — same pattern, id `sub-slack`.
    - `@if (form.SlackEnabled)` → `<InputText id="sub-webhook" class="form-control" @bind-Value="form.SlackWebhookUrl" maxlength="@Subscription.SlackWebhookUrlMaxLength" placeholder="@Subscription.SlackWebhookUrlPrefix/..."/>`.
    - `<ErrorList Messages="errors"/>`.
    - Footer: **Save** (submit, `btn-primary`, `disabled="@busy"`), **Cancel** (`OnClose`), and `@if (Exists)` a red **Unsubscribe** (`btn-danger`, `aria-label="Unsubscribe from @FeedName"`, calls `UnsubscribeAsync`).
  - Plus `<div class="modal-backdrop fade show"></div>`.
- `SaveAsync` — guard `busy`/`UserId`; build `SaveSubscriptionRequest`; call `SaveSubscriptionAsync`; on error map `result.Errors.Select(e => e.Description)` into `errors`; on success `OnChanged(isSubscribed: true)` + `OnClose`. `UnsubscribeAsync` handler → on success/`NotSubscribed` `OnChanged(false)` + `OnClose`, else show error.

**Edit — `Components/Shared/SubscribeButton.razor`:** for authenticated users both the Subscribe and the red Unsubscribe `<button>` raise a single `EventCallback OnOpen` (rename from `OnToggle`) to open the dialog; anonymous branch (login link) unchanged; keep the per-feed `aria-label`s. Drop `Busy` (the dialog owns in-flight state).

**Edit — `Components/Pages/Home.razor`:** replace `ToggleSubscriptionAsync`/`busyFeedId` with `openFeed` (a `FeedSummary?`) + `dialogVisible`; read `accountEmail` from the principal's email claim in `OnInitializedAsync`. Render **one** `<SubscribeDialog>` after the category loop, bound to `openFeed` (`Visible="dialogVisible"`, `UserId`, `AccountEmail`, `OnClose`, `OnChanged`). `SubscribeButton.OnOpen="@(() => OpenDialog(feed))"`. `OnChanged(bool subscribed)` adds/removes `openFeed.Id` in `SubscribedFeedIds` (flips the red state) and hides the dialog. `SubscribedIds` `[PersistentState]` stays as-is (small).

Minimal CSS only if needed (Bootstrap's `.modal.show`/`.modal-backdrop` cover it with the inline `display:block`); add to `wwwroot/app.css` if a z-index tweak is required.

### C1.3 — Tests + docs

**`SubscriptionServiceTests`** (construct `new SubscriptionService(_db, new SaveSubscriptionRequestValidator())`; update the existing `new(_db)` sites): `SaveSubscriptionAsync` — create email-only; create email+Slack persists the webhook; update flips channels; per-channel off keeps the row (one channel remaining); **validation**: no channel → `ErrorType.Validation` with the "at least one channel" message; Slack on + empty webhook → validation error; Slack on + non-Slack URL (theory: `http://…`, `https://evil.com/…`, `not-a-url`) → validation error; webhook over `SlackWebhookUrlMaxLength` → validation error; Slack off preserves the stored webhook. `GetSubscriptionForEditAsync` — not subscribed → defaults (Email on/Slack off) + `WebhookPrefill` from another of the user's subs (LastOrDefault = most recent); subscribed → stored flags + own webhook; unknown feed/user/blank user errors. (Validators asserted **through** the `ErrorOr` result, per the repo idiom.)

**New — `SubscribeDialogComponentTests`** (bUnit, mirrors `HomePageComponentTests`/`AdminFormsComponentTests`): render `Visible` for a signed-in user; toggling the Slack switch reveals `#sub-webhook`; Save with Slack + valid webhook persists the row and fires `OnChanged(true)`; Save with Slack + bad webhook shows the message in `.alert-danger` and persists nothing; per-channel unsubscribe (email off, Slack on, Save) updates the row; the red **Unsubscribe** deletes the row and fires `OnChanged(false)`; Save disabled while in flight (extended `GatedSubscriptionService`); a `FailingSubscriptionService` surfaces its message; webhook prefill via LastOrDefault. Extend the two nested doubles to the new interface methods.

**`HomePageComponentTests`** — update for the dialog: clicking **Subscribe** shows the modal (assert `.modal` present); completing a Save flips the card button to red **Unsubscribe**; anonymous still sees the login link. Adjust doubles for the new methods; drop the old direct-toggle assertions superseded by the dialog.

**`PublicPagesTests`** — unchanged (arranges via `SubscribeByEmailAsync`; the persisted-state budget is unaffected). Confirm still green.

**Docs** — rewrite `doc/features/subscriptions.md` for the full flow (unified dialog, two switches, fixed email target, Slack webhook validation + `LastOrDefault` prefill, per-channel vs full unsubscribe, the new service surface, the "Slack captured but not sent until C2" interim note). Add a one-line note to `doc/features/notifications.md` that Slack prefs are captured in C1 and dispatched in C2. Tick **C1** in `doc/plans/00-master-plan.md` with a short outcome line.

---

## Verification

1. `DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build` — **0 warnings, 0 errors**.
2. `dotnet test` — all green, including the new service + dialog tests.
3. **Browser check (interactivity is only proven in a real browser — CLAUDE.md + memory).** Boot the app in Development from the working directory (isolate the instance per memory: override the admin email / SMTP port so a stray digest isn't mistaken for a bug), sign in, and on `/`: click **Subscribe** → dialog opens; toggle **Slack** → webhook input appears prefilled; enter a valid `https://hooks.slack.com/services/...` → **Save** → button turns red **Unsubscribe**; reopen → turn **Email** off, keep Slack, **Save** (per-channel); reopen → click the dialog's red **Unsubscribe** → row removed. Enter a non-Slack URL → the validation message shows in the dialog. Watch the console for **circuit errors** (the P1.R dead-circuit class of bug). Use the scratchpad PuppeteerSharp probe (no browser packages added to the project).
4. `git status` clean after each commit; commits on `phase-2c`: **C1.0 plan**, **C1.1 service**, **C1.2 UI**, **C1.3 tests+docs**. No push.

## Out of scope (tracked elsewhere)
- Slack **sending** (`SlackNotificationService`, webhook dispatch, poll-result value shape) — **C2**.
- Article/Outlook email template hardening + `SiteOptions.PublicBaseUrl` — **C3**.
- RFC 8058 one-click email unsubscribe + `List-Unsubscribe` headers + unsubscribe page — **C5**.
