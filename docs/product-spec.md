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

Pick equipment, pick a meat type, enter weight and a target internal
temperature, optionally record ambient temperature. The cook starts and the app
moves into the **active cook screen**.

### 4.3 During the cook (the screen that matters)

The active cook screen is the app's centre of gravity. It must be usable
one-handed, outdoors, in daylight and at 3am, with cold or greasy hands. It
shows the current state (elapsed time, last meat temp, last pit temp, time since
last fuel, next predicted fire check) and offers four actions:

- **Log temp** — meat temp, optionally pit temp, optional note.
- **Log fuel** — see §4.4; must be ≤2 taps.
- **Log event** — wrapped / spritzed / rested / other.
- **Finish cook** — records `finishedAt`, prompts for a rating and notes.

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

### 4.6 After the cook

The cook summary shows a temperature chart (meat and pit over time, with fuel
events and milestones marked), computed statistics, and — for Pro users —
comparison against the user's own baseline for that meat type and equipment.

## 5. Feature tiers

| Capability | Free | Pro |
|---|---|---|
| Equipment profiles | ✅ unlimited | ✅ unlimited |
| Logging a cook (temps, fuel, events) | ✅ | ✅ |
| Cook history | ⚠️ limited — **policy TBD, see open questions** | ✅ unlimited |
| Per-cook chart and summary | ✅ | ✅ |
| Cross-cook analytics (§6) | ❌ | ✅ |
| Anomaly detection | ❌ | ✅ |
| Fire model Level 1 (learned cadence) | ❌ | ✅ |
| Fire model Level 2 (temp-informed) | ❌ | ✅ |
| Probe integrations (Level 3, later) | ❌ | ✅ |
| AI cook coach (later phase) | ❌ | ✅ |

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

Baselines need history to exist. A user with two cooks gets no anomaly
detection, and the UI must say so plainly rather than showing a meaningless
flag. Minimum sample size is an open question below.

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

## Open questions

1. **Free tier limit is undefined.** "Limited cook history" needs a concrete
   rule — last N cooks, a rolling time window, or unlimited logging with Pro
   gating analytics only. You said you'd decide later; until then nothing in the
   entitlement design hard-codes a number, and the limit must be expressed as a
   server-side policy value rather than a client constant.
2. **Does the free tier limit *display* or *retention*?** These are very
   different. If the server deletes cooks beyond the free limit, a user who
   later upgrades has no history to analyse and their anomaly baselines are
   permanently poorer. Recommendation: retain everything server-side, gate
   *visibility*. This has a privacy consequence (holding data users may think is
   gone) that should be stated in the privacy policy.
3. **Anomaly detection minimum sample size.** ±2σ against a baseline of three
   cooks is noise. What is the minimum cook count per (meat type, equipment)
   before flags appear — 5? 8? — and what does the UI show below that threshold?
4. **Is `meatType` a free-text field or a closed enum?** The brief lists it as a
   plain field, but analytics group by meat type and the UI must display it in
   two languages. Free text cannot be localized or grouped reliably ("brisket",
   "Brisket", "packer brisket", "tapa de asado" are four groups). Recommendation:
   closed enum with localized display names plus an "other + free text" escape
   hatch. Same question applies to `woodType`.
5. **Guest mode and account upgrade.** You chose: prompt for Apple / Google /
   email account at first launch, with anonymous device ID for users who decline.
   Two things follow that need answers — (a) does a guest user get Pro at all
   (RevenueCat can attach a subscription to an anonymous ID, but the user loses
   it on device change), and (b) when a guest later creates an account, do we
   merge their local cooks into the new account automatically, or ask?
6. **Rating scale on `Cook.rating` is unspecified.** 1–5 stars? 1–10? What is it
   rating — the food, or the cook's execution? These give different analytics.
7. **Asado cooks may not have a target internal temp.** `Cook.targetInternalTemp`
   is modelled as required, but a parrilla cook working by feel and time has no
   target temp. Should it be optional, or does the parrilla equipment type
   change the cook-creation form?
8. **Notification permission is a hard dependency for the headline feature.** If
   a user denies notification permission, the fire model's entire delivery
   mechanism is gone. What does the app do — degrade to in-app-only reminders,
   or re-prompt with an explanation?
9. **App Store positioning of a "fire alert" feature.** Predicting when a fire
   needs attention is adjacent to a safety claim. We should decide early whether
   the copy is explicitly non-safety ("a reminder to check, not a safety
   device") and where that disclaimer lives.
