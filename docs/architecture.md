# Humo — Architecture

**Status:** Draft v0.1 · **Last updated:** 2026-08-30

This document describes how Humo is built. Product behaviour lives in
`product-spec.md`; entities and sync semantics in `data-model.md`; the fire
predictor in `fire-model.md`; testing in `testing.md`.

---

## 1. System shape

```
┌─────────────────────────────────────────┐
│  Humo.App  (.NET MAUI, net10.0)         │
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
                    │  ├ photo authorization  │
                    │  └ AI proxy (later)     │
                    └─┬────────┬────────┬───┬─┘
                      │        │        │   │
        ┌─────────────▼┐ ┌─────▼───┐ ┌──▼───▼──────┐ ┌──────────┐
        │  Azure SQL   │ │  Blob   │ │    Entra    │ │ Azure AI │
        │  serverless  │ │ Storage │ │ External ID │ │ (later)  │
        └──────────────┘ └─────────┘ └─────────────┘ └──────────┘
                                            ▲
                                     ┌──────┴─────┐
                                     │ RevenueCat │
                                     └────────────┘
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
  English fallback.**
- Temperature unit is read from a separate preference and applied by a display
  converter, never by the storage layer.

### 2.3 Local persistence

SQLite on device is the **source of truth during a cook**. The app never blocks
a user action on the network. Reads and writes go through repository interfaces;
the sync client is the only component that talks to the API.

### 2.4 Charts

LiveCharts2 renders the per-cook temperature series (meat and pit) with fuel
events and milestones as annotations, behind a thin abstraction so the library
is replaceable.

### 2.5 Notifications

Fire-check alerts are **local notifications scheduled on device** — they must
fire with no connectivity, since a smoker is often outside WiFi range. Quick
responses (Added log / Still fine / Snooze) are notification actions registered
at startup; handling a response writes to SQLite and reschedules the next check
without requiring the app to be foregrounded.

Permission is requested **once, when the first prediction is ready**, and denial
degrades to an in-app countdown rather than removing the feature
(`product-spec.md` §4.5).

### 2.6 Photos

Photos are **captured and stored on device first**, like every other record.
They are compressed before they ever leave the phone (long edge ~2048px, JPEG),
because a modern phone photo is several megabytes and users are on cellular at a
cook site.

**Free users keep photos on device only; Pro users get them synced** to Azure
Blob Storage. The client treats photo upload as a **separate, resumable queue**
from record sync — a failed 3 MB upload must never block a 200-byte temperature
entry.

## 3. Backend architecture (Humo.Api)

ASP.NET Core **Minimal API** on **Azure App Service**, backed by **Azure SQL
serverless**, with **Azure Blob Storage** for photos. Endpoints are grouped by
feature (`/sync`, `/analytics`, `/entitlements`, `/photos`, later `/ai`), each in
its own endpoint-registration file rather than one large `Program.cs`.

Responsibilities:

- **Sync** — accept batches of client-generated records, apply the merge rules
  in `data-model.md`, return records the client has not seen.
- **Analytics** — recompute a cook's cached metrics **when the cook is finished**
  (or edited afterwards), and refresh user baselines on a **nightly schedule**.
  Results are stored, not computed per request.

  Recomputing on every sync would redo work that the next temperature entry
  immediately invalidates; a sync mid-cook carrying one entry should be cheap.
  Nothing a user looks at is ever more than one cook stale, and the work stays
  inline in the request — no queue or worker until load demands one.
- **Entitlements** — verify Pro status against RevenueCat server-side before
  serving any Pro-gated response.
- **Photos** — issue **SAS URLs** so image bytes go phone↔Blob Storage directly
  and never through the API. The API owns authorization and metadata only.
- **AI proxy (later)** — the app never holds AI provider credentials. It calls
  our API; our API calls the model provider with server-held credentials and
  returns the result.

## 4. Identity and authentication

At first launch the user is prompted to sign in or create an account:

- **Sign in with Apple**
- **Sign in with Google**
- **Email + password**
- **Continue without an account** — a client-generated anonymous account ID
  stored on device, with an upgrade path that claims the local data into a real
  account later.

Identity provider: **Microsoft Entra External ID** (formerly Azure AD B2C). It
federates Apple and Google, supports local email accounts, and brings password
reset, lockout and account recovery — all flows that would otherwise be
security-sensitive code for one developer to write and maintain. It fits the
Azure hosting and is free below 50k monthly active users; its own learning curve
is the real cost.

Every record is scoped to an **account ID**. Anonymous accounts use the same
shape, so the upgrade is a re-association rather than a schema migration.

The API authenticates with a bearer JWT and derives the account ID from the
token — **never from the request body**, so a client cannot write into another
account's data by changing a field.

The app-side abstraction is `IAuthService`, returning a token and an account ID.
Nothing above that interface knows which provider is behind it.

**Subscribing requires an account** (`product-spec.md` §5.2). Guests may log
cooks indefinitely but cannot subscribe or sync, and are asked once — defaulting
to yes — whether to merge their local cooks when they create or sign into an
account.

## 5. Offline-first sync

The rules in full are in `data-model.md`; the architectural shape:

- Every record carries a **client-generated GUID** as its primary key, so a
  record created offline has its final identity from birth and no server round
  trip ever renumbers it.
- Records carry **timestamps** (`createdAt`, `updatedAt`) set by the client.
- Sync is **append-only with last-write-wins** on mutable fields.
- Sync is **incremental**, driven by a per-device cursor.
- Sync is **idempotent** — replaying a batch must not duplicate or corrupt.
- Sync is opportunistic and never blocks the UI: it runs on connectivity
  regained, on app foreground, and after a cook is finished.
- Records are ordered **parents before children within a batch**; a genuinely
  orphaned record is rejected with a retryable error rather than held or
  silently dropped.

**Clock trust.** Last-write-wins on client timestamps means a device with a
badly wrong clock can win conflicts it should lose, or lose ones it should win.
The policy is **accept, record, flag** — never reject and never clamp:

- The client timestamp is stored as sent.
- The server stores its own **receipt time** alongside it.
- A record whose client timestamp diverges from receipt time by more than a
  threshold (proposed: 24 hours, allowing for genuinely offline cooks synced
  days later) is **flagged**, not altered.
- The fire model excludes flagged records from learned intervals, since a wrong
  clock corrupts cadence far more damagingly than it corrupts sync ordering.

Clamping to server time would destroy legitimately offline timestamps — a cook
logged in airplane mode and synced two days later would collapse into a single
instant, wrecking every interval in it. Rejecting would lose a user's data over
a fault they can neither see nor fix, which is the worst possible failure mode
for an offline-first app.

### 5.1 Schema migrations

- **Server:** EF Core migrations against Azure SQL, applied on deploy.
- **Device:** the SQLite database carries a **schema version number** and applies
  migrations **sequentially**. A user who skips from v1 to v5 runs migrations 2,
  3, 4 and 5 in order — never a v1→v5 special case, which is the shape that rots.
- **Every device migration is tested from every prior version.** That test matrix
  is the actual discipline here, and it is far cheaper to establish now than
  after the first field failure.

Dropping and re-syncing the device database on schema change was rejected: it
breaks the offline-first promise for anyone with unsynced cooks, and a guest
user with no account would lose everything.

### 5.2 Deletion and account closure

- Deleting an account **revokes access immediately** and soft-deletes the data.
- A **hard purge runs 30 days later**, covering accidental deletion and leaving a
  support window.
- From the moment of the request the data **contributes to nothing** — no
  baselines, no aggregates, no model training.
- The purge must cover **blobs as well as database rows**; an orphaned photo is
  still personal data.
- Individual record deletion uses the tombstone mechanism in `data-model.md`
  §5.3; this section is about the account as a whole.

## 6. Entitlements

RevenueCat is the source of truth for subscription state.

- The client uses the RevenueCat SDK for purchase and restore flows, and to
  display current entitlement.
- The **API independently verifies entitlement server-side** for every Pro-gated
  operation, by RevenueCat app user ID mapped to our account ID. A modified
  client cannot unlock server-computed analytics, photo sync, or AI access.
- The client caches the last known entitlement state for offline UI decisions.
  This cache is a UX affordance only, never a security boundary.

## 7. AI features (later phase)

- The app holds **no AI provider credentials**, ever.
- Requests go app → `Humo.Api` → the model provider, with credentials held in
  Key Vault and injected into App Service configuration.
- The API enforces Pro entitlement, rate limits per account, and controls what
  cook data is included in a prompt.

The original brief specified **AWS Bedrock**. Since everything else is on Azure,
**Azure AI Foundry is the working default** — one cloud, one credential model, no
cross-cloud egress. Because the app only ever talks to our API, the provider is a
server-side implementation detail and can be changed without an app release, so
this is deliberately left cheap to revisit at the AI slice.

## 8. Configuration and secrets

- No secrets in the mobile app bundle. Anything embedded in a mobile binary is
  public.
- API configuration comes from App Service settings; secrets from **Key Vault via
  managed identity**, not connection strings in config.
- Local development uses .NET user-secrets, never committed files.

## 9. Testing strategy

Every feature ships with unit tests in the same commit, covering edge cases and
not just the happy path. **`docs/testing.md` is the working definition of "edge
case" for this domain** — read it before writing a test list.

- **Humo.Shared.Tests** — validation, conversions (notably °C/°F round-tripping),
  and pure model logic.
- **Humo.Core.Tests** — ViewModel and service unit tests against mocked
  interfaces. This is what "no logic in code-behind" plus the `Humo.Core` split
  buys us: the interesting client behaviour is testable without a device, an
  emulator, or the MAUI workload.
- **Humo.Api.Tests** — endpoint tests via `WebApplicationFactory`, including
  sync merge semantics (out-of-order arrival, replay, conflicting updates) and
  analytics computation against fixture cooks.
- **Humo.Conventions.Tests** — the rules in `CLAUDE.md`, enforced mechanically
  rather than by review: resource parity between `AppResources.resx` and
  `AppResources.es.resx`, `Humo.Core` staying free of MAUI references,
  `Humo.Shared` referencing nothing, and no hardcoded user-facing strings in
  XAML or ViewModels.

There is no `Humo.App.Tests`. If something in the app project is worth a unit
test, it belongs in `Humo.Core` behind an interface.

`dotnet test` must pass before any work is called done — or
`dotnet test Humo.NoMaui.slnf` on a machine without the MAUI workload.

## 10. Repository layout

```
/docs
/src
  Humo.App/            MAUI, net10.0-android (+ net10.0-ios off Linux)
  Humo.Core/           ViewModels, services, repositories, fire predictor
  Humo.Api/            ASP.NET Core Minimal API
  Humo.Shared/         DTOs, enums, contracts, conversions
