# Humo — Product Specification

**Status:** Draft v0.1 · **Last updated:** 2026-08-30

---

## 1. What Humo is

Humo is a cross-platform (iOS + Android) mobile app for serious BBQ cooks to log
their cooks and learn from them over time. A "cook" is a single session — one
brisket on one smoker on one day — and the app captures what actually happened
during it: temperatures, fuel added, and the milestones (wrapped, spritzed,
rested).

The app is deliberately **manual-entry first**. It does not require a WiFi
thermometer, a probe subscription, or any hardware beyond the phone already in
the cook's pocket. Entries are cheap to record while standing at the smoker with
one greasy hand.

The payoff for that logging discipline is threefold:

1. **Cross-cook analytics** — how long *your* briskets actually take per kg on
   *your* offset, how long your stalls run, how stable your pit is.
2. **Anomaly detection** — flagging a cook that is drifting away from your own
   historical baseline while there is still time to react.
3. **Fire management** — predicting when the fire will next need fuel, and
   telling you before the pit temp drops, not after.

The third is the differentiator. Everything else in this category is a logbook;
Humo is a logbook that learns your fire.

## 2. Who it is for

**Primary persona — the long-cook enthusiast.** Owns an offset smoker or a WSM,
cooks 1–4 times a month, runs 8–16 hour cooks, cares about repeatability, and
already keeps notes in a notebook or the Notes app. Frustrated that those notes
never turn into insight. Thinks in °F, pounds, and splits.

**Secondary persona — the asado cook.** Cooks over live fire on a parrilla,
manages a brasero, works in °C and kilos, and cares less about a target internal
temp than about coal management and timing across many cuts. Supported later as
an **equipment type within the same app**, not a separate product.

**Explicitly not the target (for now):** competition teams needing multi-cook
scheduling and team coordination; restaurants and commercial pitmasters needing
HACCP-grade logging; casual grillers cooking burgers for 20 minutes.

## 3. Platform and market positioning

- **Geography first:** United States long-cook BBQ. Brisket, pork butt, ribs;
  offset smokers, kettles, kamados, WSMs, pellet grills.
- **Geography second:** Argentina / Southern Cone asado. Parrilla as an
  equipment type, wood and coal management as the fuel model.
- **Language:** English and Spanish, both at launch. See §7.

The app is designed so that adding the asado audience is a data and content
change (a new equipment type, new fuel forms, new cut names) rather than a fork
of the product.

## 4. Core user flows

### 4.1 Set up equipment (one time, editable)

The user creates one or more equipment profiles: a name, a type, optional
firebox and cook-chamber volumes, an insulation rating, and notes. Equipment is
the unit that the fire model learns against — a burn cadence learned on a
275-gallon offset means nothing on a kamado.

### 4.2 Start a cook

Pick equipment, pick a meat type, enter weight, optionally a target internal
temperature and ambient temperature. The cook starts and the app moves into the
**active cook screen**.

**Meat type is a closed enum with an "Other" free-text escape hatch.** Analytics
group by meat type and the UI is bilingual, and free text can be neither grouped
nor translated — "brisket", "Brisket" and "packer brisket" are three groups, and
a Spanish user would see whatever an English user typed. Cooks logged as "Other"
are excluded from cross-cook grouping, which is the honest cost of the escape
hatch. The same applies to wood type.

**Target internal temperature is optional.** A parrilla cook working by feel has
no target temp, and forcing a number would put meaningless data in every asado
cook. One nullable field and one cook-creation form; the form prompts for it
prominently on smoker equipment types and omits it for parrilla, which is a UI
decision rather than a schema fork.

### 4.3 During the cook (the screen that matters)

The active cook screen is the app's centre of gravity. It must be usable
one-handed, outdoors, in daylight and at 3am, with cold or greasy hands. It
shows the current state (elapsed time, last meat temp, last pit temp, time since
last fuel, next predicted fire check) and offers four actions:

