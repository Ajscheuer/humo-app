# Humo — Fire Management Model

**Status:** Draft v0.1 · **Last updated:** 2026-08-30

The fire model is Humo's differentiator. Everything else in this category is a
logbook. This document specifies the three levels, the shared predictor
interface they all implement, and the notification loop that turns predictions
into training data.

---

## 1. The problem

An offset smoker burning splits needs fuel roughly every 30–60 minutes,
depending on the rig, the wood, the weather, and how the cook runs their fire.
Miss it and pit temp drops; over-correct and it spikes. Experienced cooks
develop a feel for their own rig's cadence. Humo's job is to learn that cadence
from logged data and tell the cook *before* the fire needs attention rather than
after the temperature has already fallen.

**This is a reminder, not a safety device.** That framing belongs in the product
copy (`product-spec.md` open question 9) and constrains how confident the
notification wording may sound.

## 2. Shared predictor interface

All three levels implement the same interface so that Level 3 is a swap, not a
rewrite. Level 1 ships against it and every later level is tested against the
same fixtures.

```csharp
public interface IFirePredictor
{
    // When will fuel next be needed?
    FirePrediction PredictNextFuelNeed(FirePredictionContext context);
}

public sealed record FirePredictionContext(
    Equipment Equipment,
    Cook Cook,
    IReadOnlyList<FuelEvent> FuelEventsThisCook,
    IReadOnlyList<TempEntry> TempEntriesThisCook,   // ignored by Level 1
    IReadOnlyList<FireCheckPrompt> PromptsThisCook,
    BurnCadenceProfile? LearnedProfile,             // null on cold start
    AmbientConditions? Ambient,                     // ignored by Level 1
    DateTimeOffset Now);

public sealed record FirePrediction(
    DateTimeOffset? PredictedAt,      // null when the model declines to predict
    ConfidenceLevel Confidence,       // None | Low | Medium | High
    string ReasonKey,                 // resource key, never a formatted string
    IReadOnlyDictionary<string, object> ReasonArgs);
```

Three properties of this interface matter more than the specific fields:

- **`PredictedAt` is nullable.** A model that cannot predict must say so rather
  than guess. Cold start is the normal case for a new user, and a confidently
  wrong first prediction destroys trust in the feature permanently.
- **`ReasonKey` is a resource key, not text.** The explanation shown to the user
  ("based on 12 similar fuel loads") is bilingual, so the predictor returns a key
  plus arguments and the UI localizes and formats it. A predictor that returns
  English prose cannot ship in a Spanish UI.
- **The context carries prompts, not just events.** A "Still fine" response is a
  real observation — the fire did *not* need fuel at that moment — and any level
  that ignores it discards half its signal.

## 3. Level 1 (MVP) — learned burn cadence

### 3.1 The estimator

For a given `(equipmentId, woodType, sizeClass)`, compute the **median interval**
between consecutive `FuelEvent`s across the user's history.

Median rather than mean: fuel intervals are heavily right-skewed by the cook who
puts the phone down for three hours, and a single outlier would wreck a mean.

```
interval_i   = fuelEvent[i].recordedAt − fuelEvent[i−1].recordedAt
cadence      = median(intervals for this equipment + woodType + sizeClass)
predictedAt  = lastFuelEvent.recordedAt + cadence
```

### 3.2 Fallback ladder for sparse data

Exact-match history is rare early on. The predictor widens its grouping in
order, and confidence drops at each step:

