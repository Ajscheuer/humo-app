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
    IReadOnlyList<Cook> ActiveCooks,                // may be more than one per rig
    double TotalLoadKg,                             // combined thermal mass in the chamber
    IReadOnlyList<FuelEvent> FuelEventsThisFire,    // scoped to the rig, not a cook
    IReadOnlyList<TempEntry> TempEntriesThisFire,   // ignored by Level 1
    IReadOnlyList<FireCheckPrompt> PromptsThisFire,
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
- **The context is scoped to the fire, not to a cook**, and carries the combined
  load. See §3.0 and §3.6.

### 2.1 Thermal load is a first-class input

Meat is a heat sink. A chamber holding 20 kg of cold brisket absorbs far more
energy than one holding 5 kg, and it shows up in exactly the behaviour this model
predicts:

- **The rise after a fuel load is shallower** — the same split raises the pit
  less, because more of its energy goes into the meat.
- **Recovery takes longer**, so a naive predictor calibrated on light loads will
  call for fuel too late on a heavy one.
- **Decay is slower too**, because the loaded chamber holds more energy once it
  is up to temperature.

`TotalLoadKg` is therefore the sum of `weightKg` across **every cook active on
that rig**, not just the cook being viewed. This is the second reason fuel and
fire are modelled per-rig rather than per-cook: a predictor that only knew about
one of two briskets would systematically underestimate the load it is heating.

Load also changes *during* a cook — meat loses moisture and mass over 14 hours,
and cooks are sometimes added mid-session — but v1 treats load as fixed at each
cook's start weight. Refining that is not worth the complexity until the fitted
curves show it matters.

## 3. Level 1 (MVP) — learned burn cadence

### 3.0 The fire belongs to the rig, not to the cook

A firebox does not know how many pieces of meat are above it. Cooks routinely run
a brisket and a rack of ribs in the same smoker, and both are fed by one fire.

So **`FuelEvent` is scoped to equipment, not to a cook** (`data-model.md` §3.4).
Every cook active on that rig at that time shows the same fuel events on its
timeline, and the fire model learns from one series per rig rather than one per
cook. Modelling each cook independently would make the predictor see every fuel
load twice and predict roughly twice as often as the fire actually needs.

This keeps fuel logging at ≤2 taps even with two cooks running: there is no
"which cook is this for?" question, because the answer is "the fire".

It also means the model can see the **combined thermal load** on the rig (§2.1),
which it could not do if it only knew about one of two briskets.

### 3.6 What Level 1 does and does not do with load

Level 1 is a median of clock intervals. It does **not** condition on load —
grouping cadence by weight as well as by equipment, wood type and size class
would fragment a small dataset into bins with one sample each, and the fallback
ladder would collapse to "no prediction" for almost everyone.

The consequence is a known, documented bias: **Level 1 will call for fuel
slightly late on an unusually heavy load and slightly early on a light one**,
because it is predicting from a cadence learned across all of that rig's cooks.
Level 2 is where load stops being averaged away and becomes a fitted feature.

Level 1 still *records* `TotalLoadKg` on every prediction, so that when Level 2
arrives there is a history to fit against rather than a cold start.

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

- **Intervals bounded by a prompt-driven event.** A `FuelEvent` created from an
  "Added log" quick response (`viaNotification = true`) does **not** feed Level 1
  cadence learning. See §3.5 — this is the single most important exclusion.
- Intervals spanning a period when no cook was active on that rig — the fire was
  out between cooks, not burning slowly.
- Intervals containing the first fuel event of a fire, where the firebox was
  loaded at startup rather than replenished.
- Intervals bounded by a superseded or tombstoned event (`data-model.md` §5.3).
- Intervals bounded by a record flagged for clock skew (`architecture.md` §5) —
  a wrong device clock corrupts cadence far more than it corrupts sync ordering.
- Intervals longer than a plausibility ceiling (proposed: 4 hours) — these are
  almost always "the user stopped logging", not "the fire burned for 4 hours".

That last exclusion is a heuristic and will silently discard genuine long
intervals on a well-insulated kamado. Flagged below.

### 3.5 Not learning from our own predictions

Level 1 has an obvious way to fool itself. If it predicts 45 minutes, prompts the
user at 45 minutes, and the user obligingly adds a log, then it learns that the
cadence is 45 minutes — regardless of what the fire wanted. Every prediction
appears confirmed, and the model's error becomes invisible.

**Level 1 therefore learns cadence only from spontaneous fuel events.** Events
created via a notification response are recorded in full, appear on the cook's
timeline, and count as evidence about the *prompt* — but they are excluded from
the median that produces the next prediction.

The cost is real: the model learns more slowly, because the better it gets, the
more of a user's fuel events are prompt-driven and therefore ignored. This is
accepted for Level 1 because the alternative — weighting prompt-driven events by
some factor — means tuning a number that decides whether the model tracks the
fire or tracks itself, with no ground truth to tune it against.

**Level 2 is what makes prompt-driven events usable.** Once pit temperature
provides independent evidence of whether the fire actually needed fuel, a
confirmed prompt stops being circular and can be learned from.

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
| `totalLoadKg` | `double` | Combined load on the rig when the prediction was made. Recorded by Level 1 even though it does not use it, so Level 2 has history to fit against. |

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