- **Log temp** — meat temp, optionally pit temp, optional note.
- **Log fuel** — see §4.4; must be ≤2 taps.
- **Log event** — wrapped / spritzed / rested / other.
- **Finish cook** — records `finishedAt`, prompts for a rating and notes.

The rating is **1–5 stars on the result** — "how did it turn out?" — not on how
well the cook was executed. Result is what makes the rating useful to analytics:
correlating technique against outcome is the entire point, whereas self-assessed
execution tracks how the cook *felt*, which is a much weaker signal. Five points
is as much resolution as anyone applies honestly.

### 4.4 Fuel logging in ≤2 taps

This is a hard interaction requirement, not a nice-to-have. A cook adding a
split to the firebox is standing at an open firebox door with a glove on.

The design that satisfies it: the fuel button on the active cook screen opens a
sheet whose **wood type and fuel form are pre-filled from the last fuel event of
this cook** (or, for the first event of a cook, from the last cook on the same
equipment). What remains is a single size-class tap — small / medium / large —
which commits the event immediately with `count = 1`.

- Tap 1: "Add fuel".
- Tap 2: size class → **saved**.

Count > 1 and weight are available on the same sheet but never required. Editing
wood type or form is a deliberate extra interaction, not on the fast path.

### 4.5 Fire check notifications

The app schedules a local notification predicting when fuel will next be needed
(see `fire-model.md`). The notification carries quick responses — **Added log**,
**Still fine**, **Snooze** — and every response is training data. "Added log"
creates a `FuelEvent` without the user opening the app at all.

**The feature degrades rather than disappears without notification permission.**
The active cook screen always shows a live countdown to the next predicted fire
check, whether or not notifications are allowed. Permission is requested **once,
at the moment the first prediction is ready** — not at launch, where the request
has no context and gets denied reflexively. If denied, Humo does not nag; the
countdown carries the feature, and the setting stays reachable.

**Framing: this is a reminder, not a safety device.** Store copy and in-app text
describe fire checks as a prompt to go look at your fire, never as monitoring,
supervision, or a safety guarantee. The distinction is stated once, in-app, when
fire checks are first enabled, and again in the terms — not on every
notification, where a repeated warning would be tuned out within a week and
would bloat a message whose entire value is being glanceable.

### 4.6 Photos

Photos ship in v1. A photo belongs to a cook and can optionally be **pinned to a
moment within it** — the bark at the wrap, the fire at 2am, the slice at the end
— so the cook log reads as a timeline rather than an undifferentiated gallery.

**Free users keep photos on device; Pro users get them synced and backed up.**
Sync is the part that actually costs money, so sync is the part that is paid.
The trade-off to watch: "your photos didn't sync" needs clear in-app language,
or it reads as a bug rather than a tier boundary.

### 4.7 After the cook

The cook summary shows a temperature chart (meat and pit over time, with fuel
events and milestones marked), computed statistics, and — for Pro users —
comparison against the user's own baseline for that meat type and equipment.

## 5. Feature tiers

| Capability | Free | Pro |
|---|---|---|
| Equipment profiles | ✅ unlimited | ✅ unlimited |
| Logging a cook (temps, fuel, events) | ✅ | ✅ |
| Cook history | ⚠️ 5 most recent cooks | ✅ unlimited |
| Per-cook chart and summary | ✅ | ✅ |
| Photos | ✅ on device only | ✅ synced and backed up |
| Cross-cook analytics (§6) | ❌ | ✅ |
| Anomaly detection | ❌ | ✅ |
| Fire model Level 1 (learned cadence) | ❌ | ✅ |
| Fire model Level 2 (temp-informed) | ❌ | ✅ |
| Probe integrations (Level 3, later) | ❌ | ✅ |
| AI cook coach (later phase) | ❌ | ✅ |

### 5.1 The free history limit

Free users keep their **5 most recent cooks**. Older cooks stay **visible but
locked** — listed, dated, and named, with their contents behind an upgrade
prompt. They are never hidden and never appear deleted: an empty list reads as
data loss, a locked list reads as an offer.

