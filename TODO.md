# Routier — Roadmap

> **Tagline:** Routier adds a trading layer to SailWind — market intel, agent
> contracts, and a real economic pulse to the archipelago.

**Version strategy:** ship incremental **0.6–0.9** builds while working toward a
polished **1.0** release. **1.1** adds the route charter; **1.2** adds contraband
runs; **2.0** is the full agent storyline (Discord poll: players want narrative
progression).

---

## Shipped (v0.1 – v0.5)

| Area | Done |
|------|------|
| **Market** | Hourly snapshots → SQLite; goods/ports catalog; currency & reputation |
| **Sim** | Python bulk-pricing model + route optimizer; C# port 1:1; web parity harness |
| **Routes** | Daily generation at hub ports (local + regional); per-save RNG seed |
| **Parchment** | Manifest-style pages (summary + per-port receipts); save-backed scroll item |
| **UI** | Route agent kiosk (placeholder cube); Canvas offer picker with detail + buy |
| **Web** | Dashboard: market lookup, charts, route planner, simulator |
| **Repo** | `src/` layout, GitHub, Thunderstore manifest, MIT license |

---

## Path to 1.0 — Release milestone

**1.0 goal:** a complete, shippable trading-contract experience — polished parchment,
real hub agent, save-safe data — then publish (Thunderstore + GitHub release).

```mermaid
flowchart LR
  A["0.6 Save-bound DB"] --> B["0.7 Book UI"]
  B --> C["0.8 Agent + parchment polish"]
  C --> D["0.9 Release prep"]
  D --> E["1.0 🚢 Release"]
  E --> F["1.1 Route charter"]
  F --> H["1.2 Contraband"]
  H --> G["2.0 Storyline"]
```

### v0.6.x — Database per save slot

Each playthrough must own its market history; no cross-save snapshot bleed.

- [ ] Resolve DB path from `SaveSlots.currentSlot` → `data/routier_slot{N}.db`
- [ ] Re-open / swap DB on game load (`SaveLoadManager` post-load hook)
- [ ] Handle legacy `routier.db` (log + ignore or one-time migration note)
- [ ] Web dashboard: slot-aware `database_path` docs / env override
- [ ] Parity script `check_route_parity.py`: accept slot suffix

**Reference:** `SaveSlots.GetCurrentSavePath()` → `slot{N}.save`

### v0.7.x — Route offers as a book (mission-log style)

Replace the flat **Canvas overlay** with a **3D book UI** like the in-game mission log.

- [ ] Reuse patterns from `MissionListUI` (`book`), `MissionDetailsUI`,
  `GPButtonListedMission`
- [ ] Left page: today's offers (tier, kind, price, profit) — up to 6 rows
- [ ] Right page: route detail + buy
- [ ] Modal flow from agent (`MouseLook`, `Refs.SetPlayerControl`)
- [ ] VR: anchor on `PortDude.missionTable` when applicable
- [ ] Retire Canvas UI once book UI is stable

### v0.8.x — Real agent & parchment polish

**Agent** — replace the cube; **parchment** — final manifest quality for 1.0.

**Agent**

- [ ] Hub presence at `PortDude.missionTable` — static figure or `QuestDude`-style
  trigger (no custom rig v1)
- [ ] Opens v0.7 book UI (single purchase flow)
- [ ] `lookText` + short hook ("Route agent — today's manifests")
- [ ] Desk / scroll prop (`GPButtonPortMissions`, `ShipyardDocuments` precedent)
- [ ] Remove `PrimitiveType.Cube` from `HubKioskInstaller`

**Parchment (1.0 quality bar)**

- [ ] Layout pass: no overlap, consistent manifest typography (ongoing)
- [ ] Authenticity seal / stamp (company, issuing port, game day)
- [ ] In-game QA on several hub routes (multi-region, long names, empty legs)

### v0.9.x — Release prep

- [ ] Playtest full loop: 8am generation → agent → book → buy → read parchment → save/load
- [ ] Thunderstore package (icon, README, changelog, version bump)
- [ ] GitHub release + tagged build (`build.ps1`)
- [ ] Config defaults reviewed; breaking changes documented
- [ ] Optional: historical bulletin deferred post-1.0

### v1.0 — 🚢 Release

**Ship when all of the above are done.**

- [ ] Tag `v1.0.0`, publish Thunderstore + GitHub release
- [ ] README / manifest reflect 1.0 feature set
- [ ] Known limitations listed (no charter, contraband, or storyline yet)

**1.0 delivers:** daily route guides at hub ports, save-bound market DB, book-style
offer browser, real agent interactable, polished manifest parchment.

---

## v1.1 — Route charter

**Goal:** buy exclusivity on a **single good** along a **two-island corridor** — AI
traders abstain from that good on those ports for N days.

- [ ] New service tier on top of the route manifest (premium purchase)
- [ ] Harmony patch on `TraderBoat` — skip the chartered good at corridor ports
- [ ] Save-backed expiration (`GameState.day`, "valid until day N" on parchment)
- [ ] Honest limits: `EconCycle`, player trades, and missions still apply (no absolute
  monopoly)
