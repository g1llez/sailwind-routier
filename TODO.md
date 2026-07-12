# Routier — TODO

## Database

- [ ] **DB per save slot** — Today there's a single database (`BepInEx/plugins/Routier/data/routier.db`) shared by every save. To confirm: do snapshots from one save mix with another? If so, isolate the DB per slot (e.g. `routier_slot1.db`, `routier_slot2.db`, …) by detecting the active slot on the mod side, and align `web/config.json` / `database_path` accordingly.

## Agents & parchments (realistic roadmap — by version)

Services offered by an agent / company in **major cities**. The player **buys** a
service; the deliverable is a **parchment** (data **frozen at purchase time**) that
they keep on hand.

### Target architecture

```
Agent (NPC)  →  purchase + generation from routier.db  →  parchment item/sheet  →  read in-game
```

- Take inspiration from `TradeReceiptsUI` (TextMesh tables, save-backed) rather than
  vanilla `ShipItemScroll` (fixed textures in `ScrollDirectory`)
- JSON payload on the item (`type`, `title`, `game_day`, `rows`, `chart_points`, …) —
  content doesn't track the market after purchase
- **Authenticity seal** on every parchment (visual + metadata): company stamp/seal,
  agent name, issuing port, in-game day — reinforces immersion and distinguishes
  bulletin vs. manifest vs. charter
- Web charts (Chart.js) stay on the dashboard; in-game = **tables** first, a rendered
  curve texture later

### Services

#### 1. Historical bulletin (e.g. rum)

- Average, min/max per port / archipelago, trend over N days
- Source: `port_prices` snapshots
- Content: period, per-port table (avg / min / max / last price), best buy/sell,
  disclaimer "prices as of day X"

#### 2. Trade manifest (purchased route)

- Stops: port A → buy X, port B → sell Y
- Quantities, weight / volume, peak summary (pounds, barrels, crates)
- Estimated profit (**bulk sim** + displayed currency), cost = % of planned profit

#### 3. Route charter (premium on top of the manifest)

- **Not in vanilla** — Harmony patch on `TraderBoat`
- N days (2–5): AI abstains from the **good** at corridor ports
- Expires on `GameState.day`, stamped "valid until day N"
- Honest limit: no absolute monopoly (`EconCycle`, the player, and missions keep going)

### Implementation plan

#### v0.3 — Proof of concept

- [ ] `RoutierAgent` at 1 test port (e.g. Gold Rock) — trigger + dialogue (`QuestDude` /
  `PortDude` pattern, no new 3D model)
- [ ] Catalogue: 1 service (*Bulletin* for one good, N days)
- [ ] Purchase: deduct gold (`PlayerGold`), read `routier.db`, generate payload
- [ ] Parchment UI in the style of `TradeReceiptsUI` (TextMesh, tables) — **no 3D
  object** at first
- [ ] Authenticity seal/stamp (texture or TextMesh: company, port, day)

#### v0.4 — Portable parchment

- [ ] `RoutierParchmentItem` (custom `ShipItem`) spawned on purchase (`SpawnGood` or
  equivalent)
- [ ] Pickup / interact → opens the parchment UI
- [ ] Payload serialization in the save (`SaveablePrefab` + custom data)

#### v0.5 — Manifest + integrated sim

- [ ] Manifest service (route A→B, bulk sim, weight from `goods_catalog`)
- [ ] Service price (% of planned profit)
- [ ] Route charter (Harmony `TraderBoat`, save-backed expiration)

#### v0.6 — Polish

- [ ] Simple chart (generated `Texture2D` → 3D parchment page)
- [ ] 3D seal on the parchment mesh (reuse `parchmentMaterial` / `UISoundPlayer` sounds
  if possible)
- [ ] Multiple agents / archipelagos
- [ ] Specialists per good type (optional)

### Game references (decompiled v0.38)

| System | Role |
|---------|------|
| `TradeReceiptsUI` | UI model + save persistence |
| `ShipItemScroll` | 3D immersion (prefab textures — not dynamic on its own) |
| `QuestDude` / `PortDude` | NPC interaction |
| `Shopkeeper` / `BuyItemUI` | Purchase flow |
| `EconomyUI` | Multi-port price tables |

### Mod data (sim + parchments)

- [x] `market_globals`, `goods_catalog.base_value`, `ports_catalog` — captured on startup
- [x] Bulk sim validated — beer @ Gold Rock (`sim/fixtures/gold_rock_beer.json`)
- [x] Web **Simulator** tab (displayed currency, first-price calibration)
- [ ] Validate a 2nd good (e.g. rum)
- [ ] Calibrate `supply` from the first observed price on the API side (already partly
  done on the web side)

## Route planner (POC)

- [x] `sim/route_optimizer.py` logic — deals constrained per route (buy before sell),
  bulk sim, optimal sell split
- [x] **Sequential** port-by-port planning (sell → cash/weight freed → downstream buy)
- [x] `/api/route-plan` API + web **Route** tab
- [ ] Test with a real route (e.g. Oasis → Academy → Gold Rock City)
- [x] Multiple goods on the same route (same good possible after an intermediate sale)
- [ ] Integrate into the parchment manifest (v0.5)

### Prerequisites before parchments go live

- [ ] **DB per save slot** (see above)
- [ ] Stable bulk sim with higher capital (user testing)
