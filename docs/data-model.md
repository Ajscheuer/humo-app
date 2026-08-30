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
| `meatType` | `MeatType` | ✅ | Enum + `Other` escape hatch — see open questions. |
| `meatTypeOther` | `string?` | | Free text, only when `meatType == Other`. |
| `weightKg` | `double` | ✅ | Kilograms. Displayed in lb per user preference. |
| `targetInternalTempC` | `double?` | | Nullable — see open questions (asado). |
| `startedAt` | `DateTimeOffset` | ✅ | UTC. |
| `finishedAt` | `DateTimeOffset?` | | Null = cook in progress. |
| `ambientTempC` | `double?` | | At start; may be refined by later entries. |
| `notes` | `string?` | | |
| `rating` | `int?` | | Scale undefined — see open questions. |

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
| `source` | `TempSource` | ✅ | `Manual` today; `Probe`/`Import` reserved — see §6. |

`recordedAt` defaults to now but is **editable**, because cooks routinely log a
reading a few minutes after taking it, and a wrong timestamp distorts both the
stall calculation and the fire model.

### 3.4 FuelEvent

The record the fire model learns from. Its capture must stay ≤2 taps
(`product-spec.md` §4.4).

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | `Guid` | ✅ | |
| `cookId` | `Guid` | ✅ | FK → Cook. |
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
already predicted. Flagged rather than assumed; drop it if you disagree.

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

### 3.6 Supporting records (not in the original brief)

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
- **Ordering-tolerant:** a child arriving before its parent (possible across
  retries) is either rejected with a retryable error or held; it must never be
  silently dropped. Decision pending — see open questions.
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

## Open questions

1. **`meatType` and `woodType`: enum or free text?** Modelled above as enums
   with an `Other` escape hatch, because analytics group by them and the UI is
   bilingual — free text can be neither grouped nor translated. If you want free
   text, analytics grouping and the Spanish UI both need a different answer.
2. **`Cook.rating` scale is undefined.** 1–5 or 1–10, and rating *what* — the
   food, or how well the cook was executed? They trend differently and one of
   them is a useful analytics input while the other is mostly vanity.
3. **`targetInternalTemp` modelled as nullable, against the brief.** The brief
   lists it as required, but a parrilla cook has no target internal temp. Either
   it is nullable (chosen here) or the parrilla equipment type gets a different
   cook-creation flow. Needs your call.
4. **`viaNotification` on FuelEvent is an addition.** Justified in §3.4 —
   without it the fire model trains partly on its own predictions. Confirm or
   remove.
5. **Renaming `Event` → `CookEvent` is a deviation from the brief's schema.**
   Called out so it is a decision, not a silent edit.
6. **Out-of-order arrival policy is undecided.** Reject-and-retry is simpler;
   hold-and-resolve is friendlier on a flaky connection. Pick one before the
   sync slice.
7. **Account deletion vs. append-only.** App stores require account deletion,
   and append-only tombstones do not delete anything. We need an explicit purge
   path, a stated retention period, and a decision on whether deleted-account
   data contributes to anything (it should not).
8. **Free-tier history limit interacts with this model.** If free users are
   limited to N cooks, does the *server* keep the rest? If it does not, upgrading
   to Pro produces an empty analytics view and permanently poorer baselines.
   Recommendation in `product-spec.md` open question 2: retain, gate visibility.
9. **`ambientTempC` is a single value on Cook, but ambient changes over a 14-hour
   cook.** Fire model Level 2 wants ambient as a time-varying feature (and wind,
   which is not modelled at all). Options: periodic ambient readings on
   `TempEntry`, a separate `ConditionsEntry`, or fetching weather by location and
   time. Each has a privacy or accuracy cost. Undecided.
10. **No photo support anywhere in the model.** Not in the brief, but a BBQ
    logging app without photos of the bark is a surprising omission and the
    storage/sync design for binaries is very different from rows. Confirm it is
    genuinely out of scope for v1.
11. **SQLite migration strategy for the device database.** Users skip versions;
    a migration path must handle v1 → v5 directly. Needs deciding before the
    first post-launch schema change.