**The server retains everything regardless of tier.** The limit gates
*visibility*, not *retention*. A user who upgrades gets their real history and
real anomaly baselines immediately, rather than paying for an empty analytics
screen. The consequence is that Humo holds cook data a free user can no longer
see, which **must be stated plainly in the privacy policy** — it is exactly the
kind of thing users are right to be annoyed about discovering later.

The number 5 lives as a **server-side policy value**, not a client constant, so
changing it is configuration rather than an app release.

### 5.2 Accounts and subscriptions

**Subscribing requires an account.** The paywall prompts account creation before
purchase. RevenueCat can attach an entitlement to an anonymous device ID, but
that user loses their subscription on a new phone and is then owed a refund;
requiring an account at the moment money changes hands avoids the whole class of
problem and matches what users expect.

Guest use is otherwise unrestricted — a user can log cooks indefinitely without
an account, they simply cannot subscribe or sync.

**When a guest creates or signs into an account, Humo asks once** whether to add
their existing local cooks to that account, with "yes" preselected. It is not
silent: a guest signing into an existing account on a borrowed or shared phone
would otherwise absorb someone else's cooks into their history irreversibly.

Subscriptions are sold through **RevenueCat**. Entitlements are **checked
server-side** — the client may cache an entitlement state for offline use, but
the server is authoritative for anything the server computes (analytics, AI
proxying, sync retention).

**Offline note:** because analytics are computed server-side, a Pro user with no
connectivity sees their cached analytics, not fresh ones. Logging always works
offline; insight may lag. This is an accepted trade-off, called out here so it is
not discovered as a bug later.

## 6. Analytics (Pro)

All analytics are **computed server-side and cached per cook at sync time**, so
the client renders precomputed values rather than recomputing across history on
device.

- **Time per kg by meat type** — trend over time, so the user can see whether
  their briskets are getting faster or slower on a given rig.
- **Stall duration** — detected as the longest interval in which meat temp rises
  less than a threshold rate, within the plateau band. Reported per cook and
  trended.
- **Pit temp stability score** — dispersion of pit temp around its own median
  for the cook, normalized so that a higher score is a steadier fire. Sliceable
  by fuel strategy (wood type / form / size class mix).
- **Fuel efficiency** — fuel events per hour, normalized by average pit temp and
  ambient temp, so a cold windy day does not look like poor technique.
- **Anomaly flags** — a cook is flagged when a metric sits beyond ±2σ from the
  **user's own baseline** for that (meat type, equipment) pair. Never against a
  global population baseline; the whole point is "unusual *for you*".

Baselines need history to exist. **Anomaly flags require at least 8 cooks** for
the (meat type, equipment) pair being compared. Below that the UI says how many
more cooks are needed and shows no flags at all — a false "this cook is unusual"
alarm on a baseline of three is worse than silence, because it teaches the user
to ignore the feature permanently.

Eight is a judgement call, not a derived number: it is roughly where ±2σ stops
being dominated by sampling noise, while still being reachable within a season
for a monthly cook. It should be revisited against real data.

## 7. Localization

**English and Spanish are both launch languages.** Spanish is not a phase two.

- Resources are `.resx`-based: `AppResources.resx` (English, neutral fallback)
  and `AppResources.es.resx` (Spanish).
- Spanish is authored **neutral `es`**, flavored toward `es-AR` vocabulary where
  a choice must be made. There is no separate `es-AR` resource file unless and
  until a real divergence forces one.
- Culture follows the device setting, with an **in-app language override**
  persisted in preferences.
- Dates and numbers respect culture.
- **Temperature unit (°C/°F) is a separate user setting, independent of
  language.** An American cook whose phone is in Spanish still thinks in °F. A
  new user's default unit is seeded from region, then owned entirely by the
  user.
- Layouts must tolerate **~25% text expansion** for Spanish. No fixed-width
  containers around labels; no truncation-by-design; buttons size to content.

### 7.1 Spanish glossary — BBQ domain terms

