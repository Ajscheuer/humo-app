# Humo — Architecture

**Status:** Draft v0.1 · **Last updated:** 2026-08-30

This document describes how Humo is built. Product behaviour lives in
`product-spec.md`; entities and sync semantics in `data-model.md`; the fire
predictor in `fire-model.md`.

---

## 1. System shape

```
┌─────────────────────────────────────────┐
│  Humo.App  (.NET MAUI, net9.0)          │
│  ┌───────────┐  ┌──────────┐            │
│  │ Views     │◄─┤ ViewModels│           │   iOS + Android
│  │ (XAML)    │  └─────┬─────┘           │
│  └───────────┘        │                 │
│                 ┌─────▼──────┐          │
│                 │  Services  │          │
│                 └─────┬──────┘          │
│              ┌────────┴────────┐        │
│         ┌────▼────┐      ┌─────▼─────┐  │
│         │ SQLite  │      │ Sync      │  │
│         │ (truth) │      │ client    │  │
│         └─────────┘      └─────┬─────┘  │
└────────────────────────────────┼────────┘
                                 │ HTTPS, JWT
                    ┌────────────▼────────────┐
                    │ Humo.Api                │
                    │ ASP.NET Core Minimal API│  Azure App Service
                    │  ├ sync endpoints       │
                    │  ├ analytics compute    │
                    │  ├ entitlement checks   │
                    │  └ AI proxy (later)     │
                    └──┬──────────┬────────┬──┘
                       │          │        │
              ┌────────▼──┐  ┌────▼─────┐ ┌▼──────────────┐
              │ Azure SQL │  │RevenueCat│ │ AWS Bedrock   │
              │ serverless│  │   API    │ │ (later phase) │
              └───────────┘  └──────────┘ └───────────────┘
```

`Humo.Shared` — DTOs, enums, and validation shared by app and API — is
referenced by both sides so the wire contract cannot drift.

## 2. Client architecture (Humo.App)

### 2.1 MVVM, strictly

- **Views are XAML only.** Code-behind contains the constructor and
  `InitializeComponent()`. Nothing else — no event handlers with logic, no
  navigation calls, no data access. If something seems to need code-behind, it
  needs a behavior, a converter, or a command.
- **ViewModels** hold state and commands, depend on service interfaces, and know
  nothing about `Page`, `View`, or any platform type.
- **Services** own persistence, sync, localization, notifications, and settings.
  Every service is registered against an interface in the DI container so
  ViewModels are unit-testable with no MAUI runtime.
- **Models** in `Humo.Shared` are plain records/classes with no framework
  attributes beyond what serialization needs.

Navigation goes through an `INavigationService` abstraction over Shell, so
ViewModels can request navigation without referencing MAUI types.

### 2.2 Localization

- All user-facing strings resolve from `AppResources.resx` /
  `AppResources.es.resx` — **never** literals in XAML or ViewModels.
- XAML binds through a markup extension (`{loc:Translate KeyName}`) so a runtime
  language change re-resolves without a page rebuild.
- ViewModels take an `ILocalizer` dependency rather than touching
  `AppResources` statically; this keeps them testable and lets tests assert
  which key was used rather than which English text.
- Culture resolution order: **in-app override (preferences) → device culture →
  English fallback.** Setting the override updates
  `CultureInfo.CurrentUICulture` and raises a change so bound strings refresh.
- Temperature unit is read from a separate preference and applied by a display
  converter, never by the storage layer.

### 2.3 Local persistence

SQLite on device is the **source of truth during a cook**. The app never blocks
a user action on the network. Reads and writes go through repository interfaces;
the sync client is the only component that talks to the API.

### 2.4 Charts

LiveCharts2 renders the per-cook temperature series (meat and pit) with fuel
events and milestones as annotations. Chart configuration lives in a ViewModel-
exposed series model, not in code-behind.

### 2.5 Notifications

Fire-check alerts are **local notifications scheduled on device** — they must
fire with no connectivity, since a smoker is often outside WiFi range. Quick
responses (Added log / Still fine / Snooze) are notification actions registered
at startup; handling a response writes to SQLite and reschedules the next check
without requiring the app to be foregrounded.

## 3. Backend architecture (Humo.Api)

ASP.NET Core **Minimal API** on **Azure App Service**, backed by **Azure SQL
serverless**. Endpoints are grouped by feature (`/sync`, `/analytics`,
`/entitlements`, later `/ai`), each in its own endpoint-registration file rather
than one large `Program.cs`.

Responsibilities:

- **Sync** — accept batches of client-generated records, apply the merge rules
  in `data-model.md`, return records the client has not seen.
- **Analytics** — recompute a cook's cached metrics when its data changes at
  sync time, and recompute the user's baselines for the affected (meat type,
  equipment) pair. Results are stored, not computed per request.
- **Entitlements** — verify Pro status against RevenueCat server-side before
  serving any Pro-gated response.
- **AI proxy (later)** — the app never holds Bedrock credentials. It calls our
  API, our API calls Bedrock with server-held credentials and returns the
  result.

## 4. Identity and authentication

At first launch the user is prompted to sign in or create an account:

- **Sign in with Apple**
- **Sign in with Google**
- **Email + password**
- **Continue without an account** — a client-generated anonymous account ID
  stored on device, with an upgrade path that claims the local data into a real
  account later.

Every record in the system is scoped to an **account ID**. Anonymous accounts
use the same shape, so the upgrade is a re-association rather than a migration
of schema.

The API authenticates requests with a bearer JWT and derives the account ID from
the token — **never from the request body**, so a client cannot write into
another account's data by changing a field.