| Grouping | Min. intervals | Confidence |
|---|---|---|
| equipment + woodType + sizeClass | 5 | High |
| equipment + sizeClass | 5 | Medium |
| equipment (any fuel) | 4 | Medium |
| equipment type + sizeClass (across the user's rigs) | 4 | Low |
| — nothing sufficient — | | **None → no prediction** |

There is **no global/population fallback.** A stranger's offset tells us nothing
useful about yours, and a prediction from someone else's fire is worse than
none.

### 3.3 Cold start

A brand-new user gets no fire-check notifications until enough intervals exist —
typically midway through their second cook on a rig. The UI must be explicit
about this ("Humo is still learning your fire — 3 more fuel loads") rather than
silently doing nothing, or the feature reads as broken. This is a first-run
experience problem as much as a modelling one.

### 3.4 Intervals that must be excluded

Not every gap between fuel events is a burn cadence:

- Intervals spanning a cook boundary (last event of one cook → first of the
  next). Only within-cook intervals count.
- Intervals containing the first fuel event of a cook, where the firebox was
  loaded at startup rather than replenished.
- Intervals bounded by a superseded or tombstoned event (`data-model.md` §5.3).
- Intervals longer than a plausibility ceiling (proposed: 4 hours) — these are
  almost always "the user stopped logging", not "the fire burned for 4 hours".

That last exclusion is a heuristic and will silently discard genuine long
intervals on a well-insulated kamado. Flagged below.

## 4. The notification loop

```
      ┌─────────────────────────────────────────────┐
      │ FuelEvent logged (or cook started)          │
      └────────────────────┬────────────────────────┘
                           ▼
             IFirePredictor.PredictNextFuelNeed
                           │
              ┌────────────┴────────────┐
       PredictedAt == null        PredictedAt set
              │                          │
      show "still learning"     schedule local notification
                                         │
                                         ▼
                          ┌──────────────────────────┐
                          │ "Fire check — time to     │
                          │  check your fire?"        │
                          │ [Added log][Still fine]   │
                          │ [Snooze]                  │
                          └──────────┬────────────────┘
                                     ▼
                        record FireCheckPrompt response
                                     │
        ┌────────────────────────────┼──────────────────────────┐
        ▼                            ▼                          ▼
  Added log                    Still fine                   Snooze
  → create FuelEvent           → negative signal:            → reschedule +N min
    (viaNotification = true)     cadence is longer             record the snooze
  → re-predict                   than predicted
                               → reschedule, lengthen
```

### 4.1 FireCheckPrompt

Every prompt is persisted with what was predicted and what came back:

| Field | Type | Notes |
|---|---|---|
| `id` | `Guid` | |
| `cookId` | `Guid` | |
| `scheduledFor` | `DateTimeOffset` | What the model predicted. |
| `deliveredAt` | `DateTimeOffset?` | Null if never delivered. |
| `respondedAt` | `DateTimeOffset?` | |
| `response` | `PromptResponse` | `AddedFuel \| StillFine \| Snoozed \| Ignored \| Dismissed` |
| `predictorVersion` | `string` | Which model made this call. |
| `confidence` | `ConfidenceLevel` | What it claimed at the time. |

`predictorVersion` is what makes "did Level 2 actually beat Level 1?" an
answerable question instead of a matter of opinion. Without it, every model
change is a leap of faith.

### 4.2 Learning from each response

- **Added log** — creates a `FuelEvent` with `viaNotification = true`. Confirms
  the prediction; the interval feeds the cadence estimator.
- **Still fine** — the fire did not need fuel yet. Lengthen the working estimate
  for this cook and reschedule. Crucially this is stored, so the model can learn
  that it systematically predicts early on this rig.
- **Snooze** — reschedule by a fixed increment (proposed: 15 minutes), record
  the snooze. Repeated snoozes are a strong signal the cadence is too short.
- **Ignored** — no response before the next event. Weak signal, worth recording;
  it may just mean the phone was in a pocket.

**The feedback loop is self-reinforcing and must be watched.** If the model
predicts every 45 minutes and the user obediently adds a log at every prompt,
the model learns 45 minutes regardless of what the fire wanted. `viaNotification`
lets us down-weight prompt-driven events, and Level 2's temperature evidence is
the real correction. This is the single biggest modelling risk in Level 1.

## 5. Level 2 — temperature-informed correction

Level 1 knows only clock intervals. Level 2 adds the evidence that actually
matters: what the pit temperature did.

### 5.1 The burn curve

Each fuel event produces a characteristic pit-temp response — **rise** to a
peak, then **decay** as the fuel is consumed. Level 2 fits a simple
three-parameter shape (rise rate, peak magnitude and time-to-peak, decay rate)
per `(equipment, woodType, form, sizeClass)` from historical `TempEntry` data
around each fuel event.

From the fitted curve, an **expected envelope** for the current cook is
projected forward from the last fuel event. When logged pit temps trend below
that envelope — sustained, not a single reading, and not a spike from an open
lid — the next fire check is **pulled earlier**, proportional to the shortfall.

Level 2 only ever *corrects* Level 1's cadence within bounds; it does not
replace it. If temperature data is sparse (a cook logging meat temp only), Level
2 degrades to Level 1's answer rather than fabricating a curve.

### 5.2 Features

- Ambient temperature (from `Cook.ambientTempC`, or richer sources — see
  `data-model.md` open question 9).
- Wind — a large real effect on an offset, and **currently not captured
  anywhere in the data model**. Either the user logs it, or we fetch weather by
  location and time (a privacy decision), or Level 2 ships without it.
- Equipment `insulation` and `fireboxVolumeL`, which are exactly why these
  fields exist on Equipment.

### 5.3 Data density problem

Manual logging yields a pit temp every 20–60 minutes. Fitting a rise/peak/decay
curve to 2–4 points per fuel event is optimistic. Level 2 will likely need to
fit **pooled across many fuel events** of the same class rather than per-event,
and to be honest about confidence when it cannot. This is the reason Level 3
exists.

## 6. Level 3 (later) — continuous probe data

Same `IFirePredictor`, same notification loop, much better input: pit temp every
30 seconds from a FireBoard (API or CSV import) or MEATER (cloud API).

Because the interface is shared, Level 3 is a data-source change plus a better
curve fit — not a new feature. Ingested readings become `TempEntry` rows with
`source = Probe` or `Import` (`data-model.md` §6), so charts, analytics, stall
detection and the fire model all consume them without special cases.

Volume is the design constraint: a 14-hour cook at 30-second intervals is ~1,700
readings, against maybe 20 manual entries. Storage, sync batching, chart
downsampling, and curve fitting all need to tolerate that, which is why
`(cookId, recordedAt)` indexing and time-range paging are specified from day one.

## 7. Where the model runs

**Level 1 runs on device.** It must work with no connectivity — a smoker is
often out of WiFi range — and it must schedule local notifications. The cadence
estimator is a median over the user's local SQLite data; this is cheap.

**Level 2's curve fitting probably belongs server-side**, computed at sync and
delivered to the device as a compact `BurnCadenceProfile` the on-device
predictor consumes. That keeps the device path offline-capable (it applies a
profile rather than fitting one) while the expensive fitting happens where the
full history lives.

This split is a proposal, not a settled decision — see open questions.

## 8. Evaluation

A fire model that cannot be measured cannot be improved. From Level 1 onward:

- **Prediction error** — signed minutes between `scheduledFor` and the actual
  next `FuelEvent`. Track median and spread, per equipment.
- **Systematic bias** — is the model consistently early or late? Early is the
  safer failure and should be the deliberate bias.
- **Response mix** — the ratio of Added log / Still fine / Snooze / Ignored is
  the fastest read on whether the feature is helping or nagging.
- **Level comparison** — with `predictorVersion` recorded per prompt, Level 2
  can be evaluated against Level 1 on the same user's later cooks.

---

## Open questions

1. **The self-reinforcing feedback loop (§4.2) is not solved, only flagged.**
   `viaNotification` lets us detect and down-weight prompt-driven events, but the
   right weighting is unknown. Should Level 1 exclude prompt-driven events from
   cadence learning entirely until Level 2 provides independent evidence?
2. **Minimum interval counts in §3.2 are invented.** 5 / 5 / 4 / 4 are plausible
   starting numbers, not derived ones. They need tuning against real data, and
   the first real data will be yours.
3. **The 4-hour plausibility ceiling (§3.4) will discard genuine long intervals**
   on a well-insulated kamado or a low overnight burn. Should the ceiling scale
   with equipment type and insulation instead of being a constant?
4. **Wind is a significant feature with no home in the data model.** Log it,
   fetch it (privacy cost — requires location), or ship Level 2 without it.
5. **Where does Level 2 run?** §7 proposes server-side fitting with an on-device
   profile. That means Level 2 quality depends on having synced recently, which
   sits awkwardly with offline-first. The alternative is on-device fitting with a
   simpler model.
6. **Notification reliability is unverified.** iOS's 64 pending-notification cap,
   background-response handling without launching the app, and Android's
   aggressive OEM battery optimizations all threaten the delivery mechanism this
   entire feature rides on. This needs a platform spike before the fire model
   slice — if quick responses can't reliably write data in the background, the
   whole interaction design changes.
7. **Fire model is Pro-gated (`product-spec.md` §5) but learns from free users'
   data.** A free user logging fuel events is training a model they cannot use.
   That is defensible, but it should be a deliberate decision — and it affects
   whether a free user is even prompted for notification permission.
8. **Snooze increment (15 min) and "sustained below envelope" thresholds are
   placeholders.** Both need real cooks to calibrate.
9. **Multiple concurrent cooks on one rig, or one cook across two rigs**, are not
   modelled at all. The predictor assumes one active cook per equipment. Probably
   fine for v1; worth confirming it is an accepted limitation rather than an
   oversight.