> ⚠️ **Every translation below is a placeholder and must be reviewed.** These are
> proposals from an English-speaking model, flavored toward Argentine usage
> where possible. BBQ vocabulary is dialect-specific and much of American
> low-and-slow terminology has no settled Spanish equivalent — some terms may
> genuinely be best left in English, as loanwords are common among asadores who
> follow American BBQ. **Do not treat any line here as final.**

| English | Proposed Spanish | Notes |
|---|---|---|
| Cook (noun, the session) | `la cocción` — *TODO: confirm* | "el asado" means the event socially; "cocción" is more clinical. Which reads better in-app? |
| Cook (person) | `el asador` / `la asadora` — *TODO: confirm* | Gendered; check UI strings avoid assuming. |
| Smoker | `el ahumador` — *TODO: confirm* | |
| Offset smoker | `el ahumador de desplazamiento` — *TODO: confirm* | Clunky. "Offset" may be better kept in English. |
| Kettle | `la parrilla tipo kettle` — *TODO: confirm* | Weber-branded in practice. |
| Kamado | `el kamado` — *TODO: confirm* | Likely unchanged. |
| WSM (bullet smoker) | `el ahumador vertical` — *TODO: confirm* | |
| Pellet grill | `la parrilla de pellets` — *TODO: confirm* | |
| Parrilla | `la parrilla` | Native term; no translation needed. |
| Firebox | `la cámara de fuego` — *TODO: confirm* | Compare: "el hogar", "el fogón". |
| Cook chamber | `la cámara de cocción` — *TODO: confirm* | |
| Brasero (coal basket) | `el brasero` | Native term. |
| Embers / coals | `las brasas` | Native term. Central to asado. |
| Firewood | `la leña` | Native term. |
| Split (of wood) | `el leño` / `la astilla` — *TODO: confirm* | "Leño" = log; a *split* is specifically a quartered log. May need a phrase. |
| Chunk | `el trozo de leña` — *TODO: confirm* | |
| Charcoal | `el carbón` | |
| Lump charcoal | `el carbón de leña` — *TODO: confirm* | vs. briquettes below. |
| Briquettes | `las briquetas` — *TODO: confirm* | |
| Pellets | `los pellets` — *TODO: confirm* | Loanword in practice. |
| Fuel event | `la carga de combustible` — *TODO: confirm* | "Carga" (load) may read better than "combustible". |
| Pit temperature | `la temperatura de la cámara` — *TODO: confirm* | |
| Internal temperature | `la temperatura interna` | |
| Ambient temperature | `la temperatura ambiente` | |
| Target temperature | `la temperatura objetivo` — *TODO: confirm* | |
| Probe | `la sonda` — *TODO: confirm* | |
| Grate | `la parrilla` (the grill surface) — *TODO: confirm* | Collides with "parrilla" the equipment. Disambiguate. |
| Vents / dampers | `los reguladores de aire` — *TODO: confirm* | |
| The stall | `el estancamiento` — *TODO: confirm* | **Most uncertain term in this table.** No settled Spanish equivalent; "la meseta" (plateau) is another candidate. May be best left as "el stall". |
| Wrap (verb) | `envolver` — *TODO: confirm* | |
| The Texas crutch | `el método Texas crutch` — *TODO: confirm* | Probably untranslatable; keep English. |
| Bark | `la corteza` — *TODO: confirm* | Literally "bark/crust"; check it isn't read as tree bark. |
| Smoke ring | `el anillo de humo` — *TODO: confirm* | |
| Rest (verb / noun) | `reposar` / `el reposo` — *TODO: confirm* | |
| Spritz (verb) | `rociar` — *TODO: confirm* | |
| Rub | `el aderezo seco` — *TODO: confirm* | "Rub" often kept in English. |
| Trim (verb) | `recortar` — *TODO: confirm* | |
| Thin blue smoke | `humo azul fino` — *TODO: confirm* | Jargon; may need explanation, not translation. |
| Low and slow | `baja y lenta` — *TODO: confirm* | Idiom; may not carry. |
| Reverse sear | `el sellado inverso` — *TODO: confirm* | |
| Brisket | `el brisket` / `la tapa de asado` — *TODO: confirm* | **Cut geometry differs between US and Argentine butchery.** Argentine "tapa de asado" is not the same cut. Needs a real butcher's answer. |
| Point / flat (brisket) | `la punta` / `la parte plana` — *TODO: confirm* | Depends on brisket answer above. |
| Pork butt / Boston butt | `la paleta de cerdo` — *TODO: confirm* | |
| Ribs (pork) | `el costillar de cerdo` — *TODO: confirm* | |
| Ribs (beef) | `el costillar` / `la tira de asado` — *TODO: confirm* | Cut differs; "tira de asado" is a cross-cut style. |
| Chicken | `el pollo` | |
| Fire check | `el control del fuego` — *TODO: confirm* | Notification title; needs to be short. |
| Add a log | `agregar leña` — *TODO: confirm* | Notification quick-response; must be very short. |
| Still fine | `todo bien` — *TODO: confirm* | Notification quick-response. |
| Snooze | `posponer` — *TODO: confirm* | |

