"""Collect all root-ish transforms in GRC bbox across all levels."""
from __future__ import annotations

import math

import UnityPy

from sailwind_paths import GAME_DATA

X0, X1 = 340, 410
Z0, Z1 = -60, -10


def main():
    rows = []
    for level in sorted(GAME_DATA.glob("level*")):
        if level.suffix:
            continue
        try:
            env = UnityPy.load(str(level))
        except Exception:
            continue
        for obj in env.objects:
            if obj.type.name != "Transform":
                continue
            d = obj.read()
            p = d.m_LocalPosition
            if not (X0 <= p.x <= X1 and Z0 <= p.z <= Z1):
                continue
            if p.y < -2 or p.y > 20:
                continue
            rows.append((level.name, "", p.x, p.y, p.z))

    rows.sort(key=lambda r: (r[0], r[3], r[2]))
    print(f"{'level':<8} {'name':<30} {'x':>9} {'y':>8} {'z':>9}")
    print("-" * 70)
    for level, name, x, y, z in rows:
        print(f"{level:<8} {(name or '<unnamed>')[:30]:<30} {x:9.3f} {y:8.3f} {z:9.3f}")

    print("\nY value clusters (all levels):")
    from collections import Counter
    c = Counter(round(r[3], 1) for r in rows)
    for y, n in sorted(c.items()):
        print(f"  Y={y:5.1f}  n={n}")


if __name__ == "__main__":
    main()
