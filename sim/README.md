# Routier — market simulation

Port of Sailwind `IslandMarket` pricing (v0.38) for offline tests.

## What it models

- `currentSupply` → mid price via quadratic curve (`goods_soft_cap`, `base_value`)
- Buy spread / sell spread (`GetSpread`)
- Sequential **buy**: price at current supply, then `supply -= 1`
- Sequential **sell**: price at current supply, then `supply += 1`

## Quick start

From the repo root:

```bash
python -m unittest sim/tests/test_market_model.py
python sim/tests/run_case.py sim/fixtures/example_case.json
```

## Your in-game data

Add a JSON file under `sim/fixtures/` (see `sim/fixtures/README.md`).

Minimum for buy simulation:

1. `supply_start` from snapshot or economy UI
2. First `buy_raw` (or display price + currency rules)
3. `quantity` to test
4. Optional: list of real unit prices after each click in `expected_unit_prices`

`calibrate_from` solves `base_value` from one (supply, buy) observation so you don't need `item.value` from game files.

## Files

| File | Role |
|------|------|
| `market_model.py` | Core formulas + `simulate_buy` / `simulate_sell` |
| `tests/run_case.py` | CLI runner for JSON fixtures |
| `tests/test_market_model.py` | Sanity checks on curve shape |
| `fixtures/*.json` | Cases you fill with real measurements |
