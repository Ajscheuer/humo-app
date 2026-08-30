# Humo

BBQ cook-tracking app for iOS and Android. Offline-first, bilingual
(English + Spanish), with cross-cook analytics and a fire-management model that
learns your rig's burn cadence.

## Documentation

Read the relevant document before implementing a feature:

| Document | Covers |
|---|---|
| [`docs/product-spec.md`](docs/product-spec.md) | What Humo is, personas, flows, tiers, localization, Spanish glossary |
| [`docs/architecture.md`](docs/architecture.md) | Client and API structure, identity, sync shape, testing strategy |
| [`docs/data-model.md`](docs/data-model.md) | Entities, enums, units, sync semantics |
| [`docs/fire-model.md`](docs/fire-model.md) | The three-level fire predictor and its notification loop |
| [`docs/testing.md`](docs/testing.md) | Where tests live, and the edge cases that recur in this domain |

[`CLAUDE.md`](CLAUDE.md) holds the always-true conventions. Each doc ends with
open questions — decisions that are deliberately unresolved, not oversights.

## Layout

```
src/Humo.Shared    DTOs, enums, contracts, unit conversion  (references nothing)
src/Humo.Core      ViewModels, services, repositories, fire predictor  (net9.0, no MAUI)
src/Humo.App       Views, platform services, DI wiring  (MAUI, iOS + Android)
src/Humo.Api       ASP.NET Core Minimal API
tests/…            One test project per source project except Humo.App,
                   plus Humo.Conventions.Tests, which enforces CLAUDE.md
```

`Humo.Core` targets plain `net9.0` and must never reference `Microsoft.Maui.*`.
That is what lets every ViewModel and service be unit-tested with `dotnet test`
on any machine, with no workload and no device. Platform capabilities go behind
interfaces declared in `Humo.Core` and implemented in `Humo.App`.

## Prerequisites

- .NET SDK 9.0
- For the app: `dotnet workload install maui` (Android builds anywhere; **iOS
  requires a Mac**)

## Build and test

```bash
dotnet test                      # everything, needs the MAUI workload
dotnet test Humo.NoMaui.slnf     # the same tests, no workload required
dotnet build src/Humo.App -f net9.0-android
dotnet run --project src/Humo.Api
```

`Humo.NoMaui.slnf` is a solution filter that excludes `Humo.App`. It exists so
CI and development machines without the MAUI workload can still run the full
test suite.

## Localization

All user-facing strings live in
`src/Humo.Core/Resources/Strings/AppResources.resx` and `AppResources.es.resx`.
Adding an English string without its Spanish counterpart **fails the test suite**
(`ResourceParityTests`) rather than shipping English text inside a Spanish UI.

XAML uses `{loc:Translate Key}`; ViewModels take an `ILocalizer` dependency and
reference keys through the `AppStrings` constants.
