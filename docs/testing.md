# Humo — Testing

**Status:** Draft v0.1 · **Last updated:** 2026-08-30

`CLAUDE.md` states the rule: every feature ships with unit tests in the same
commit, covering edge cases and not just the happy path. This document is the
working definition of "edge case" for *this* domain, so that "did I cover
everything?" is a checklist question rather than a matter of memory.

---

## 1. Where tests live

| Project | Covers | Needs |
|---|---|---|
| `Humo.Shared.Tests` | Conversions, DTO validation, pure model logic | nothing |
| `Humo.Core.Tests` | ViewModels, services, repositories, the fire predictor | nothing |
| `Humo.Api.Tests` | Endpoints via `WebApplicationFactory`, sync merge rules, analytics | nothing |
| `Humo.Conventions.Tests` | The rules in `CLAUDE.md`, enforced mechanically | nothing |

None of them require the MAUI workload or a device — that is the entire reason
ViewModels live in `Humo.Core` rather than in the app project. `dotnet test
Humo.NoMaui.slnf` runs all four anywhere.

There is no `Humo.App.Tests`. `Humo.App` contains only XAML, code-behind
constructors, platform service implementations, and DI wiring. If something in
there is worth a unit test, it is in the wrong project — move it to `Humo.Core`
behind an interface.

## 2. The conventions tests

`Humo.Conventions.Tests` fails the build when a rule from `CLAUDE.md` is broken,
rather than relying on anyone noticing in review:

- **Resource parity** — an English string with no Spanish counterpart, a Spanish
  value left as untranslated English, an `AppStrings` constant with no resource,
  or an empty resource value.
- **`Humo.Core` stays platform-free** — it must not reference `Microsoft.Maui.*`,
  because that is what keeps ViewModels testable without a device.
- **`Humo.Shared` references nothing** — no package or project references, so the
  wire contract cannot drift toward either side.
- **No hardcoded user-facing strings in XAML** — `Text`, `Title`,
  `Placeholder` and friends must bind, not carry a literal.

When a rule can be checked mechanically, check it mechanically. A convention
enforced only by review is a convention that erodes.

## 3. Edge cases that recur in this domain

Work the relevant rows when planning a test list. Not every row applies to every
change; a row that applies and has no test is a gap.

### 3.1 Units and temperature

- Round trip: a value the user typed in °F or lb must come back out unchanged
  after storage in °C or kg.
- Known reference points: 0/100 °C, −40 (where the scales meet), 225 °F.
- **Deltas versus points on the scale** — a 10 °C rise is 18 °F, not 50 °F.
  Anything that computes a *change* in temperature needs its own test.
- Invalid or out-of-range enum values.
- Negative temperatures (ambient below freezing is normal for winter cooks).
- `NaN` and infinity reaching a conversion or an average.
- Precision: display rounds, storage never does.

### 3.2 Localization

- Both languages, for every user-facing path.
- Regional variants folding onto the neutral culture (`es-AR`, `es-MX` → `es`).
- An unsupported language falling back to English.
- A missing resource key.
- A stored language override the platform no longer recognises.
- Culture-dependent number and date formatting (Spanish uses a decimal comma).
- **The unit setting must not move when the language does, and vice versa.**
  Assert this explicitly wherever both are in scope; it is the rule most likely
  to be broken by an innocent-looking change.
- Runtime language switching re-resolves strings without a page rebuild.

### 3.3 Time

- Everything is UTC in storage; local time only at display.
- A cook that spans midnight, a month boundary, a DST transition, or a timezone
  change mid-cook.
- `recordedAt` in the past (users log readings a few minutes late) and, from a
  wrong device clock, in the future.
- Zero-length and negative intervals.
- Cooks still in progress (`finishedAt` is null) wherever duration is computed.

### 3.4 Offline and local storage

- Every user action must succeed with no connectivity.
- An empty database, and a first run with no data at all.
- Concurrent writes during an active cook.
- Records created offline keeping their client-generated GUID after sync.
- Device database migration across skipped app versions.

### 3.5 Sync

- Replay: the same batch twice produces the same state.
- Out-of-order arrival: a child record before its parent.
- Conflicting edits to the same mutable record, resolved by `updatedAt`.
- Identical `updatedAt` on both sides (the tie must be deterministic).
- Tombstoned and superseded records excluded from analytics and the fire model.
- A partial batch failing mid-way.
- **Account scoping:** a request that names another account's ID in its body
  must not touch that account's data. Test this as a security case, not a
  happy-path case.

### 3.6 Entitlements

- Free and Pro users on every gated path.
- Entitlement expiring mid-session.
- The offline cached entitlement being stale or absent.
- A client claiming Pro that the server does not agree with — the server wins.

### 3.7 Fire model

- Cold start: no history at all. The predictor must decline to predict rather
  than guess.
- Each rung of the fallback ladder, and the transition between them.
- Intervals that must be excluded: across cook boundaries, the first load of a
  cook, superseded events, implausibly long gaps.
- Every prompt response — added fuel, still fine, snoozed, ignored.
- A single outlier interval not moving the median much (this is why it is a
  median).
- Predictions and reasons resolving in both languages, via resource keys rather
  than formatted English.

### 3.8 Analytics

- A user with too few cooks for a baseline — the honest "not enough data yet"
  path, not a meaningless flag.
- Exactly at the ±2σ boundary.
- Zero variance (every cook identical), which makes σ zero and must not divide
  by it.
- A single cook, and a single temperature entry.
- Cooks with no pit temps logged at all.

### 3.9 API

- Unauthenticated and wrong-account requests.
- Malformed payloads and missing required fields.
- Payloads at probe-data scale (thousands of entries), not just manual scale.

## 4. What good looks like

- One behaviour per test, named as a sentence.
- `[Theory]` for the same behaviour across many inputs; `[Fact]` for one case.
- Arrange/act/assert, with the interesting value visible in the test rather than
  hidden in a helper.
- A comment when the *reason* is not obvious from the assertion — why 18 and not
  50, why a median, why this must not throw.
- Deterministic: no wall-clock reads, no random data, no ordering assumptions.
  Inject time; never call `DateTimeOffset.UtcNow` inside logic under test.

---

## Open questions

1. **No coverage threshold is set.** `coverlet.collector` is already referenced,
   so a number could be enforced in CI. A hard gate tends to produce tests
   written for the metric; the alternative is reviewing coverage reports
   occasionally. Undecided which is worth it for a solo project.
2. **No UI or integration test layer is planned.** ViewModel tests cover
   behaviour, but nothing exercises a real XAML page, a real SQLite file, or a
   real device notification. The fire model's notification delivery in
   particular cannot be proven by unit tests at all — it needs the platform
   spike named in `fire-model.md`.
3. **No property-based testing.** Conversions and sync merge rules are the two
   places where generated inputs would likely find something example tests miss.
   Worth considering when sync lands.
4. **Test data builders do not exist yet.** By slice 3 the tests will need to
   construct cooks with many entries; without shared builders that turns into
   copy-paste. Worth adding at the first sign of duplication, not before.
