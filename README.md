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

- **.NET SDK 10.0** — any feature band. `global.json` pins the floor at
  `10.0.100` with `rollForward: latestFeature`, so whichever 10.0.x SDK you have
  installed is used. Pin a *specific* build there only with a reason: naming the
  exact SDK from one machine locks every other machine out, even ones with a
  perfectly good newer .NET 10.
- For the app:
  - **macOS:** `sudo dotnet workload install maui` — `sudo` because .NET usually
    lives in `/usr/local/share/dotnet`, which is not user-writable.
  - **Linux:** `dotnet workload install maui-android` (there is no iOS workload
    for Linux, and none is needed — see the target framework note below).
  - Both need a JDK and the Android SDK. If you don't have Android Studio,
    `dotnet build src/Humo.App -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=True`
    fetches the SDK.

  **On macOS you need the Android workload even to build iOS.** NuGet restore
  evaluates every target framework the project lists, not just the one passed to
  `-f`, so `dotnet build -f net10.0-ios` fails with `NETSDK1147: maui-android`
  until the full `maui` workload is installed. Installing `maui` rather than
  `maui-ios` covers it.

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

### If the build misbehaves

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
- **`A valid Xcode installation was not found at '/Library/Developer/CommandLineTools'`**
  — `xcode-select` is pointing at the Command Line Tools, which have compilers
  but none of the iOS platform tooling. Point it at a full Xcode:

  ```bash
  sudo xcode-select -s /Applications/Xcode.app
  sudo xcodebuild -runFirstLaunch
  ```

  **.NET 10's iOS workload needs a recent Xcode**, but check what you have
  before chasing a version theory: Xcode 26.0–26.2 build this project fine on
  CI. An earlier note here claimed a hard 26.6 floor; that was wrong, and came
  from a stub Xcode install that reported a version it could not build with.
- **`xcrun: unable to find utility "actool"` / `"ibtool"`** — the selected Xcode
  is missing its macOS platform SDK or its component tools. Same family as the
  problem above: the Xcode is incomplete or is really the Command Line Tools.
  `sudo xcodebuild -runFirstLaunch` installs the missing components.

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

### Automated PR review

`.github/workflows/claude-review.yml` reviews every PR against `CLAUDE.md` and
`/docs`, then approves, requests changes, or comments. It requires an
**`ANTHROPIC_API_KEY` repository secret**; without it the job fails and the
review simply does not happen — CI is unaffected.

The reviewer is a fresh Claude that sees only the diff. That independence is the
point: a model reviewing work it just wrote re-checks its own assumptions with
the same blind spots that produced them, so a self-review is close to worthless
as a quality gate.

Treat its approval as one signal, not a merge authorization. It reviews a diff;
it does not run the app.

## Localization

All user-facing strings live in
`src/Humo.Core/Resources/Strings/AppResources.resx` and `AppResources.es.resx`.
Adding an English string without its Spanish counterpart **fails the test suite**
(`ResourceParityTests`) rather than shipping English text inside a Spanish UI.

XAML uses `{loc:Translate Key}`; ViewModels take an `ILocalizer` dependency and
reference keys through the `AppStrings` constants.
