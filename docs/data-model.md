# Humo — Data Model

**Status:** Draft v0.1 · **Last updated:** 2026-08-30

Entities, storage rules, and sync semantics. Read alongside `architecture.md`.

---

## 1. Principles

1. **Client-generated GUIDs.** Every record's primary key is a `Guid` created on
   the device. A record has its final identity before it has ever seen a
   network.
2. **SQLite on device is the source of truth during a cook.** The server is a
   durable, queryable replica that also computes analytics.
3. **Append-only sync, last-write-wins.** New records are appended; mutable
   records resolve conflicts by latest `updatedAt`.
4. **Celsius everywhere in storage.** Every temperature column is °C. Conversion
   to °F happens at display, once, in a converter. No °F ever reaches the
   database, the API, or a cached analytic.
5. **Account scoping.** Every row belongs to an account ID, derived server-side
   from the auth token.
6. **UTC timestamps.** All instants are stored UTC. Cooks span midnight, time
   zones, and DST transitions; a naive local timestamp breaks interval maths in
   exactly the places the fire model depends on.

## 2. Common fields

Every synced entity carries:

| Field | Type | Notes |
|---|---|---|
| `id` | `Guid` | Client-generated, primary key, never reassigned. |
| `accountId` | `Guid` | Server-assigned scope; client sends nothing authoritative. |
| `createdAt` | `DateTimeOffset` | UTC, set by the creating client. |
| `updatedAt` | `DateTimeOffset` | UTC, set on every local mutation. Drives LWW. |
| `deletedAt` | `DateTimeOffset?` | Soft-delete tombstone. See §5.3. |
| `syncedAt` | `DateTimeOffset?` | **Local only**, never sent. Null = pending upload. |

## 3. Entities

### 3.1 Equipment

The rig. The unit the fire model learns against.

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | `Guid` | ✅ | |
| `name` | `string` | ✅ | User-supplied, e.g. "Old Country Brazos". |
| `type` | `EquipmentType` | ✅ | `Offset \| Kettle \| Kamado \| Wsm \| Pellet \| Parrilla` |
| `fireboxVolumeL` | `double?` | | Litres. Feeds fire model as a capacity hint. |
| `cookChamberVolumeL` | `double?` | | Litres. |
| `insulation` | `InsulationLevel` | ✅ | `None \| Light \| Heavy` |
| `notes` | `string?` | | |

`type` and `insulation` are **enums with localized display names**, not stored
strings. The stored value is the enum; the label is a resource lookup. This is
what makes a bilingual UI possible over shared data.

### 3.2 Cook

One session.

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | `Guid` | ✅ | |
| `equipmentId` | `Guid` | ✅ | FK → Equipment. |
| `meatType` | `MeatType` | ✅ | Enum + `Other` escape hatch. `Other` cooks are excluded from cross-cook grouping. |
| `meatTypeOther` | `string?` | | Free text, only when `meatType == Other`. |
| `weightKg` | `double` | ✅ | Kilograms. Displayed in lb per user preference. |
| `targetInternalTempC` | `double?` | | Optional: a parrilla cook working by feel has no target temp. |
| `startedAt` | `DateTimeOffset` | ✅ | UTC. |
| `finishedAt` | `DateTimeOffset?` | | Null = cook in progress. |
| `ambientTempC` | `double?` | | At start; may be refined by later entries. |
| `notes` | `string?` | | |
| `rating` | `int?` | | **1–5, rating the result** — how the food turned out, not how well the cook was executed. Outcome is the useful analytics signal. |

**Derived, cached at sync (server-computed, read-only on client):** total
duration, time per kg, stall start/end/duration, pit temp stability score, fuel
efficiency, anomaly flags. Stored in a separate `CookAnalytics` row keyed by
cook ID so recomputation never rewrites user-entered data.

### 3.3 TempEntry

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | `Guid` | ✅ | |
| `cookId` | `Guid` | ✅ | FK → Cook. |
| `recordedAt` | `DateTimeOffset` | ✅ | UTC. Not necessarily entry time — see below. |
| `meatTempC` | `double` | ✅ | |
| `pitTempC` | `double?` | | |
| `note` | `string?` | | |
| `ambientTempC` | `double?` | | Ambient at this moment, if the cook recorded it. |
| `source` | `TempSource` | ✅ | `Manual` today; `Probe`/`Import` reserved — see §6. |

