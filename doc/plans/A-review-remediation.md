# A-review-remediation — Phase 2 Track A review follow-up

A senior review of the A1/A2/A3 commits (`684293c`, `cdd2981`, `9a80369`) found the implementation solid and
convention-faithful, with **no active bugs**. This plan records the graded improvements agreed for remediation and
tracks them to completion. It is the A-track analogue of `P1.R-phase-1-review-remediation.md`.

## Findings recap
- **Verified correct / documented deferrals (not fixed):** no `IsAdmin` toggle / `UpdateSecurityStampAsync` (deferred),
  no last-admin protection (non-goal), no RowVersion concurrency token (pre-existing), cascade rules, race re-check.
- **P1:** (1) no E2E test that an authenticated non-admin is denied `/admin/*`; (2) `FeedService.UpdateAsync` left a
  feed's `ETag`/`LastModified` intact when the URL changed (latent wrong-304 once C4 lands).
- **P2:** shared `Modal` had no Escape-to-close / focus handling; untested edit-modal validation, modal-cancel,
  second-admin delete, zero-impact copy.
- **P3:** stale success banner behind modals; user-delete count from a page snapshot; self-guard bypassable on an
  empty acting id.

## Steps (one commit each)

- [x] **Step 1 — Feed-URL change resets conditional-request state (P1).** `FeedService.UpdateAsync` clears
  `ETag`/`LastModified` when `FeedUrl` changes (ordinal compare); `FeedServiceTests` covers cleared-on-change and
  kept-on-non-URL-edit; `feed-polling.md` + `admin-catalog-management.md` updated.
- [x] **Step 2 — Non-admin authorization E2E test (P1).** `AdminPagesTests.CreateConfirmedUserAsync` seeds a confirmed
  non-admin; a signed-in non-admin GET of every `/admin/*` route is a redirect to `/Account/AccessDenied` (forbidden,
  not the anonymous login challenge); `admin-authorization.md` documents both denial outcomes.
- [x] **Step 3 — Shared `Modal` keyboard/focus a11y (P2).** `@onkeydown` Escape → `OnClose`; `FocusAsync` the container
  on open; `AdminFormsComponentTests` asserts Escape closes the edit dialog without saving; `admin-catalog-management.md`
  updated. (bUnit handles `FocusAsync` natively — existing modal tests stay green.)
- [x] **Step 4 — Targeted test additions (P2).** Category edit-modal validation, delete cancel, zero-impact feed delete
  (`AdminFormsComponentTests`); second-admin delete, blank acting-id, Identity satellite-row cascade, live
  subscription-count (`UserAdminServiceTests`).
- [x] **Step 5 — Cosmetic/consistency polish (P3).** `notice` cleared in every `Begin*` handler (Categories/Feeds/Users);
  `Users.razor` reads the delete-user count fresh via new `IUserAdminService.GetSubscriptionCountAsync`;
  `DeleteUserAsync` refuses a blank acting id; `admin-user-management.md` updated.

## Outcome
All five steps applied. **313 tests green (was 298), `dotnet build` 0 warnings.** Interactive changes (Steps 3, 5)
re-verified in a real browser on an isolated instance (Escape closes a dialog, live delete count, clean console).

## Verification (every step)
`set DOTNET_CLI_TELEMETRY_OPTOUT=1`; `dotnet build LuminaFeed.slnx` (0 warnings); `dotnet test` green; interactive
changes (Steps 3, 5) re-checked in a real browser on an isolated instance (override SMTP port / admin email; no browser
packages in the app project).