- [ ] Sim / web tool to preview charter value before buying

**Scope:** 1 good, 2 islands (A ↔ B or A → B). Multi-hop charters = later.

---

## v1.2 — Contraband

**Goal:** a **hidden agent** sells high-profit **illegal goods** — separate from the
official route agent at hubs. Selling carries a real risk of being caught by port
authorities.

### Concept

- **Hidden contact** — not at the mission desk; discoverable location (tavern back
  room, night-only dock, reputation-gated whisper, etc.)
- **Contraband manifest** — parchment listing buy port, sell port, good, qty, expected
  profit (higher margin than legal routes)
- **Sell-side risk** — chance (or deterministic checks) of seizure / fine / reputation
  hit when unloading illegal cargo at port
- **No charter overlap** — contraband goods are outside the 1.1 TraderBoat abstention
  model (different ruleset)

### Implementation sketch

- [ ] Catalog of contraband goods per archipelago (subset of `goods_catalog` or mod-tagged
  illegal list)
- [ ] Hidden agent interactable + separate offer UI (book page style or compact list)
- [ ] Route planner variant: legal sim + contraband premium multiplier
- [ ] **Catch on sell** — hook port entry / market sell (`QuestItemDetector` precedent:
  seizure, bribe, reputation penalty)
- [ ] Config: catch probability, fine tiers, reputation damage, time-of-day modifiers
- [ ] Parchment disclaimer: *"Unregistered cargo — authorities may intervene"*
- [ ] Save-backed: active contraband run, heat/cooldown per port (optional)

### Design questions (TBD)

- [ ] Random catch % vs. fixed inspection windows (like vanilla `activeFrom` / `activeUntil`)?
- [ ] Does getting caught affect the **official** route agent's trust (storyline hook for 2.0)?
- [ ] One contraband offer per day vs. player-initiated contact?

**Reference:** `QuestItemDetector` — illegal cargo seizure, bribe, reputation reset at port.

---

## v2.0 — Agent storyline

**Goal:** the route agent becomes a **narrative spine** — unique missions over time
that evolve the player across archipelagos. Aligns with Discord poll (storyline > pure
mechanics).

### Pillars

1. **Recurring agent** — same company / character at hubs (or one archipelago lead per
   region), not a faceless kiosk.
2. **Unique missions** — scripted beats tied to reputation, wealth, or ports visited;
   not just rerolled daily routes.
3. **Progression over time** — unlock tiers, new corridors, harder contracts, story
   branches as `GameState.day` and regions advance.
4. **Archipelago arc** — missions that pull the player between island groups (local →
   regional → cross-archipelago stakes).

### Early design questions (TBD)

- [ ] One global storyline vs. per-archipelago threads?
- [ ] Mission data: JSON mod content vs. DB-driven vs. hybrid?
- [ ] Failure / expiry: do story missions penalize like vanilla cargo missions?
- [ ] Integration with 1.1 charter (story rewards = charter discounts?)

### Out of scope for 2.0 v1

- Full voice acting, cinematics, new 3D character models
- Replacing vanilla mission system entirely

---

## Architecture

```
                    ┌─────────────────────────────────────┐
  v1.0              │  Agent → book UI → buy → parchment  │
                    └─────────────────┬───────────────────┘
                                      │
                              routier_slot{N}.db
                                      │
  v1.1              ┌─────────────────▼───────────────────┐
                    │  + Route charter (TraderBoat patch) │
                    └─────────────────┬───────────────────┘
                                      │
  v1.2              ┌─────────────────▼───────────────────┐
                    │  + Contraband agent & sell risk     │
                    └─────────────────┬───────────────────┘
                                      │
  v2.0              ┌─────────────────▼───────────────────┐
                    │  + Story missions & archipelago arc │
                    └─────────────────────────────────────┘
```

---

## Backlog / references

### Game systems (decompiled v0.38)

| System | Role |
|--------|------|
| `MissionListUI` / `book` | Book-style offer browser (v0.7 → 1.0) |
| `MissionLog` | Journal / log rows |
| `MissionDetailsUI` | Detail page layout |
| `TradeReceiptsUI` | Table columns on panels |
| `ShipItemScroll` | Physical parchment item |
| `SaveSlots` | Per-save DB (v0.6) |
| `QuestDude` / `PortDude` | Agent interaction (v0.8 → 1.0) |
| `TraderBoat` | Charter abstention target (v1.1) |
| `QuestItemDetector` | Illegal cargo seizure / bribe / rep hit (v1.2 contraband) |

### Sim & data hygiene

- [x] `market_globals`, `goods_catalog`, `ports_catalog`
- [x] Bulk sim — beer @ Gold Rock (`sim/fixtures/gold_rock_beer.json`)
- [x] Web Simulator + Route tabs
- [ ] Calibrate `supply` from first observed price (web API, partial)
- [ ] Long-route playtest (e.g. Oasis → Academy → Gold Rock City)

### Post-1.0 ideas (not scheduled)

- Historical price bulletin (1 good, N days, from snapshots)
- Simple price chart as parchment texture
- Per-good specialist agents
- Multi-hop route charter