- **Total load in the chamber** (§2.1) — the strongest of these after the fuel
  itself. A heavy load flattens the rise and lengthens the recovery, and Level 2
  is where that becomes measurable rather than assumed. Fit the curve *per unit
  of load* where there is enough data, rather than treating a 20 kg cook and a
  5 kg cook as the same event.
- **Ambient temperature**, now available as a time series from `TempEntry`
  (`data-model.md` §3.3) rather than a single value at the start.
- Equipment `insulation`, `fireboxVolumeL` and `cookChamberVolumeL` — which is
  exactly why those fields exist on `Equipment`. Chamber volume and load
  together approximate the thermal mass the fire is fighting.
- **Wind is deferred.** It is a large real effect on an offset, but it has no
  home in the data model and would need either a manual field or location-based
  weather. Revisit if the fitted curves leave unexplained variance that
  correlates with nothing else.

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

**Level 2's curve fitting runs server-side**, computed at sync and delivered to
the device as a compact `BurnCadenceProfile` that the on-device predictor
consumes. The device *applies* a profile rather than fitting one, so prediction
stays fully offline-capable while the expensive fitting happens where the full
history lives.

The accepted cost: **Level 2 quality depends on having synced recently.** A user
who has been offline for weeks gets a stale profile, and a guest user gets none
at all. Level 2 degrades to Level 1's answer in both cases rather than failing —
which is why both levels implement the same interface.

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

## Decisions

Settled 2026-08-30.

| # | Decision | Rationale |
|---|---|---|
| 1 | Level 1 learns cadence **only from spontaneous fuel events**; prompt-driven ones are excluded | Otherwise the model converges on its own prediction and its error becomes invisible. Slower learning is the accepted cost; Level 2's temperature evidence is what makes prompt-driven events usable later. |
| 2 | **Fuel events are scoped to equipment, not to a cook** | A firebox does not know how many pieces of meat are above it. Multiple cooks on one rig share one fire and one cadence; modelling them separately would double-count every fuel load. |
| 2b | **Thermal load is a first-class input** — `TotalLoadKg`, summed across all active cooks on the rig | Meat is a heat sink: more protein flattens the rise and lengthens the recovery after a fuel load. Level 1 records it but does not condition on it; Level 2 fits against it. |
| 3 | Level 2 **fits server-side, applies on device** | Prediction stays offline-capable by applying a compact profile rather than fitting one. Cost: profile freshness depends on syncing, and guests get none — both degrade to Level 1 rather than failing. |
| 4 | **Wind is deferred** until Level 2 shows it is needed | Ambient now rides on `TempEntry` for free; wind would need either a manual field or location-based weather, and neither is worth its cost on speculation. |
| 5 | Clock-skew-flagged records are **excluded from learned intervals** | A wrong device clock corrupts cadence far more damagingly than it corrupts sync ordering. |

## Open questions

1. **Is a free user's logging meant to train a model they cannot use?** The fire
   model is Pro-gated but every fuel event a free user logs is training data.
   Per-user-only training makes this defensible — nobody's data trains anyone
   else's model — but it should be a deliberate decision, and it affects whether
   a free user is even asked for notification permission.
2. **Minimum interval counts in §3.2 are invented.** 5 / 5 / 4 / 4 are plausible
   starting numbers, not derived ones. They need calibrating against real data,
   and the first real data will be yours.
3. **The 4-hour plausibility ceiling (§3.4) will discard genuine long intervals**
   on a well-insulated kamado or a low overnight burn. Should the ceiling scale
   with equipment type and insulation instead of being a constant?
4. **Snooze increment (15 min) and "sustained below envelope" thresholds are
   placeholders.** Both need real cooks to calibrate.
5. **Pit temperature is now ambiguous when two cooks share a rig.** A `TempEntry`
   couples `meatTempC` (per cook) with `pitTempC` (per fire), so two concurrent
   cooks can record conflicting pit temps for the same fire at the same moment.
   Fuel events moved to the equipment; pit temperature arguably should too.
   Unresolved — it affects the stability score and Level 2's envelope.
6. **Notification reliability is unverified.** iOS's 64 pending-notification cap,
   background-response handling without launching the app, and Android's
   aggressive OEM battery optimizations all threaten the delivery mechanism this
   entire feature rides on. **Spike before the fire model slice** — if quick
   responses cannot reliably write data in the background, the whole interaction
   design changes.
7. **Excluding prompt-driven events has an unmeasured cost.** The better the
   model gets, the more events become prompt-driven and therefore ignored, which
   could stall learning entirely for a well-served user. Worth measuring once
   there is real data: what fraction of events remain spontaneous?
8. **Load changes during a cook and v1 ignores that.** Meat loses moisture and
   mass over 14 hours, and cooks are sometimes added to a running rig. `TotalLoadKg`
   is fixed at each cook's start weight. Refine only if the fitted curves show
   the drift matters.
9. **Cross-cook analytics are distorted by shared rigs.** A brisket cooked
   alongside ribs is fighting for the same heat as one cooked alone, so its
   time-per-kg is not comparable to a solo cook's — yet both land in the same
   baseline for anomaly detection. Either the baseline should be conditioned on
   solo-versus-shared, or shared cooks should be flagged and excluded. Affects
   `product-spec.md` §6 as much as this document.
