# Humo — Conventions

BBQ cook-tracking app. Cross-platform mobile, offline-first, bilingual.

## Stack

- **App:** .NET MAUI, `net9.0` (iOS + Android)
- **API:** ASP.NET Core Minimal API on Azure App Service
- **Database:** Azure SQL serverless (server), SQLite (device)
- Projects: `Humo.App`, `Humo.Api`, `Humo.Shared`, plus test projects.

## Architecture

- **MVVM. No logic in code-behind** — code-behind is the constructor and
  `InitializeComponent()` only. Use behaviors, converters, or commands instead.
- ViewModels depend on service *interfaces*, never on MAUI or platform types.
- Shared DTOs, enums, and contracts live in `Humo.Shared` and are referenced by
  both app and API so the wire contract cannot drift.

## Offline-first

- **SQLite on device is the source of truth during a cook.** Never block a user
  action on the network.
- All record IDs are **client-generated GUIDs** — a record has its final identity
  before it ever reaches the server.
- Sync is **append-only**, **idempotent**, and resolves mutable records by
  **last-write-wins** on `updatedAt`.
- All timestamps are stored UTC.
- The API derives the account ID from the auth token, **never** from the request
  body.

## Units

- **Temperatures are stored in Celsius, everywhere** — device, API, database,
  cached analytics. Convert to °F only at display, in a converter.
- **°C/°F is a user setting, independent of language.** An American cook with a
  Spanish phone still wants °F. Same for kg/lb.
- Unit conversion lives in one place in `Humo.Shared`, with tests.

## Localization (English + Spanish, both are launch languages)

- **All user-facing strings go through `.resx` resources.** Never hardcode a
  string in XAML or in a ViewModel.
- **Every new English string added to `AppResources.resx` must be added to
  `AppResources.es.resx` in the same commit.** No exceptions, no "I'll translate
  it later".
- Enums are stored as values and displayed via resource lookup — never store a
  display string.
- Layouts must tolerate ~25% text expansion for Spanish. No fixed-width
  containers around labels.
- Dates and numbers follow culture; temperature unit does not.

## Before you call something done

- **Run `dotnet test`.** All tests pass, or it isn't done.
- **Read the relevant `/docs` file before implementing a feature** —
  `product-spec.md`, `architecture.md`, `data-model.md`, `fire-model.md`.
- If the docs are ambiguous or contradict the code, raise it as an open question
  rather than silently picking an answer.