`ambientTempC` is optional here as well as on `Cook`. Ambient changes over a
14-hour cook, and fire model Level 2 wants it as a time-varying feature. Putting
it on the entry the cook is already creating gives a time series for free, with
no new entity, no extra interaction, and no location permission.

`recordedAt` defaults to now but is **editable**, because cooks routinely log a
reading a few minutes after taking it, and a wrong timestamp distorts both the
stall calculation and the fire model.

### 3.4 FuelEvent

The record the fire model learns from. Its capture must stay ≤2 taps
(`product-spec.md` §4.4).

**A fuel event belongs to the equipment, not to a cook.** A firebox does not know
how many pieces of meat are above it: cooks routinely run a brisket and ribs in
one smoker, fed by one fire. Scoping fuel to the rig means concurrent cooks share
one fuel series and one learned cadence, instead of the model seeing every load
twice and predicting roughly twice as often as the fire needs.

It also keeps logging at ≤2 taps with two cooks running — there is no "which cook
is this for?" question, because the answer is "the fire".

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | `Guid` | ✅ | |
| `equipmentId` | `Guid` | ✅ | FK → Equipment. The fire this fed. |
| `cookId` | `Guid?` | | The cook that was on screen when it was logged, for display only. Never used by the fire model. |
| `recordedAt` | `DateTimeOffset` | ✅ | UTC. |
| `woodType` | `WoodType` | ✅ | Enum + `Other`. Pre-filled from previous event. |
| `woodTypeOther` | `string?` | | |
| `form` | `FuelForm` | ✅ | `Split \| Chunk \| Charcoal \| Pellets` |
| `sizeClass` | `SizeClass` | ✅ | `Small \| Medium \| Large` — the one required tap. |
| `count` | `int` | ✅ | Defaults to 1. |
| `weightKg` | `double?` | | Optional, for cooks who weigh their wood. |
| `viaNotification` | `bool` | ✅ | True when created from an "Added log" quick response. Lets the fire model weight self-reported-under-prompt events differently from spontaneous ones. |

`viaNotification` is an addition to the brief's schema. Without it, an event
created *because we asked* is indistinguishable from one created because the
fire needed it — which biases the learned cadence toward whatever cadence we
already predicted. Level 1 excludes prompt-driven events from cadence learning
entirely (`fire-model.md` §3.5). Flagged rather than assumed; drop it if you
disagree.

**Thermal load.** The fire model needs the combined weight of everything in the
chamber, because meat is a heat sink: more protein flattens the pit's rise after
a fuel load and lengthens its recovery. That figure is derived — the sum of
`weightKg` across every cook active on the rig at that moment — so it needs no
new field, but it is only computable *because* fuel and cooks are both anchored
to equipment. See `fire-model.md` §2.1.

### 3.5 CookEvent

Milestones. **Named `CookEvent`, not `Event`** — `Event` collides with C#'s
`event` keyword in enough contexts to be a persistent annoyance, and reads
ambiguously next to domain events in the sync layer. Same shape as the brief.

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | `Guid` | ✅ | |
| `cookId` | `Guid` | ✅ | FK → Cook. |
| `recordedAt` | `DateTimeOffset` | ✅ | UTC. |
| `type` | `CookEventType` | ✅ | `Wrapped \| Spritzed \| Rested \| Other` |
| `note` | `string?` | | |

### 3.6 Photo

Photos ship in v1. A photo belongs to a cook, and may optionally be pinned to a
moment within it — "here's the bark at the wrap" belongs on the timeline where it
happened, not in an undifferentiated gallery.

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | `Guid` | ✅ | |
| `cookId` | `Guid` | ✅ | FK → Cook. Every photo belongs to a cook. |
| `subjectId` | `Guid?` | | The TempEntry / FuelEvent / CookEvent it is pinned to. |
| `subjectType` | `PhotoSubject?` | | `TempEntry \| FuelEvent \| CookEvent`. Null = cook-level. |
| `capturedAt` | `DateTimeOffset` | ✅ | UTC. |
| `localPath` | `string?` | ✅ local | **Local only**, never sent. Where the file is on device. |
| `storageKey` | `string?` | | Blob name once uploaded. Null = not synced. |
| `widthPx` / `heightPx` | `int` | ✅ | For layout without reading the file. |
| `byteSize` | `long` | ✅ | Post-compression size. |
| `caption` | `string?` | | User text. Note: user-authored, so never localized. |

**Free users keep photos on device only; Pro users get them synced.** Sync is the
part that actually costs money, so sync is the part that is paid. The client
therefore treats photo upload as a **separate, resumable queue** from record
sync — a failed 3 MB upload must never block a 200-byte temperature entry.

