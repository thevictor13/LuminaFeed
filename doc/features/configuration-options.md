# Feature: Configuration & Options (G0.5)

Strongly-typed, startup-validated configuration for SMTP, polling, the unsubscribe secret, and the bootstrap admin.

## Option types (`LuminaFeed/Options/`)

- **`SmtpOptions`** (`Smtp`) — `Host` (required), `Port` (1–65535, default 25), `FromAddress` (required, email),
  `FromName`, `UseStartTls`, optional `UserName` / `Password`.
- **`PollingOptions`** (`Polling`) — `IntervalSeconds` (≥1, default 60) with an `Interval` `TimeSpan` accessor.
- **`UnsubscribeOptions`** (`Unsubscribe`) — `HmacSecret` (required, min length 16) for RFC 8058 unsubscribe tokens (C5).
- **`AdminSeedOptions`** (`AdminSeed`) — optional `Email` / `Password` with an `IsConfigured` computed flag; see
  [Seed Data](./seed-data.md).

## Wiring (`Program.cs`)

`SmtpOptions`, `PollingOptions`, and `UnsubscribeOptions` are each bound and
`.ValidateDataAnnotations().ValidateOnStart()` — invalid config **fails fast at startup**. `AdminSeedOptions` is bound
**without** validation on purpose: empty means "no admin seeded".

## Config files

- **`appsettings.json`** (base, committed) populates the harmless SMTP defaults (`FromAddress`, `FromName`, `Port`) but
  leaves **`Smtp:Host`**, **`Unsubscribe:HmacSecret`**, and the `AdminSeed:*` values **empty**. Because `Smtp:Host` and
  `Unsubscribe:HmacSecret` are required, this base config is **intentionally non-bootable on its own**: a start with only
  this file fails `ValidateOnStart` until they are supplied. **Production must provide `Smtp:Host` and
  `Unsubscribe:HmacSecret`** (and, if an admin is wanted, `AdminSeed:*`) via environment variables or user-secrets. A
  `UserSecretsId` exists in the csproj for this.
- **`appsettings.Development.json`** (committed) supplies local dev values: Papercut SMTP (`localhost:25`, no TLS/auth),
  a throwaway dev `Unsubscribe:HmacSecret`, and a local dev admin (`admin@luminafeed.local`). These are **dev-only,
  clearly labelled placeholder values** and must never be loaded in a non-Development environment.

## Tests

- `LuminaFeed.Tests/OptionsTests.cs` — binding, the `PollingOptions.Interval` computation, and DataAnnotations failures
  (required SMTP fields, minimum-length unsubscribe secret) through an options pipeline mirroring `Program.cs`.
- `LuminaFeed.Tests/HostBootTests.cs` — boots the real `Program` DI graph against an isolated temp SQLite database:
  a valid-config boot resolves the graph and runs seeding; a missing `Unsubscribe:HmacSecret` fails `ValidateOnStart`.