Proposed identity provider: **Microsoft Entra External ID** (formerly Azure AD
B2C), which federates Apple and Google and supports local email accounts, and
fits the Azure hosting choice. The alternative — ASP.NET Core Identity in our
own API with Apple/Google federation hand-rolled — trades a managed dependency
for full control and more code to own. Flagged as an open question; the app-side
abstraction (`IAuthService` returning a token and an account ID) is the same
either way.

## 5. Offline-first sync

The rules in full are in `data-model.md`; the architectural shape:

- Every record carries a **client-generated GUID** as its primary key, so a
  record created offline has its final identity from birth and no server round
  trip ever renumbers it.
- Records carry **timestamps** (`createdAt`, `updatedAt`) set by the client.
- Sync is **append-only with last-write-wins** on mutable fields.
- Sync is **incremental**, driven by a per-device cursor of what the server has
  already sent.
- Sync is **idempotent** — replaying a batch must not duplicate or corrupt.
- Sync is opportunistic and never blocks the UI: it runs on connectivity
  regained, on app foreground, and after a cook is finished.

**Clock trust.** Last-write-wins on client timestamps means a device with a
badly wrong clock can win conflicts it should lose, or lose ones it should win.
The server records its own receipt time alongside the client timestamp so
pathological skew is detectable after the fact. See open questions.

## 6. Entitlements

RevenueCat is the source of truth for subscription state.

- The client uses the RevenueCat SDK for purchase and restore flows, and to
  display current entitlement.
- The **API independently verifies entitlement server-side** for every Pro-gated
  operation, by RevenueCat app user ID mapped to our account ID. A modified
  client cannot unlock server-computed analytics or AI access.
- The client caches the last known entitlement state for offline UI decisions.
  This cache is a UX affordance only, never a security boundary.

## 7. AI features (later phase)

- The app holds **no AI provider credentials**, ever.
- Requests go app → `Humo.Api` → **AWS Bedrock**, with credentials held in Azure
  Key Vault and injected into App Service configuration.
- The API enforces Pro entitlement, rate limits per account, and controls what
  cook data is included in a prompt.
- Cross-cloud (Azure-hosted API calling AWS Bedrock) is a deliberate, accepted
  choice; it adds egress cost and a second cloud credential to manage, noted as
  an open question rather than reopened here.

## 8. Configuration and secrets

- No secrets in the mobile app bundle. Anything embedded in a mobile binary is
  public.
- API configuration comes from App Service settings; secrets from Key Vault via
  managed identity.
- Local development uses .NET user-secrets, never committed files.

## 9. Testing strategy

- **Humo.Shared.Tests** — validation, conversions (notably °C/°F round-tripping),
  and pure model logic.
- **Humo.App.Tests** — ViewModel unit tests against mocked service interfaces.
  This is what "no logic in code-behind" buys us: the interesting client
  behaviour is testable without a device or emulator.
- **Humo.Api.Tests** — endpoint tests via `WebApplicationFactory`, including
  sync merge semantics (out-of-order arrival, replay, conflicting updates) and
  analytics computation against fixture cooks.
- Localization has a **test that asserts key parity** between `AppResources.resx`
  and `AppResources.es.resx`, so an English string added without its Spanish
  counterpart fails the build rather than shipping as an English string inside a
  Spanish UI.

`dotnet test` must pass before any work is called done.

## 10. Repository layout (proposed)

```
/docs
/src
  Humo.App/            MAUI, net9.0-ios;net9.0-android
  Humo.Api/            ASP.NET Core Minimal API
  Humo.Shared/         DTOs, enums, contracts, conversions
/tests
  Humo.App.Tests/
  Humo.Api.Tests/
  Humo.Shared.Tests/
Humo.sln
CLAUDE.md
```

---

## Open questions

1. **Identity provider not finally chosen.** Entra External ID vs. ASP.NET Core
   Identity in our own API. Entra is less code but adds a managed dependency,
   a per-MAU cost above the free tier, and its own learning curve for a solo
   developer. Needs a decision before the auth slice.
2. **Azure SQL serverless auto-pause vs. sync latency.** A paused database takes
   seconds to resume. A user finishing a cook and syncing hits that cold start.
   Do we accept it (sync is background, so probably yes), disable auto-pause
   (cost), or keep a warming ping (cost, and inelegant)?
3. **Analytics recomputation cost at sync.** Recomputing a user's baseline on
   every sync is wasteful when a sync carries one temp entry. Should
   recomputation be debounced, queued, or triggered only on cook completion?
   Related: does this need a background worker, or can it stay inline in the
   request?
4. **Clock skew policy.** We record server receipt time, but we have not decided
   what to *do* about a client whose timestamps are implausible. Reject? Clamp?
   Accept and flag? This matters because a wrong clock corrupts the fire model's
   learned intervals, not just sync ordering.
5. **Cross-cloud AI (Azure API → AWS Bedrock).** Confirmed as intentional, but
   the egress cost, latency, and second credential surface should be revisited
   before the AI phase rather than at it.
6. **Notification quick-response reliability.** iOS caps pending local
   notifications (64) and both platforms restrict background work. Handling
   "Added log" without launching the app needs platform-specific verification —
   this should be spiked before the fire model slice, not assumed.
7. **LiveCharts2 on MAUI net9.0** — version, licensing terms, and iOS/Android
   rendering behaviour need verification before the charts slice. Charts should
   sit behind a thin abstraction so the library is replaceable if it disappoints.
8. **Nothing here specifies a migrations strategy.** Azure SQL will need schema
   migrations (EF Core migrations or SQL scripts) and the device SQLite database
   needs its own versioned migration path. The device side is the harder one — a
   user can skip many app versions. Needs deciding before the first schema
   change after launch.
9. **Deletion and account closure are unspecified.** Append-only sync has no
   delete story, but the app stores account-scoped personal data and both app
   stores require account deletion. See `data-model.md` open questions.