Images are compressed on device before upload (long edge ~2048px, JPEG). Bytes
go phone↔Blob Storage directly via SAS URLs; they never pass through the API,
which owns authorization and metadata only.

### 3.7 Supporting records (not in the original brief)

- **`FireCheckPrompt`** — a scheduled or delivered fire-check notification and
  its response (`AddedFuel \| StillFine \| Snoozed \| Ignored`). The fire model
  cannot learn from responses it does not store, and "Still fine" is a genuine
  negative signal that appears nowhere in the brief's schema. Detailed in
  `fire-model.md`.
- **`CookAnalytics`** — server-computed cached metrics per cook (§3.2).
- **`UserBaseline`** — per (accountId, meatType, equipmentId) mean and standard
  deviation per metric, plus the sample size behind it. Anomaly flags read from
  here; sample size is what lets the UI say "not enough cooks yet" honestly.

## 4. Enumerations

All enums are stored as their integer or stable string value and displayed via
resource lookup. **Never store the display string.** Adding a value is additive;
values are never renumbered.

- `EquipmentType`: Offset, Kettle, Kamado, Wsm, Pellet, Parrilla
- `InsulationLevel`: None, Light, Heavy
- `FuelForm`: Split, Chunk, Charcoal, Pellets
- `SizeClass`: Small, Medium, Large
- `CookEventType`: Wrapped, Spritzed, Rested, Other
- `TempSource`: Manual (Probe, Import reserved — §6)
- `PhotoSubject`: TempEntry, FuelEvent, CookEvent
- `PromptResponse`: AddedFuel, StillFine, Snoozed, Ignored, Dismissed
- `MeatType`: proposed — Brisket, PorkButt, PorkRibs, BeefRibs, Chicken, Turkey,
  Pork Loin, Lamb, Sausage, Other. Argentine cuts to be added with the parrilla
  work; the enum is designed to grow.
- `WoodType`: proposed — Oak, PostOak, Hickory, Mesquite, Pecan, Apple, Cherry,
  Maple, Quebracho, Espinillo, Other. The last two matter for asado.

## 5. Sync semantics

### 5.1 Append-only records

`TempEntry`, `FuelEvent`, `CookEvent`, and `FireCheckPrompt` are **immutable
after creation** in the normal case. They arrive at the server, are inserted by
`id`, and re-sending the same `id` is a no-op. No conflict is possible.

### 5.2 Mutable records

`Cook` and `Equipment` are edited (a cook is finished, rated, renamed). These
resolve **last-write-wins at record granularity**, comparing `updatedAt`. The
losing version is discarded, not merged field-by-field.

Record-granularity LWW means editing a cook's notes on one device and its rating
on another loses one of the two edits. This is accepted for a solo-user app
where multi-device concurrent editing is rare, and it is stated here so nobody
is surprised. Field-level merge is the escape hatch if it becomes a real problem.

### 5.3 Corrections and deletions

Strict append-only has no answer for "I typed 165 instead of 65". Rather than
abandon append-only, corrections use **tombstones plus replacement**:

- Editing an append-only record writes `deletedAt` on the original and inserts a
  new record with a new `id` and a `supersedesId` pointing at the original.
- Deleting writes `deletedAt` only.
- Nothing is ever physically removed by sync. Server-side purge for account
  deletion is a separate, deliberate operation.

Analytics and the fire model read only records where `deletedAt is null`.

### 5.4 Protocol

- **Push:** client sends all records with `syncedAt == null`, batched, ordered
  parents-before-children (Equipment → Cook → children) so foreign keys resolve.
- **Pull:** client sends its cursor; server returns records changed since, plus
  a new cursor.
- **Idempotent:** a replayed batch produces the same state. Insert-by-`id`,
  LWW-by-`updatedAt`.
- **Ordering:** records are ordered **parents before children within a batch**,
  so the normal case never fails. A genuinely orphaned record — possible across
  retries and partial batches — is **rejected with a retryable error**, and the
  client re-sends it with its parent. It is never held server-side and never
  silently dropped.

  Holding orphans would mean a pending-records table, an expiry policy for
  orphans whose parent never arrives, and a second state machine to reason
  about. Accepting them would allow referentially broken rows that every query
  and every analytic then has to defend against.
- `accountId` is always taken from the token, never from the payload.

## 6. Designing for probe data (Level 3) now