/tests
  Humo.Core.Tests/
  Humo.Api.Tests/
  Humo.Shared.Tests/
  Humo.Conventions.Tests/
Humo.sln
Humo.NoMaui.slnf      solution filter: everything except the MAUI app
CLAUDE.md
```

---

## Decisions

Settled 2026-08-30. Recorded so they are not silently relitigated.

| # | Decision | Rationale |
|---|---|---|
| 1 | **Azure end to end** — App Service, Azure SQL serverless, Entra External ID, Blob Storage | As the original brief specified. An all-AWS backend was considered and rejected; keeping one cloud avoids a second credential model and cross-cloud egress. |
| 2 | Identity is **Microsoft Entra External ID** | Federates Apple and Google, handles email accounts and recovery — flows not worth hand-writing alone. Free below 50k MAU. |
| 3 | **Accept database cold starts** | Azure SQL serverless auto-pause costs a resume delay on an idle sync. Sync is background by design, so it is invisible. Revisit only if a user-facing read ever waits on it. |
| 4 | Analytics recompute **on cook completion**, baselines **nightly** | A mid-cook sync carrying one entry must stay cheap; nothing a user sees is ever more than one cook stale. |
| 5 | Clock skew: **accept, record server time, flag** | Clamping destroys legitimate offline timestamps; rejecting loses user data over an invisible fault. |
| 6 | Migrations: **EF Core server-side, sequential versioned scripts on device** | Users skip versions; sequential application avoids v1→v5 special cases. Every migration tested from every prior version. |
| 7 | Account deletion: **immediate soft-delete, hard purge at 30 days**, including blobs | Covers mistaps and support windows without leaving "deleted" data alive indefinitely. |
| 8 | **Photos ship in v1** — device-first, compressed, SAS-URL upload; **free = local only, Pro = synced** | Photos are the most engaging part of a cook log. Sync is the part that actually costs money, so that is the part that is paid. |
| 9 | AI defaults to **Azure AI Foundry**, revisited at the AI slice | Keeps one cloud. Reversible server-side without an app release, so it is cheap to change on real pricing. |
| 10 | Charting: **LiveChartsCore.SkiaSharpView.Maui 2.0.5**, behind `CookChartData` | Checked before adopting rather than after. 2.0.5 is stable, not one of the long beta line; the licence is **MIT** with no commercial tier, which matters here because FluentAssertions 8 had already been rejected over exactly that; and it ships `net10.0-android` and `net10.0-ios` assemblies, so neither target needs a fallback. The abstraction keeps series, units, ordering and markers in `Humo.Core` where they are unit-tested, leaving the package responsible only for pixels. |

## Open questions

1. **Azure SQL serverless minimum capacity and auto-pause delay** need checking
   against a realistic idle pattern before committing — the cost floor and the
   resume time are both configuration.
2. **Entra External ID user flows vs. native SDK flows.** The hosted user flow is
   fast to ship and looks like a web page inside a native app; native flows look
   right and cost more work. Affects how the first-launch screen actually feels.
3. **Notification quick-response reliability is unverified.** iOS caps pending
   local notifications (64) and both platforms restrict background work.
   Handling "Added log" without launching the app needs platform-specific
   verification — **spike this before the fire model slice**, not during it. If
   background responses cannot reliably write data, the whole interaction design
   changes.
4. ~~**LiveCharts2 on MAUI net10.0** — version, licensing, and iOS/Android
   rendering behaviour need verification before the charts slice.~~
   **Resolved at the charts slice; see Decision 10.** Version and licensing check
   out. Rendering on a real device is the one part still unverified, and it is
   now a manual-verification item rather than an adoption risk: the charting
   package is confined to `Humo.App` behind `CookChartData`, so replacing it
   would be a view change.
5. **Photo upload retry and storage cost are unmodelled.** SAS URLs and a
   separate upload queue are specified, but not the retry policy, the per-account
   storage ceiling, or what happens when a Pro subscription lapses with photos
   already synced.
6. **CI exists; CD does not.** `.github/workflows/ci.yml` runs the tests and
   builds the app for Android (Linux) and iOS (macOS) on every PR to `main`.
   Nothing yet deploys: publishing the API to App Service, applying EF Core
   migrations on deploy, and distributing app builds to TestFlight or Play
   internal testing are all still unbuilt, and each needs credentials stored as
   repository secrets.
7. **The iOS CI job builds for the simulator only.** That compiles
   `Platforms/iOS` and processes `Info.plist` — enough to catch the class of
   defect that the Android build caught — but it does not sign, archive, or
   prove the app runs on a device. Device builds need an Apple Developer
   account and certificates in secrets.