Terms marked *TODO: confirm* stay marked until you replace them. The
`AppResources.es.resx` values should carry the reviewed strings, not these.

## 8. Non-goals for v1

- Social features, sharing, feeds, following other cooks.
- Recipe management or step-by-step cooking instructions.
- Grocery/shopping, inventory of wood or meat.
- Multi-user or team cooks.
- Web or desktop clients.
- Continuous probe hardware integrations (this is fire model Level 3, later).

---

## Decisions

Settled 2026-08-30. Recorded so they are not silently relitigated; each can be
reopened deliberately.

| # | Decision | Rationale |
|---|---|---|
| 1 | Free tier = **5 most recent cooks**, visible but locked | Easy to explain and enforce; locked-but-visible converts where an empty list reads as a bug. Stored as a server-side policy value, so the number is configuration. |
| 2 | **Retain all data server-side; gate visibility only** | Upgrading must reveal real history and real baselines, not an empty screen. Requires an explicit privacy-policy statement. |
| 3 | Anomaly flags need **≥8 cooks** per (meat type, equipment) | ±2σ below that is sampling noise, and one false alarm permanently costs trust. Revisit against real data. |
| 4 | `meatType` and `woodType` are **enums + "Other" free text** | Analytics must group and the UI must translate; free text does neither. Other-typed cooks are excluded from grouping. |
| 5a | **An account is required to subscribe** | Device-bound entitlements strand users on a new phone and generate refunds. |
| 5b | Guest → account **asks once, defaults to merge** | Silent merge would absorb someone else's cooks when signing into an existing account on a shared phone. |
| 6 | `rating` is **1–5 stars on the result** | Outcome is the useful analytics signal; self-assessed execution tracks how the cook felt. |
| 7 | `targetInternalTemp` is **optional for everyone** | One field, one form. Parrilla cooks have no target temp; the form varies, the schema does not. |
| 8 | Notification denial **degrades to an in-app countdown**; permission asked once, in context | The feature must survive a reflexive denial at launch. |
| 9 | Fire checks are framed as a **reminder, not a safety device** — stated once in-app and in the terms | Repeated warnings get tuned out and bloat a glanceable notification. |

## Open questions

1. **The Spanish glossary in §7.1 is entirely unreviewed.** Every term is still
   marked `TODO: confirm`. The highest-risk entries are *the stall* (no settled
   Spanish equivalent) and *brisket* (US and Argentine butchery cut differently,
   so "tapa de asado" may not be the same meat at all). This is yours to correct.
2. **Privacy policy and terms do not exist yet.** Decisions 2 and 9 both create
   text that has to live in them, and both app stores require a privacy policy
   before submission.
3. **The paywall's timing and content are unspecified.** Decision 5a says an
   account is required to subscribe, but not when a free user first meets the
   paywall — at the 6th cook, on opening analytics, or on a trial expiry. This
   shapes conversion more than the tier definition does.
4. **No trial period is defined.** Free-with-limits and a time-limited trial of
   Pro are different products; RevenueCat supports either.
