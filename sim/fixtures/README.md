# Fixtures — données in-game pour valider le simulateur

Copie `example_case.json` et remplis avec tes mesures en jeu.

## Champs utiles

| Champ | Description |
|-------|-------------|
| `supply_start` | `currentSupply` du good à l'île (voir snapshot Routier `supply` ou UI) |
| `supply_purchase_limit` | Seuil d'achat de l'île (souvent 0, parfois autre) |
| `calibrate_from.buy_raw` | Prix d'achat affiché **avant** le 1er achat (raw / en devise port si tu convertis après) |
| `quantity` | Nombre d'unités à simuler |
| `expected_unit_prices` | Liste des prix réels en jeu `[p1, p2, …]` ou `null` pour explorer seulement |
| `expected_total` | Somme réelle payée |

## Lancer

Depuis la racine du repo :

```bash
python sim/tests/run_case.py sim/fixtures/ton_case.json
```

## Notes

- Routier stocke `buy_raw` / `sell_raw` **avant** conversion devise affichée.
- Pour comparer à l'UI, utilise les mêmes règles que le dashboard ou note les prix en devise du port.
- `goods_soft_cap` et `positive_price_mult` viennent du jeu global — on les ajustera si la calibration ne colle pas.
