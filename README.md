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
src/Humo.Core      ViewModels, services, repositories, fire predictor  (net10.0, no MAUI)
src/Humo.App       Views, platform services, DI wiring  (MAUI, iOS + Android)
src/Humo.Api       ASP.NET Core Minimal API  (Azure App Service + Azure SQL)
tests/…            One test project per source project except Humo.App,
                   plus Humo.Conventions.Tests, which enforces CLAUDE.md
```

`Humo.Core` targets plain `net10.0` and must never reference `Microsoft.Maui.*`.
That is what lets every ViewModel and service be unit-tested with `dotnet test`
on any machine, with no workload and no device. Platform capabilities go behind
interfaces declared in `Humo.Core` and implemented in `Humo.App`.

## Prerequisites

- .NET SDK 10.0 (pinned in `global.json`)
- For the app: `dotnet workload install maui-android` (or `maui` on macOS), plus
  a JDK and the Android SDK. `dotnet build src/Humo.App -t:InstallAndroidDependencies
  -p:AcceptAndroidSDKLicenses=True` installs the Android SDK if you don't have
  Android Studio.

`Humo.App` targets `net10.0-android` everywhere, and adds `net10.0-ios`
**everywhere except Linux**. NuGet restore evaluates every target framework
listed, so an unconditional iOS target breaks restore on Linux even when building
Android — this is the same condition the .NET 10 MAUI template uses. Building and
running the iOS target requires a Mac.

## Build and test

```bash
dotnet test                      # everything, needs the MAUI workload
dotnet test Humo.NoMaui.slnf     # the same tests, no workload required
dotnet build src/Humo.App -f net10.0-android
dotnet run --project src/Humo.Api
```

`Humo.NoMaui.slnf` is a solution filter that excludes `Humo.App`. It exists so
CI and development machines without the MAUI workload can still run the full
test suite.

### If the Android build misbehaves

- **`type or namespace 'Microsoft.Maui' not found`** — the `Microsoft.Maui.Controls`
  package reference is missing. `UseMaui=true` does *not* add it implicitly, and
  under central package management a missing `PackageVersion` makes the reference
  vanish silently rather than erroring.
- **Every package suddenly has "no inclusive lower bound" (NU1604)** — MSBuild
  failed to load `Directory.Packages.props` and central package management is
  off. An XML comment containing `--` will do this without an obvious error.
- **`CommonUtilities.Helpers.UserName must have a valid value`** — the Android SDK
  installer needs a username; export `USER` before running it. Affects bare
  containers, not normal dev machines.
- **`XA5207: Could not find android.jar for API level 36`** — run the
  `InstallAndroidDependencies` command above; .NET 10 targets a newer Android API
  level than .NET 9 did.
- **`NU1903: known high severity vulnerability`** — .NET 10 reports NuGet audit
  findings, and `TreatWarningsAsErrors` turns them into build failures. That is
  working as intended: pin the offending package forward in
  `Directory.Packages.props` rather than suppressing it.

## Continuous integration

`.github/workflows/ci.yml` runs on every PR to `main`, on pushes to `main`, and
on demand:

| Job | Runner | Proves |
|---|---|---|
| Tests | Linux | The full test suite, no MAUI workload needed |
| Build app (Android) | Linux | `Humo.App` compiles for `net10.0-android` |
| Build app (iOS) | macOS | `Humo.App` compiles for `net10.0-ios`, simulator target |

Android builds on Linux rather than macOS on purpose: it builds identically on
both, and on a private repository macOS minutes bill at 10x. The iOS job is the
only one that genuinely needs a Mac, since Apple ships the iOS SDK for macOS
only.

The iOS job targets the simulator, so it needs no signing identity or
provisioning profile. It still compiles `Platforms/iOS` and processes
`Info.plist`, which is the part worth checking automatically.

## Localization

All user-facing strings live in
`src/Humo.Core/Resources/Strings/AppResources.resx` and `AppResources.es.resx`.
Adding an English string without its Spanish counterpart **fails the test suite**
(`ResourceParityTests`) rather than shipping English text inside a Spanish UI.

XAML uses `{loc:Translate Key}`; ViewModels take an `ILocalizer` dependency and
reference keys through the `AppStrings` constants.
