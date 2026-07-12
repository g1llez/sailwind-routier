# Routier

> Routier adds a trading layer to SailWind — market intel, agent contracts, and a real economic pulse to the archipelago.

*FR : Routier ajoute une couche de commerce à SailWind — analyse de marché, contrats d'agents, et un vrai pouls économique pour l'archipel.*

A **BepInEx** mod for **Sailwind** (0.38+) that turns trade-route planning into an
in-game service: it snapshots market data every in-game hour, generates a handful of
profitable trade routes per hub port each day, and sells them as portable parchment
guides through a route agent at the port. A companion web dashboard reads the same
database for market lookup, charts, and a manual route planner/simulator.

## Features

- **Market snapshots** — trade-post buy/sell prices, supply, currency cross-rates, and
  reputation, captured every in-game hour while time is advancing.
- **Daily route generation** — at a configurable in-game hour, samples and scores
  candidate routes (local + cross-region) per hub port using the same optimizer as the
  offline simulator, and keeps the best few as that day's offers.
- **Route agent kiosk** — an interactable at each hub port opens a screen listing the
  day's offers (tier, price, estimated profit) with a detail panel (full route, hops,
  distance, budget, ROI, cargo weight/volume) before you buy.
- **Parchment guides** — buying an offer spawns a physical, saveable parchment with a
  route summary page and one page per port (cargo discharged/loaded, per-leg distance
  and cargo stats), rendered as manifest-style pages.
- **Web dashboard** — latest snapshot status, currency rates & reputation, per-port
  goods table, price history charts, and a route planner/simulator that mirrors the
  in-game optimizer for testing routes offline.

## Mod

- Database: `BepInEx/plugins/Routier/data/routier.db`
- Route generation and the kiosk UI only activate at hub ports (`Port.hubPort`)

### Build / install

```powershell
.\build.ps1
```

By default it targets `C:\Program Files (x86)\Steam\steamapps\common\Sailwind`; pass
`-GameDir` if your install is elsewhere:

```powershell
.\build.ps1 -GameDir "D:\Games\Sailwind"
```

Config (`BepInEx/config/gaucl.routier.cfg`), generated on first run:

| Section | Key | Default | Notes |
|---|---|---|---|
| Capture | `IntervalGameHours` | `1` | In-game hours between market snapshots |
| Storage | `DatabasePath` | *(empty)* | Override for `routier.db` location |
| Routes | `Enabled` | `true` | Generate + sell daily route guides |
| Routes | `GenerationHour` | `8` | In-game hour routes are (re)generated |
| Routes | `LocalCount` / `RegionalCount` | `4` / `2` | Offers per hub, same-region vs. cross-region |
| Routes | `RoiFloor` | `0.20` | Minimum ROI for an offer to be kept |
| Routes | `BudgetMin` / `BudgetMax` | `2000` / `30000` | Random capital budget range used to plan a route |
| Routes | `HopsMin` / `HopsMax` | `3` / `5` | Ports per route, including the hub |
| Routes | `SamplesPerList` | `24` | Candidate routes evaluated per offer before keeping the best |

## Web dashboard

```powershell
cd web
pip install -r requirements.txt
python server.py
```

Open http://127.0.0.1:8765

Edit `web/config.json` if your `routier.db` path or port differs.

Tabs: market lookup (prices/supply per port + history chart), currency & reputation,
**Route** planner (build/score a route against live snapshot data), and **Simulator**
(validate the bulk-pricing model against real in-game measurements).

## Project layout

```
Routier/
├── src/               # BepInEx mod (C#)
│   ├── Plugin.cs / PluginInfo.cs
│   ├── Market/        # Snapshots, SQLite, goods/ports catalog
│   ├── Routes/        # Route generator, optimizer, daily catalog
│   ├── Parchment/     # Page models, rendering, save-backed scrolls
│   ├── UI/            # Route agent kiosk + offer selection screen
│   └── Patches/       # Harmony patches (save, ship items, scrolls)
├── Routier.csproj
├── build.ps1          # Build + install into a local Sailwind copy
├── manifest.json      # Thunderstore package manifest
├── sim/               # Offline Python port of the market model + route optimizer
│   ├── market_model.py
│   ├── route_optimizer.py
│   ├── fixtures/      # JSON cases captured from real in-game measurements
│   └── tests/         # Unit tests + parity check vs. the C# optimizer
└── web/               # Flask dashboard reading the same SQLite database
    ├── server.py
    ├── route_planner.py / market_sim.py   # Mirrors sim/ for the web UI
    └── templates/, static/
```

See `sim/README.md` for the offline market simulator and `TODO.md` for the roadmap.

## License

MIT — see [LICENSE](LICENSE).