`TempSource` exists so that a future stream of probe readings is the same table,
not a parallel one. Two consequences to accept now rather than retrofit:

- Manual entries arrive every 20–60 minutes; probe entries arrive every 30
  seconds. Same table, wildly different volumes. Indexing on
  `(cookId, recordedAt)` and paging by time range from day one avoids a painful
  migration.
- Charts and analytics must downsample rather than assume a manual-scale point
  count.

## 7. Units

| Quantity | Stored | Displayed |
|---|---|---|
| Temperature | °C (`double`) | °C or °F per **user setting, independent of language** |
| Weight | kg (`double`) | kg or lb per user setting |
| Volume | litres (`double`) | L or gal per user setting |
| Instants | UTC `DateTimeOffset` | Local time, formatted per culture |

Conversion lives in **one** place in `Humo.Shared` with unit tests, including
round-trip cases (`225°F → °C → °F`). Display rounds; storage never does.

---

## Decisions

Settled 2026-08-30.

| # | Decision | Rationale |
|---|---|---|
| 1 | `meatType` and `woodType` are **enums + `Other` free text** | Analytics must group and the UI must translate; free text does neither. `Other` cooks are excluded from grouping — the honest cost of the escape hatch. |
| 2 | `rating` is **1–5 on the result** | Outcome is the useful analytics signal; self-assessed execution tracks how the cook felt. |
| 3 | `targetInternalTempC` is **optional** | Parrilla cooks have no target temp. One field, one form; the form varies by equipment type, the schema does not. |
| 4 | **Ambient moves onto `TempEntry`** as an optional field | Gives a time series for Level 2 with no new entity, no extra interaction, and no location permission. Wind stays unmodelled until Level 2 proves it needs it. |
| 5 | **Photos ship in v1** — cook-level, optionally pinned to an entry; free = local, Pro = synced | Photos are the most engaging part of a cook log. Upload runs on its own resumable queue so a 3 MB image never blocks a 200-byte reading. |
| 6 | Out-of-order arrival: **ordered within a batch, reject-and-retry across batches** | Holding orphans means a pending table and an expiry policy; accepting them allows referentially broken rows every query must defend against. |
| 7 | Account deletion: **immediate soft-delete, hard purge at 30 days**, blobs included | See `architecture.md` §5.2. Deleted data contributes to no baseline or aggregate from the moment of the request. |
| 8 | Free tier limits **visibility, not retention** | The server keeps everything, so upgrading reveals real history and real baselines rather than an empty screen. Requires an explicit privacy-policy statement. |
| 9 | Device migrations are **sequential and versioned** | Users skip versions; v1→v5 runs 2, 3, 4, 5 in order. Every migration tested from every prior version. |

## Open questions

1. **`viaNotification` on `FuelEvent` is an addition to your schema.** Justified
   in §3.4 — without it the fire model partly trains on its own predictions —
   but it is my invention, not your spec. Confirm or drop.
2. **Renaming `Event` → `CookEvent` is a deviation from your schema.** Flagged so
   it stays a decision rather than a silent edit.
3. **Photo storage has no ceiling.** No per-account limit, no per-cook cap, and
   no defined behaviour when a Pro subscription lapses with photos already
   synced — do they stay, expire, or become read-only?
4. **`subjectType`/`subjectId` on `Photo` is a loose polymorphic reference.** It
   cannot be enforced by a foreign key, so integrity depends on application code.
   A per-parent nullable column (`tempEntryId`, `fuelEventId`, `cookEventId`)
   would be enforceable but wider. Worth revisiting when photos are implemented.
5. **`weightKg` is required on `Cook`, but not every cook is weighed.** A cook
   who does not weigh their meat currently cannot start a cook without inventing
   a number, and time-per-kg is meaningless for them. Making it optional is the
   obvious fix — but weight now also feeds the fire model's thermal load, so an
   unweighted cook degrades the fire prediction for everything sharing that rig,
   not just its own analytics. Needs deciding with that cost in view.
6. **Pit temperature is ambiguous when two cooks share a rig.** `TempEntry`
   couples `meatTempC` (per cook) with `pitTempC` (per fire), so two concurrent
   cooks can record conflicting pit temps for the same fire at the same instant.
   Fuel events moved to the equipment; pit temperature arguably should too.
7. **`FuelEvent.cookId` is display-only and could drift.** It records which cook
   was on screen, which may not be the cook a reader later associates with that
   fire. Harmless if it stays display-only, misleading if anything starts
   treating it as ownership.
